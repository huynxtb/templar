using System.Text.Encodings.Web;
using Scriban;
using Scriban.Parsing;

namespace Templar.Scriban;

/// <summary>
/// The <see cref="TemplateContext"/> Templar renders with. Its one job is HTML encoding: Scriban
/// routes only <c>{{ … }}</c> output through <see cref="Write"/>, never the literal text around it,
/// so encoding here escapes substituted values and leaves the template's own markup alone.
/// </summary>
internal sealed class TemplarScriptContext(HtmlEncoder? htmlEncoder) : TemplateContext
{
    public override TemplateContext Write(SourceSpan span, object? textAsObject)
    {
        if (htmlEncoder is null || textAsObject is null) return base.Write(span, textAsObject);

        // TemplateRaw is the caller saying the markup is theirs and already safe.
        if (textAsObject is TemplateRaw raw) return base.Write(span, raw.Value);

        return base.Write(span, htmlEncoder.Encode(ObjectToString(textAsObject, nested: false) ?? string.Empty));
    }
}
