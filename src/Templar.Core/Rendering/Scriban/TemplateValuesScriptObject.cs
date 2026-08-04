using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace Templar.Scriban;

/// <summary>
/// Exposes a <see cref="TemplateValues"/> to Scriban as the top global, so name matching stays the
/// library's own — <see cref="TemplateVariableNameComparer"/> by default, or whatever comparer the
/// caller built the value set with. Scriban's own <see cref="ScriptObject"/> is ordinal.
/// </summary>
internal sealed class TemplateValuesScriptObject(TemplateValues values) : ScriptObject
{
    private readonly TemplateValues _values = values;

    public override bool TryGetValue(TemplateContext context, SourceSpan span, string member, out object? value)
    {
        if (base.TryGetValue(context, span, member, out value)) return true;

        if (_values.TryGetValue(member, out var supplied))
        {
            value = supplied;
            return true;
        }

        value = null;
        return false;
    }

    public override bool Contains(string member)
        => base.Contains(member) || _values.Contains(member);
}
