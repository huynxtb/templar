using System.Globalization;

namespace Templar.Rendering;

/// <summary>How a single part of a template should be rendered.</summary>
public readonly struct TemplateRenderContext(
    CultureInfo culture,
    bool htmlEncode,
    MissingVariableBehavior missingVariableBehavior,
    string? description = null)
{
    /// <summary>Culture used to format dates, numbers and other <see cref="IFormattable"/> values.</summary>
    public CultureInfo Culture { get; } = culture;

    /// <summary>HTML-encode substituted values. True for HTML bodies, false for text and subjects.</summary>
    public bool HtmlEncode { get; } = htmlEncode;

    /// <summary>What to do about placeholders with no supplied value.</summary>
    public MissingVariableBehavior MissingVariableBehavior { get; } = missingVariableBehavior;

    /// <summary>Human-readable origin (for example <c>welcome-user/vi/Html</c>) used in error messages.</summary>
    public string? Description { get; } = description;
}
