using Microsoft.Extensions.Options;

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

/// <summary>Global behaviour of the query, command and render services.</summary>
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
    public int CompiledTemplateCacheSize { get; set; } = 1024;

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
    }
}
