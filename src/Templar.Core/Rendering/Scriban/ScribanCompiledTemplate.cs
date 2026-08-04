using Templar.Rendering;
using ScribanTemplate = Scriban.Template;

namespace Templar.Scriban;

/// <summary>A template parsed by Scriban. Produced by <see cref="ScribanTemplateCompiler"/>.</summary>
public sealed class ScribanCompiledTemplate : CompiledTemplate
{
    internal ScribanCompiledTemplate(string source, ScribanTemplate template, string[] variableNames, bool isStatic)
        : base(source, variableNames)
    {
        Template = template;
        IsStatic = isStatic;
    }

    /// <summary>
    /// True when the page is nothing but literal text. This cannot be derived from
    /// <see cref="CompiledTemplate.VariableNames"/> the way the base class does — <c>{{ 2 + 2 }}</c>
    /// and <c>{{ include 'x' }}</c> reference no variable but still produce output.
    /// </summary>
    public override bool IsStatic { get; }

    internal ScribanTemplate Template { get; }
}
