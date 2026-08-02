namespace Templar;

/// <summary>Base type for every error raised by Templar.</summary>
public abstract class TemplateException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>No active template matched the requested key, channel and culture chain.</summary>
public sealed class TemplateNotFoundException(string templateKey, string culture, TemplateChannel channel)
    : TemplateException($"No active template '{templateKey}' for channel '{channel}' was found for culture '{culture}' or any of its fallbacks.")
{
    public string TemplateKey { get; } = templateKey;
    public string Culture { get; } = culture;
    public TemplateChannel Channel { get; } = channel;
}

/// <summary>The template was found but could not be rendered.</summary>
public sealed class TemplateRenderException(
    string message,
    IReadOnlyList<string>? missingVariables = null,
    Exception? innerException = null) : TemplateException(message, innerException)
{
    /// <summary>Placeholder names that had no supplied value, when that was the cause.</summary>
    public IReadOnlyList<string> MissingVariables { get; } = missingVariables ?? [];
}
