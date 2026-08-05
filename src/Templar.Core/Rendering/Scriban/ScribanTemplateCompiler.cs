using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Scriban.Parsing;
using Scriban.Syntax;
using Templar.Rendering;
using ScribanTemplate = Scriban.Template;

namespace Templar.Scriban;

/// <summary>
/// Compiles template bodies with Scriban: <c>{{ name }}</c> substitution plus <c>{{ if }}</c> /
/// <c>{{ else }}</c>, <c>{{ for }}</c> over a collection, pipes and the built-in <c>date</c> /
/// <c>string</c> / <c>math</c> / <c>array</c> functions.
/// </summary>
/// <remarks>
/// <c>AddTemplar()</c> registers it together with <see cref="ScribanTemplateRenderer"/>, which is the
/// only renderer that understands a <see cref="ScribanCompiledTemplate"/>.
/// </remarks>
public sealed partial class ScribanTemplateCompiler(TemplateOptions options) : ITemplateCompiler
{
    private readonly TemplateOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ConcurrentDictionary<string, CompiledTemplate> _cache = new(StringComparer.Ordinal);
    private readonly int _cacheSize = Math.Max(1, options?.CompiledTemplateCacheSize ?? 1024);

    /// <summary>
    /// A whole token that is an identifier followed by <c>:</c> and a format — the
    /// <c>{{DATE:dd/MM/yyyy}}</c> shape Templar 1.0 used. Scriban's ternary (<c>a ? b : c</c>),
    /// object literals and named arguments (<c>fn arg: 1</c>) all fail to match, so this only fires
    /// on the legacy syntax.
    /// </summary>
    [GeneratedRegex(@"\{\{\s*([A-Za-z_][A-Za-z0-9_.-]*)\s*:\s*([^{}|?\r\n]+?)\s*\}\}")]
    private static partial Regex LegacyFormatToken();

    public CompiledTemplate Compile(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (_cache.TryGetValue(source, out var cached)) return cached;

        var compiled = Parse(source);

        // Coarse bound: the working set is the number of distinct bodies, so dropping everything at
        // the limit beats tracking per-entry recency.
        if (_cache.Count >= _cacheSize) _cache.Clear();
        _cache[source] = compiled;

        return compiled;
    }

    private ScribanCompiledTemplate Parse(string source)
    {
        if (_options.RejectLegacyFormatSyntax)
        {
            var legacy = LegacyFormatToken().Match(source);
            if (legacy.Success)
                throw new TemplateCompilationException(
                    $"'{legacy.Value}' is the legacy Templar 1.0 format syntax. Scriban does not reject it, " +
                    $"it renders it as an empty string, so the value would be lost silently. Write " +
                    $"{{{{ {legacy.Groups[1].Value} | format '{legacy.Groups[2].Value}' }}}} instead, or set " +
                    $"{nameof(TemplateOptions)}.{nameof(TemplateOptions.RejectLegacyFormatSyntax)} to false.",
                    [legacy.Value]);
        }

        var template = _options.UseLiquidSyntax
            ? ScribanTemplate.ParseLiquid(source)
            : ScribanTemplate.Parse(source);

        if (template.HasErrors)
        {
            var errors = template.Messages
                .Where(message => message.Type == ParserMessageType.Error)
                .Select(message => message.ToString())
                .ToArray();

            throw new TemplateCompilationException(
                $"The template could not be parsed: {string.Join("; ", errors)}",
                errors);
        }

        var collector = new GlobalVariableCollector();
        collector.Visit(template.Page);

        var body = template.Page?.Body?.Statements;
        var isStatic = body is null
            || body.All(statement => statement is ScriptRawStatement or ScriptEscapeStatement);

        return new ScribanCompiledTemplate(source, template, collector.Names, isStatic);
    }

    /// <summary>
    /// Collects the free variables of a page for <see cref="CompiledTemplate.VariableNames"/>: the
    /// global names it reads, minus the ones a <c>for</c> introduces itself.
    /// </summary>
    private sealed class GlobalVariableCollector : ScriptVisitor
    {
        private readonly List<string> _globals = [];
        private readonly HashSet<string> _bound = new(StringComparer.Ordinal);

        public string[] Names => [.. _globals.Where(name => !_bound.Contains(name))];

        public override void Visit(ScriptVariableGlobal node)
        {
            if (!_globals.Contains(node.Name, StringComparer.Ordinal)) _globals.Add(node.Name);
            base.Visit(node);
        }

        // `it` in `{{ it.name }}` is a member name, not a variable of its own.
        public override void Visit(ScriptMemberExpression node) => Visit(node.Target);

        public override void Visit(ScriptForStatement node)
        {
            if (node.Variable is ScriptVariable loopVariable) _bound.Add(loopVariable.Name);
            base.Visit(node);
        }
    }
}
