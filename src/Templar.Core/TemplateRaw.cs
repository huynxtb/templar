namespace Templar;

/// <summary>
/// Marks a value as pre-encoded so it is inserted into an HTML body verbatim instead of being
/// HTML-encoded. Only use this for markup you control.
/// </summary>
/// <example>
/// <code>
/// values.Set("CTA", TemplateRaw.Html("&lt;a href=\"https://x\"&gt;Confirm&lt;/a&gt;"));
/// </code>
/// </example>
public sealed class TemplateRaw
{
    private TemplateRaw(string value) => Value = value;

    /// <summary>The verbatim markup.</summary>
    public string Value { get; }

    /// <summary>Wraps already-encoded markup.</summary>
    public static TemplateRaw Html(string value) => new(value ?? string.Empty);

    public override string ToString() => Value;
}
