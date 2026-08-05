using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace Templar.Scriban;

/// <summary>
/// Exposes <see cref="TemplateOptions.Functions"/> to Scriban. Built once and shared by every render,
/// so it is never written to: assignment in a template lands on the values object pushed above it.
/// </summary>
/// <remarks>
/// The lookup fallback is what <see cref="TemplateValuesScriptObject"/> does for values, for the same
/// reason — <see cref="ScriptObject"/> is ordinal, and a name registered as <c>slugify</c> still has
/// to answer to <c>{{ Slugify … }}</c>.
/// </remarks>
internal sealed class TemplateFunctionsScriptObject : ScriptObject
{
    private string[] _names = [];

    public static TemplateFunctionsScriptObject Create(IDictionary<string, Delegate> functions)
    {
        var script = new TemplateFunctionsScriptObject();

        foreach (var (name, function) in functions) script.Import(name, function);
        script._names = [.. functions.Keys];

        return script;
    }

    public override bool TryGetValue(TemplateContext context, SourceSpan span, string member, out object? value)
    {
        if (base.TryGetValue(context, span, member, out value)) return true;

        var match = Match(member);
        if (match is not null) return base.TryGetValue(context, span, match, out value);

        value = null;
        return false;
    }

    public override bool Contains(string member) => base.Contains(member) || Match(member) is not null;

    private string? Match(string member)
    {
        foreach (var name in _names)
        {
            if (TemplateVariableNameComparer.Instance.Equals(name, member)) return name;
        }

        return null;
    }
}
