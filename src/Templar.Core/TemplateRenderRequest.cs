using System.Diagnostics.CodeAnalysis;

namespace Templar;

/// <summary>
/// Describes what to render: which template, in which language, with which values.
/// </summary>
public sealed class TemplateRenderRequest
{
    public TemplateRenderRequest() { }

    /// <summary>Shorthand for the common case of rendering every part of one template.</summary>
    [SetsRequiredMembers]
    public TemplateRenderRequest(string templateKey, string? culture = null, TemplateValues? values = null)
    {
        TemplateKey = templateKey;
        Culture = culture;
        Values = values ?? TemplateValues.Empty;
    }

    /// <summary>Logical template name, for example <c>welcome-user</c>.</summary>
    public required string TemplateKey { get; init; }

    /// <summary>
    /// Requested culture. When <see langword="null"/> the configured
    /// <see cref="TemplateOptions.DefaultCulture"/> is used.
    /// </summary>
    public string? Culture { get; init; }

    /// <summary>Channel to render. Defaults to <see cref="TemplateChannel.Email"/>.</summary>
    public TemplateChannel Channel { get; init; } = TemplateChannel.Email;

    /// <summary>Placeholder values substituted into the template.</summary>
    public TemplateValues Values { get; init; } = TemplateValues.Empty;

    /// <summary>Which parts to render. Defaults to <see cref="TemplateParts.All"/>.</summary>
    public TemplateParts Parts { get; init; } = TemplateParts.All;

    /// <summary>
    /// Overrides <see cref="TemplateOptions.MissingVariableBehavior"/> for this call only.
    /// </summary>
    public MissingVariableBehavior? MissingVariableBehavior { get; init; }
}
