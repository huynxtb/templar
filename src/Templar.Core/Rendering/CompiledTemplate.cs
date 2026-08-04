namespace Templar.Rendering;

/// <summary>
/// A template that has been parsed once. Compiling is cached, so rendering the same body repeatedly
/// does not re-scan the source text.
/// </summary>
/// <remarks>
/// Each rendering engine derives its own type — the Scriban compiler stores a parsed Scriban page —
/// so an <see cref="ITemplateRenderer"/> only understands templates from its own
/// <see cref="ITemplateCompiler"/>. Anything replacing one half has to replace the other too.
/// </remarks>
public abstract class CompiledTemplate(string source, IReadOnlyList<string> variableNames)
{
    /// <summary>The original template text.</summary>
    public string Source { get; } = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>Distinct placeholder names found in the template, in order of first appearance.</summary>
    public IReadOnlyList<string> VariableNames { get; } =
        variableNames ?? throw new ArgumentNullException(nameof(variableNames));

    /// <summary>True when rendering the template can only ever produce <see cref="Source"/>.</summary>
    public virtual bool IsStatic => VariableNames.Count == 0;
}
