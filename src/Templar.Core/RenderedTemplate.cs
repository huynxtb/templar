namespace Templar;

/// <summary>
/// The result of rendering a <see cref="TemplateDefinition"/> with a set of values.
/// </summary>
public sealed class RenderedTemplate
{
    /// <summary>Key of the template that was rendered.</summary>
    public required string TemplateKey { get; init; }

    /// <summary>Culture actually used, which may differ from the requested one after fallback.</summary>
    public required string Culture { get; init; }

    /// <summary>Channel of the rendered template.</summary>
    public TemplateChannel Channel { get; init; }

    /// <summary>Rendered subject / title, or <see langword="null"/> when absent or not requested.</summary>
    public string? Subject { get; init; }

    /// <summary>Rendered plain-text body, or <see langword="null"/> when absent or not requested.</summary>
    public string? Text { get; init; }

    /// <summary>Rendered HTML body, or <see langword="null"/> when absent or not requested.</summary>
    public string? Html { get; init; }

    /// <summary>True when a plain-text body was produced.</summary>
    public bool HasText => !string.IsNullOrEmpty(Text);

    /// <summary>True when an HTML body was produced.</summary>
    public bool HasHtml => !string.IsNullOrEmpty(Html);
}
