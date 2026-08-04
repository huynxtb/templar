using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;
using Templar.Rendering;

namespace Templar.Scriban;

/// <summary>
/// Renders a <see cref="ScribanCompiledTemplate"/>: values HTML-encoded in an HTML body unless
/// wrapped in <see cref="TemplateRaw"/>, numbers and dates formatted in the template's culture, and
/// every missing name reported in one error.
/// </summary>
public sealed class ScribanTemplateRenderer(
    ScribanOptions scribanOptions,
    HtmlEncoder? htmlEncoder = null) : ITemplateRenderer
{
    /// <summary>
    /// Escapes the markup-significant characters but passes other Unicode through unchanged.
    /// <see cref="HtmlEncoder.Default"/> would turn every non-ASCII letter into a numeric entity,
    /// which for a multi-language library means "Chào mừng" arriving as
    /// <c>Ch&amp;#xE0;o m&amp;#x1EEB;ng</c> — correct but unreadable, and considerably larger.
    /// </summary>
    public static HtmlEncoder UnicodeFriendlyEncoder { get; } =
        HtmlEncoder.Create(new TextEncoderSettings(UnicodeRanges.All));

    private readonly ScribanOptions _options = scribanOptions ?? throw new ArgumentNullException(nameof(scribanOptions));
    private readonly HtmlEncoder _htmlEncoder = htmlEncoder ?? UnicodeFriendlyEncoder;

    // Culture-independent, unlike the builtins, so it is built once rather than per render.
    private readonly TemplateFunctionsScriptObject? _functions = scribanOptions?.Functions.Count > 0
        ? TemplateFunctionsScriptObject.Create(scribanOptions.Functions)
        : null;

    public string Render(CompiledTemplate template, TemplateValues values, in TemplateRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        if (template is not ScribanCompiledTemplate scriban)
            throw new TemplateRenderException(
                $"{nameof(ScribanTemplateRenderer)} cannot render a template compiled by " +
                $"{template.GetType().Name}. Register one engine's compiler and renderer together.");

        if (scriban.IsStatic) return scriban.Source;

        var behavior = context.MissingVariableBehavior;
        List<string>? missing = null;

        var script = new TemplarScriptContext(context.HtmlEncode ? _htmlEncoder : null)
        {
            // Missing names are handled by TryGetVariable below instead, so that every one of them
            // is reported in a single error rather than only the first.
            StrictVariables = false,

            // All three together, or a missing name would still blow up on the first `{{ x.y }}`
            // that reads through it — which would both defeat MissingVariableBehavior.Empty and
            // pre-empt the collected report below with Scriban's own member-access error.
            EnableRelaxedMemberAccess = _options.RelaxedMemberAccess,
            EnableRelaxedTargetAccess = _options.RelaxedMemberAccess,
            EnableRelaxedIndexerAccess = _options.RelaxedMemberAccess,

            LoopLimit = _options.LoopLimit,
            RecursiveLimit = _options.RecursiveLimit,
            RegexTimeOut = _options.RegexTimeout,
            TryGetVariable = (TemplateContext _, SourceSpan _, ScriptVariable variable, out object? value) =>
            {
                switch (behavior)
                {
                    case MissingVariableBehavior.Keep:
                        value = $"{{{{{variable.Name}}}}}";
                        return true;
                    case MissingVariableBehavior.Empty:
                        value = null;
                        return true;
                    default:
                        (missing ??= []).Add(variable.Name);
                        value = null;
                        return true;
                }
            },
        };

        if (_options.MemberNameFallback) script.TryGetMember = TryGetMemberByComparer;

        script.PushCulture(context.Culture);
        script.PushGlobal(ScribanFunctions.Create(context.Culture));

        // After the builtins so a caller can replace `format`, before the values so a value still
        // shadows a function of the same name.
        if (_functions is not null) script.PushGlobal(_functions);

        script.PushGlobal(new TemplateValuesScriptObject(values));

        _options.ConfigureContext?.Invoke(script);

        // Scriban's own conversions follow the culture pushed above, but a delegate in
        // ScribanOptions.Functions doing $"{amount:N0}" reads the ambient one. Align them, or the
        // template's culture would silently stop applying at the boundary of a caller's function.
        var ambient = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = context.Culture;

        string output;
        try
        {
            output = scriban.Template.Render(script);
        }
        catch (ScriptRuntimeException exception)
        {
            var where = context.Description is null ? string.Empty : $" in '{context.Description}'";
            throw new TemplateRenderException($"{exception.OriginalMessage}{where}.", innerException: exception);
        }
        finally
        {
            CultureInfo.CurrentCulture = ambient;
        }

        if (missing is { Count: > 0 })
        {
            var where = context.Description is null ? string.Empty : $" in '{context.Description}'";
            throw new TemplateRenderException(
                $"No value was supplied for placeholder(s) {string.Join(", ", missing.Select(m => $"{{{{{m}}}}}"))}{where}.",
                missing);
        }

        return output;
    }

    /// <summary>
    /// Scriban's default renamer maps a CLR <c>FirstName</c> to <c>first_name</c> and nothing else.
    /// This runs only after that exact match has failed, so it costs nothing on the common path and
    /// extends <see cref="TemplateVariableNameComparer"/>'s promise to member access:
    /// <c>{{ user.FirstName }}</c> and <c>{{ user.firstname }}</c> reach the same property.
    /// </summary>
    private static bool TryGetMemberByComparer(
        TemplateContext context,
        SourceSpan span,
        object target,
        string member,
        out object? value)
    {
        value = null;
        if (target is null) return false;

        var accessor = context.GetMemberAccessor(target);
        foreach (var candidate in accessor.GetMembers(context, span, target))
        {
            if (TemplateVariableNameComparer.Instance.Equals(candidate, member))
                return accessor.TryGetValue(context, span, target, candidate, out value);
        }

        return false;
    }
}

/// <summary>The functions Templar adds on top of Scriban's own builtins.</summary>
internal static class ScribanFunctions
{
    public static ScriptObject Create(CultureInfo culture)
    {
        var functions = new ScriptObject();

        // A .NET format string applied in the template's culture, not the request's — Scriban's own
        // date.to_string takes strftime, and this is also the replacement for the legacy
        // {{DATE:dd/MM/yyyy}} syntax.
        functions.Import("format", new Func<object?, string?, object?>((value, format) => value switch
        {
            null => null,
            IFormattable formattable => Format(formattable, format, culture),
            _ => value.ToString(),
        }));

        functions.Import("raw", new Func<object?, TemplateRaw>(value =>
            TemplateRaw.Html(value?.ToString() ?? string.Empty)));

        return functions;
    }

    private static string Format(IFormattable value, string? format, CultureInfo culture)
    {
        try
        {
            return value.ToString(format, culture);
        }
        catch (FormatException exception)
        {
            throw new TemplateRenderException(
                $"Format string '{format}' is not valid for a value of type {value.GetType().Name}.",
                innerException: exception);
        }
    }
}
