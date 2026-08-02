using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace Templar.Rendering;

/// <summary>
/// Default renderer. Values are formatted with the target culture and, for HTML bodies,
/// HTML-encoded unless wrapped in <see cref="TemplateRaw"/>.
/// </summary>
public sealed class TemplateRenderer(HtmlEncoder? htmlEncoder = null) : ITemplateRenderer
{
    /// <summary>
    /// Escapes the markup-significant characters but passes other Unicode through unchanged.
    /// <see cref="HtmlEncoder.Default"/> would turn every non-ASCII letter into a numeric entity,
    /// which for a multi-language library means "Chào mừng" arriving as
    /// <c>Ch&amp;#xE0;o m&amp;#x1EEB;ng</c> — correct but unreadable, and considerably larger.
    /// </summary>
    public static HtmlEncoder UnicodeFriendlyEncoder { get; } =
        HtmlEncoder.Create(new TextEncoderSettings(UnicodeRanges.All));

    private readonly HtmlEncoder _htmlEncoder = htmlEncoder ?? UnicodeFriendlyEncoder;

    public string Render(CompiledTemplate template, TemplateValues values, in TemplateRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        if (template.IsStatic) return template.Source;

        var builder = new StringBuilder(template.Source.Length + 64);
        List<string>? missing = null;

        foreach (var segment in template.Segments)
        {
            if (segment.IsLiteral)
            {
                builder.Append(segment.Text);
                continue;
            }

            var name = segment.Name!;
            if (!values.TryGetValue(name, out var value))
            {
                switch (context.MissingVariableBehavior)
                {
                    case MissingVariableBehavior.Keep:
                        builder.Append(segment.Text);
                        break;
                    case MissingVariableBehavior.Empty:
                        break;
                    default:
                        (missing ??= []).Add(name);
                        break;
                }

                continue;
            }

            AppendValue(builder, value, segment.Format, context);
        }

        if (missing is { Count: > 0 })
        {
            var where = context.Description is null ? string.Empty : $" in '{context.Description}'";
            throw new TemplateRenderException(
                $"No value was supplied for placeholder(s) {string.Join(", ", missing.Select(m => $"{{{{{m}}}}}"))}{where}.",
                missing);
        }

        return builder.ToString();
    }

    private void AppendValue(StringBuilder builder, object? value, string? format, in TemplateRenderContext context)
    {
        switch (value)
        {
            case null:
                return;

            case TemplateRaw raw:
                builder.Append(raw.Value);
                return;

            case string text:
                Append(builder, text, context);
                return;
        }

        string formatted;
        if (value is IFormattable formattable)
        {
            try
            {
                formatted = formattable.ToString(format, context.Culture);
            }
            catch (FormatException ex)
            {
                throw new TemplateRenderException(
                    $"Format string '{format}' is not valid for a value of type {value.GetType().Name}.",
                    innerException: ex);
            }
        }
        else
        {
            formatted = Convert.ToString(value, context.Culture) ?? string.Empty;
        }

        Append(builder, formatted, context);
    }

    private void Append(StringBuilder builder, string text, in TemplateRenderContext context)
    {
        if (text.Length == 0) return;

        // HtmlEncoder returns the same instance when nothing needs escaping, so this does not
        // allocate for the common case.
        builder.Append(context.HtmlEncode ? _htmlEncoder.Encode(text) : text);
    }
}
