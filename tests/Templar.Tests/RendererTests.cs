using System.Globalization;
using Templar.Rendering;
using Xunit;

namespace Templar.Tests;

public class RendererTests
{
    private static readonly MustacheTemplateCompiler Compiler = new();
    private static readonly TemplateRenderer Renderer = new();

    private static string Render(
        string source,
        TemplateValues values,
        bool htmlEncode = false,
        MissingVariableBehavior missing = MissingVariableBehavior.Throw,
        string culture = "en")
        => Renderer.Render(
            Compiler.Compile(source),
            values,
            new TemplateRenderContext(CultureInfo.GetCultureInfo(culture), htmlEncode, missing, "test"));

    [Fact]
    public void Substitutes_values_regardless_of_case_and_separators()
    {
        var values = TemplateValues.Create()
            .Set("username", "huy")
            .Set("user_email", "huy@example.com");

        var result = Render("{{USERNAME}} <{{userEmail}}>", values);

        Assert.Equal("huy <huy@example.com>", result);
    }

    [Fact]
    public void Formats_values_with_the_template_culture()
    {
        var values = TemplateValues.Create().Set("DATE", new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal("31/07/2026", Render("{{DATE:dd/MM/yyyy}}", values, culture: "vi"));
        Assert.Equal("1.234,5", Render("{{N:#,##0.0##}}", TemplateValues.Create().Set("N", 1234.5m), culture: "de-DE"));
    }

    [Fact]
    public void Html_encodes_values_but_not_the_template_markup()
    {
        var values = TemplateValues.Create().Set("username", "<b>huy</b> & co");

        var result = Render("<p>Hello {{username}}</p>", values, htmlEncode: true);

        Assert.Equal("<p>Hello &lt;b&gt;huy&lt;/b&gt; &amp; co</p>", result);
    }

    [Fact]
    public void Does_not_encode_values_wrapped_in_TemplateRaw()
    {
        var values = TemplateValues.Create().Set("cta", TemplateRaw.Html("<a href=\"/go\">Go</a>"));

        var result = Render("{{cta}}", values, htmlEncode: true);

        Assert.Equal("<a href=\"/go\">Go</a>", result);
    }

    [Fact]
    public void Leaves_non_ascii_letters_alone_when_encoding_html()
    {
        var values = TemplateValues.Create().Set("username", "Huy <Nguyễn>");

        var result = Render("<p>Chào {{username}}</p>", values, htmlEncode: true);

        Assert.Equal("<p>Chào Huy &lt;Nguyễn&gt;</p>", result);
    }

    [Fact]
    public void Does_not_encode_a_text_body()
    {
        var values = TemplateValues.Create().Set("username", "a & b");

        Assert.Equal("a & b", Render("{{username}}", values));
    }

    [Fact]
    public void Throws_and_reports_every_missing_placeholder()
    {
        var exception = Assert.Throws<TemplateRenderException>(
            () => Render("{{a}} {{b}} {{c}}", TemplateValues.Create().Set("b", 1)));

        Assert.Equal(["a", "c"], exception.MissingVariables);
        Assert.Contains("test", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Can_blank_or_keep_missing_placeholders()
    {
        Assert.Equal("[]", Render("[{{a}}]", TemplateValues.Empty, missing: MissingVariableBehavior.Empty));
        Assert.Equal("[{{a}}]", Render("[{{a}}]", TemplateValues.Empty, missing: MissingVariableBehavior.Keep));
    }

    [Fact]
    public void Renders_a_supplied_null_as_an_empty_string()
    {
        var values = TemplateValues.Create().Set("a", null);

        Assert.Equal("[]", Render("[{{a}}]", values));
    }

    [Fact]
    public void Reports_an_invalid_format_string()
    {
        var values = TemplateValues.Create().Set("DATE", new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc));

        var exception = Assert.Throws<TemplateRenderException>(() => Render("{{DATE:Q}}", values));

        Assert.IsType<FormatException>(exception.InnerException);
    }

    [Fact]
    public void Builds_values_from_an_anonymous_object()
    {
        var values = TemplateValues.FromObject(new { username = "huy", EMAIL = "huy@example.com" });

        Assert.Equal("huy huy@example.com", Render("{{USERNAME}} {{email}}", values));
    }
}
