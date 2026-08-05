using Microsoft.Extensions.Options;
using Scriban;

namespace Templar;

/// <summary>What to do when a template uses a placeholder that no value was supplied for.</summary>
public enum MissingVariableBehavior
{
    /// <summary>Throw a <see cref="TemplateRenderException"/>. The default: a missing value is a bug.</summary>
    Throw = 0,

    /// <summary>Replace the placeholder with an empty string.</summary>
    Empty = 1,

    /// <summary>Leave the placeholder text (<c>{{name}}</c>) in the output.</summary>
    Keep = 2,
}

/// <summary>
/// Global behaviour of the query, command and render services, and of the Scriban engine
/// <c>AddTemplar</c> registers. Everything is configured in the one place:
/// <c>services.AddTemplar(options => …)</c>.
/// </summary>
public sealed class TemplateOptions
{
    /// <summary>
    /// Culture used when a request does not specify one, and the last resort of the fallback chain.
    /// Defaults to <c>en</c>.
    /// </summary>
    public string DefaultCulture { get; set; } = "en";

    /// <summary>
    /// When true (the default) a request for <c>vi-VN</c> falls back to <c>vi</c> and then to
    /// <see cref="DefaultCulture"/>. When false only an exact culture match is accepted.
    /// </summary>
    public bool EnableCultureFallback { get; set; } = true;

    /// <summary>Cache templates read from the database. Defaults to true.</summary>
    public bool EnableCache { get; set; } = true;

    /// <summary>How long a template stays cached. Defaults to five minutes.</summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Key prefix used by <c>DistributedTemplateCache</c>, so several applications can share one
    /// Redis instance. Ignored by the in-process cache. Defaults to <c>templar:</c>.
    /// </summary>
    public string CacheKeyPrefix { get; set; } = "templar:";

    /// <summary>How to treat placeholders with no matching value. Defaults to <see cref="MissingVariableBehavior.Throw"/>.</summary>
    public MissingVariableBehavior MissingVariableBehavior { get; set; } = MissingVariableBehavior.Throw;

    /// <summary>
    /// HTML-encode substituted values when rendering an HTML body. Defaults to true; turning this
    /// off allows HTML injection from template values.
    /// </summary>
    public bool HtmlEncodeValues { get; set; } = true;

    /// <summary>Maximum number of compiled templates kept in memory. Defaults to 1024.</summary>
    /// <remarks>Read by the compiler, which clears the whole cache at the limit rather than evicting.</remarks>
    public int CompiledTemplateCacheSize { get; set; } = 1024;

    /// <summary>
    /// Parse templates as Liquid rather than Scriban, for bodies migrated from Shopify/Jekyll.
    /// Defaults to false. Liquid loses Scriban's pipes and the <c>format</c> function.
    /// </summary>
    public bool UseLiquidSyntax { get; set; }

    /// <summary>
    /// Maximum iterations a single <c>for</c> or <c>while</c> may run. Defaults to 1000. Templates
    /// come from a database, so this is what stops one bad row from hanging a request thread.
    /// </summary>
    public int LoopLimit { get; set; } = 1000;

    /// <summary>Maximum nesting depth for template functions. Defaults to 100.</summary>
    public int RecursiveLimit { get; set; } = 100;

    /// <summary>Timeout applied to the <c>regex</c> functions. Defaults to one second.</summary>
    public TimeSpan RegexTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Let <c>{{ user.middle_name }}</c>, <c>{{ absent.anything }}</c> and <c>{{ list[9] }}</c> yield
    /// nothing instead of raising an error. Defaults to true, which is what makes
    /// <c>{{ if user.middle_name }}</c> usable on optional fields. Unlike
    /// <see cref="MissingVariableBehavior"/> this governs member, target and indexer access rather
    /// than top-level names — a missing name is still reported, once, with all the others.
    /// </summary>
    public bool RelaxedMemberAccess { get; set; } = true;

    /// <summary>
    /// Fall back to <see cref="TemplateVariableNameComparer"/> when a member name does not match
    /// exactly, so <c>{{ user.FirstName }}</c>, <c>{{ user.first_name }}</c> and
    /// <c>{{ user.firstname }}</c> all reach the same property. Defaults to true. Turn it off for
    /// Scriban's own convention only, which is <c>first_name</c>.
    /// </summary>
    public bool MemberNameFallback { get; set; } = true;

