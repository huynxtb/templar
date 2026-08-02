using System.Buffers;
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Templar.Rendering;

/// <summary>
/// Compiles <c>{{placeholder}}</c> templates. Supported syntax:
/// <list type="bullet">
///   <item><description><c>{{name}}</c> — substitute the value called <c>name</c>.</description></item>
///   <item><description><c>{{name:format}}</c> — substitute using a .NET format string, e.g. <c>{{DATE:dd/MM/yyyy}}</c>.</description></item>
///   <item><description><c>{{{{</c> — a literal <c>{{</c>.</description></item>
/// </list>
/// Anything that looks like a placeholder but is not closed, is empty, or spans a line break is
/// treated as ordinary text, so CSS blocks and JSON snippets inside an HTML body survive intact.
/// </summary>
public sealed class MustacheTemplateCompiler(IOptions<TemplateOptions>? options = null) : ITemplateCompiler
{
    private const string OpenToken = "{{";
    private const string CloseToken = "}}";

    /// <summary>Characters that disqualify a candidate token from being a placeholder.</summary>
    private static readonly SearchValues<char> InvalidTokenChars = SearchValues.Create("\r\n{}");

    private readonly ConcurrentDictionary<string, CompiledTemplate> _cache = new(StringComparer.Ordinal);
    private readonly int _cacheSize = Math.Max(1, options?.Value.CompiledTemplateCacheSize ?? 1024);

    public CompiledTemplate Compile(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (_cache.TryGetValue(source, out var cached)) return cached;

        var compiled = Parse(source);

        // Coarse bound: the working set is the number of distinct template bodies, so simply
        // dropping everything at the limit is cheaper than tracking per-entry recency.
        if (_cache.Count >= _cacheSize) _cache.Clear();
        _cache[source] = compiled;

        return compiled;
    }

    private static CompiledTemplate Parse(string source)
    {
        var segments = new List<TemplateSegment>();
        var names = new List<string>();
        var seen = new HashSet<string>(TemplateVariableNameComparer.Instance);

        var literalStart = 0;
        var index = 0;

        void FlushLiteral(int end)
        {
            if (end > literalStart)
                segments.Add(new TemplateSegment { Text = source[literalStart..end] });
        }

        while (index < source.Length)
        {
            var open = source.IndexOf(OpenToken, index, StringComparison.Ordinal);
            if (open < 0) break;

            // "{{{{" escapes a literal "{{".
            if (open + 3 < source.Length && source[open + 2] == '{' && source[open + 3] == '{')
            {
                FlushLiteral(open);
                segments.Add(new TemplateSegment { Text = OpenToken });
                index = open + 4;
                literalStart = index;
                continue;
            }

            var close = source.IndexOf(CloseToken, open + OpenToken.Length, StringComparison.Ordinal);
            if (close < 0) break;

            var inner = source[(open + OpenToken.Length)..close];
            if (!TryParseToken(inner, out var name, out var format))
            {
                index = open + OpenToken.Length;
                continue;
            }

            FlushLiteral(open);
            var token = source[open..(close + CloseToken.Length)];
            segments.Add(new TemplateSegment { Text = token, Name = name, Format = format });

            if (seen.Add(name)) names.Add(name);

            index = close + CloseToken.Length;
            literalStart = index;
        }

        FlushLiteral(source.Length);

        return new CompiledTemplate(source, [.. segments], [.. names]);
    }

    private static bool TryParseToken(string inner, out string name, out string? format)
    {
        name = string.Empty;
        format = null;

        if (inner.Length == 0) return false;
        if (inner.AsSpan().ContainsAny(InvalidTokenChars)) return false;

        var separator = inner.IndexOf(':');
        if (separator >= 0)
        {
            name = inner[..separator].Trim();
            var rawFormat = inner[(separator + 1)..].Trim();
            format = rawFormat.Length == 0 ? null : rawFormat;
        }
        else
        {
            name = inner.Trim();
        }

        if (name.Length == 0) return false;

        // A placeholder name is a single word; "font-size: 12px" style content is not one.
        foreach (var c in name)
        {
            if (char.IsWhiteSpace(c)) return false;
        }

        return true;
    }
}
