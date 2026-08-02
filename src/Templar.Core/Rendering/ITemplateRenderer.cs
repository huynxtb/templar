namespace Templar.Rendering;

/// <summary>Substitutes values into a compiled template.</summary>
public interface ITemplateRenderer
{
    /// <summary>Renders <paramref name="template"/> with <paramref name="values"/>.</summary>
    /// <exception cref="TemplateRenderException">
    /// A placeholder had no value and the context asks for
    /// <see cref="MissingVariableBehavior.Throw"/>, or a supplied format string is invalid.
    /// </exception>
    string Render(CompiledTemplate template, TemplateValues values, in TemplateRenderContext context);
}