    /// <summary>
    /// Reject <c>{{DATE:dd/MM/yyyy}}</c> — the format syntax Templar 1.0 used — at compile time, with
    /// a message pointing at <c>{{ DATE | format 'dd/MM/yyyy' }}</c>. Defaults to true: Scriban does
    /// not treat that shape as an error, it renders it as an empty string, so without this a template
    /// carried over from 1.0 loses the value silently.
    /// </summary>
    public bool RejectLegacyFormatSyntax { get; set; } = true;

    /// <summary>
    /// Functions the templates can call, on top of Scriban's builtins and Templar's <c>format</c> and
    /// <c>raw</c>. Register them once where the container is configured; a template then calls one by
    /// name — <c>{{ slugify title }}</c> — or pipes into it — <c>{{ title | slugify }}</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Names match through <see cref="TemplateVariableNameComparer"/> like everything else, so
    /// <c>slugify</c> registered here answers to <c>{{ Slugify … }}</c> and <c>{{ slug_ify … }}</c>
    /// too. Any delegate shape works: Scriban binds the template's arguments to its parameters and
    /// converts them, so a <c>Func&lt;decimal, string&gt;</c> is called with a number from the
    /// template. Values shadow functions — a value named <c>slugify</c> wins over one registered here.
    /// </para>
    /// <para>
    /// The delegates are shared by every render on every thread, so they must not close over
    /// per-request state; take what they need as parameters. For anything this cannot express — a
    /// whole namespace of functions, a <c>TemplateLoader</c> for <c>{{ include }}</c> — use
    /// <see cref="ConfigureContext"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddTemplar(options =>
    ///         {
    ///             options.Functions["vnd"] = (decimal amount) => $"{amount:N0} ₫";
    ///             options.Functions["mask"] = (string? value) => value is null or "" ? "" : $"****{value[^4..]}";
    ///         })
    ///         .UsePostgreSql(connectionString);
    /// </code>
    /// </example>
    public IDictionary<string, Delegate> Functions { get; } =
        new Dictionary<string, Delegate>(TemplateVariableNameComparer.Instance);

    /// <summary>
    /// Called on every <see cref="TemplateContext"/> before it renders, after Templar has applied
    /// everything above. Use it to add functions, a <c>TemplateLoader</c> for <c>{{ include }}</c>,
    /// or any Scriban setting this class does not expose.
    /// </summary>
    public Action<TemplateContext>? ConfigureContext { get; set; }

    /// <summary>
    /// Unwraps and validates configured options in one expression, so a primary constructor can
    /// still fail on bad configuration at construction time.
    /// </summary>
    internal static TemplateOptions Validated(IOptions<TemplateOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value;
        value.Validate();
        return value;
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(DefaultCulture))
            throw new InvalidOperationException($"{nameof(TemplateOptions)}.{nameof(DefaultCulture)} must be set.");
        if (CacheDuration <= TimeSpan.Zero)
            throw new InvalidOperationException($"{nameof(TemplateOptions)}.{nameof(CacheDuration)} must be positive.");
        if (CompiledTemplateCacheSize <= 0)
            throw new InvalidOperationException($"{nameof(TemplateOptions)}.{nameof(CompiledTemplateCacheSize)} must be positive.");
        if (CacheKeyPrefix is null)
            throw new InvalidOperationException($"{nameof(TemplateOptions)}.{nameof(CacheKeyPrefix)} cannot be null; use an empty string for no prefix.");
        if (LoopLimit <= 0)
            throw new InvalidOperationException($"{nameof(TemplateOptions)}.{nameof(LoopLimit)} must be positive.");
        if (RecursiveLimit <= 0)
            throw new InvalidOperationException($"{nameof(TemplateOptions)}.{nameof(RecursiveLimit)} must be positive.");
        if (RegexTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException($"{nameof(TemplateOptions)}.{nameof(RegexTimeout)} must be positive.");

        foreach (var (name, function) in Functions)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException(
                    $"{nameof(TemplateOptions)}.{nameof(Functions)} has an entry with a blank name.");
            if (function is null)
                throw new InvalidOperationException(
                    $"{nameof(TemplateOptions)}.{nameof(Functions)}['{name}'] is null.");
        }
    }
}
