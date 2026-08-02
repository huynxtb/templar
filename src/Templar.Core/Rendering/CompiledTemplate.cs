namespace Templar.Rendering;

/// <summary>A literal chunk of text, or a placeholder to substitute.</summary>
internal readonly struct TemplateSegment
{
    /// <summary>Literal text, or for a placeholder the original token including the braces.</summary>
    public required string Text { get; init; }

    /// <summary>Placeholder name, or <see langword="null"/> when this segment is literal text.</summary>
    public string? Name { get; init; }

    /// <summary>Optional format string taken from <c>{{name:format}}</c>.</summary>
    public string? Format { get; init; }

    public bool IsLiteral => Name is null;
}

/// <summary>
/// A template that has been parsed once into literal and placeholder segments. Compiling is
/// cached, so rendering the same body repeatedly does not re-scan the source text.
/// </summary>
public sealed class CompiledTemplate
{
    internal CompiledTemplate(string source, TemplateSegment[] segments, string[] variableNames)
    {
        Source = source;
        Segments = segments;
        VariableNames = variableNames;
    }

    /// <summary>The original template text.</summary>
    public string Source { get; }

    /// <summary>Distinct placeholder names found in the template, in order of first appearance.</summary>
    public IReadOnlyList<string> VariableNames { get; }

    internal TemplateSegment[] Segments { get; }

    /// <summary>True when the template contains no placeholders at all.</summary>
    public bool IsStatic => VariableNames.Count == 0;
}
