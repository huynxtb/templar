namespace Templar.Rendering;

/// <summary>Parses raw template text into a reusable <see cref="CompiledTemplate"/>.</summary>
public interface ITemplateCompiler
{
    /// <summary>
    /// Compiles <paramref name="source"/>. Implementations are expected to be thread-safe and to
    /// cache their results, so calling this on every render is cheap.
    /// </summary>
    CompiledTemplate Compile(string source);
}
