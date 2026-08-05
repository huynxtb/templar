using System.Globalization;
using Templar.Rendering;
using Templar.Scriban;
using Xunit;

namespace Templar.Tests;

public class ScribanRendererTests
{
    private static readonly TemplateOptions Options = new();
    private static readonly ScribanTemplateCompiler Compiler = new(Options);
    private static readonly ScribanTemplateRenderer Renderer = new(Options);

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

    private static TemplateValues Order() => TemplateValues.Create()
        .Set("customer", new { FirstName = "Huy", IsVip = true })
        .Set("lines", new[]
        {
            new { Name = "Bàn phím", Quantity = 1, Total = 1_250_000m },
            new { Name = "Chuột <không dây>", Quantity = 2, Total = 700_000m },
        });

    // ------------------------------------------------------------------ loops, branches, builtins

    [Fact]
    public void Renders_a_table_from_a_collection()
    {
        const string Source =
            "{{ for line in lines }}<tr><td>{{ line.name }}</td><td>{{ line.quantity }}</td></tr>{{ end }}";

        var result = Render(Source, Order(), htmlEncode: true);

        Assert.Equal(
            "<tr><td>Bàn phím</td><td>1</td></tr><tr><td>Chuột &lt;không dây&gt;</td><td>2</td></tr>",
            result);
    }

    [Fact]
    public void Chooses_a_branch_with_if_else()
    {
        Assert.Equal("VIP", Render("{{ if customer.is_vip }}VIP{{ else }}standard{{ end }}", Order()));

        var standard = TemplateValues.Create().Set("customer", new { IsVip = false });
        Assert.Equal("standard", Render("{{ if customer.is_vip }}VIP{{ else }}standard{{ end }}", standard));
    }

    [Theory]
    [InlineData("paid", "Paid")]
    [InlineData("pending", "Awaiting payment")]
    [InlineData("cancelled", "Status: cancelled")]
    public void Chooses_a_branch_with_case_and_when(string status, string expected)
    {
        const string Source =
            "{{ case order.status }}{{ when 'paid' }}Paid{{ when 'pending' }}Awaiting payment" +
            "{{ else }}Status: {{ order.status }}{{ end }}";

        var values = TemplateValues.Create().Set("order", new { Status = status });

        Assert.Equal(expected, Render(Source, values));
    }

    [Fact]
    public void Exposes_loop_metadata_and_builtin_functions()
    {
        var result = Render("{{ for line in lines }}{{ for.index }}:{{ line.name | string.upcase }} {{ end }}", Order());

        Assert.Equal("0:BÀN PHÍM 1:CHUỘT <KHÔNG DÂY> ", result);
    }

    [Fact]
    public void Stops_a_runaway_loop_at_the_configured_limit()
    {
        var options = new TemplateOptions { LoopLimit = 10 };
        var renderer = new ScribanTemplateRenderer(options);

        var exception = Assert.Throws<TemplateRenderException>(() => renderer.Render(
            new ScribanTemplateCompiler(options).Compile("{{ while true }}x{{ end }}"),
            TemplateValues.Empty,
            new TemplateRenderContext(CultureInfo.InvariantCulture, false, MissingVariableBehavior.Empty)));

        Assert.Contains("10", exception.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ substitution guarantees

    [Fact]
    public void Matches_top_level_names_regardless_of_case_and_separators()
    {
        var values = TemplateValues.Create()
            .Set("username", "huy")
            .Set("user_email", "huy@example.com");

        Assert.Equal("huy <huy@example.com>", Render("{{ USERNAME }} <{{ userEmail }}>", values));
    }

    [Fact]
    public void Matches_member_names_regardless_of_case_and_separators()
    {
        var values = TemplateValues.Create().Set("customer", new { FirstName = "Huy" });

        Assert.Equal(
            "Huy Huy Huy",
            Render("{{ customer.first_name }} {{ customer.FirstName }} {{ customer.firstname }}", values));
    }

    [Fact]
    public void Formats_values_with_the_template_culture()
    {
        var values = TemplateValues.Create()
            .Set("DATE", new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc))
            .Set("N", 1234.5m);

        Assert.Equal("31/07/2026", Render("{{ DATE | format 'dd/MM/yyyy' }}", values, culture: "vi"));
        Assert.Equal("1.234,5", Render("{{ N | format '#,##0.0##' }}", values, culture: "de-DE"));

        // Without a format the decimal separator is the culture's but there is no grouping.
        Assert.Equal("1234,5", Render("{{ N }}", values, culture: "de-DE"));
    }

    [Fact]
    public void Html_encodes_values_but_not_the_template_markup()
    {
        var values = TemplateValues.Create().Set("username", "<b>huy</b> & co");

        Assert.Equal(
            "<p>Hello &lt;b&gt;huy&lt;/b&gt; &amp; co</p>",
            Render("<p>Hello {{ username }}</p>", values, htmlEncode: true));
    }

    [Fact]
    public void Does_not_encode_values_wrapped_in_TemplateRaw()
    {
        var values = TemplateValues.Create().Set("cta", TemplateRaw.Html("<a href=\"/go\">Go</a>"));

        Assert.Equal("<a href=\"/go\">Go</a>", Render("{{ cta }}", values, htmlEncode: true));
    }

    [Fact]
    public void The_raw_function_bypasses_encoding_from_inside_the_template()
    {
        var values = TemplateValues.Create().Set("cta", "<a href=\"/go\">Go</a>");

        Assert.Equal("<a href=\"/go\">Go</a>", Render("{{ cta | raw }}", values, htmlEncode: true));
    }

    [Fact]
    public void Leaves_non_ascii_letters_alone_when_encoding_html()
    {
        var values = TemplateValues.Create().Set("username", "Huy <Nguyễn>");

        Assert.Equal("<p>Chào Huy &lt;Nguyễn&gt;</p>", Render("<p>Chào {{ username }}</p>", values, htmlEncode: true));
    }

    [Fact]
    public void Does_not_encode_a_text_body()
    {
        var values = TemplateValues.Create().Set("username", "a & b");

        Assert.Equal("a & b", Render("{{ username }}", values));
    }

    [Fact]
    public void Throws_and_reports_every_missing_placeholder()
    {
        var exception = Assert.Throws<TemplateRenderException>(
            () => Render("{{ a }} {{ b }} {{ c }}", TemplateValues.Create().Set("b", 1)));

        Assert.Equal(["a", "c"], exception.MissingVariables);
        Assert.Contains("test", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The name is missing, but the template reads through it. Without relaxed target access Scriban
    /// raises "cannot get the member of a null object" first and Templar's own report — which names
    /// every missing value at once — never gets a chance to run.
    /// </summary>
    [Fact]
    public void Reports_a_missing_name_that_is_read_through_rather_than_written()
    {
        const string Source = "{{ order.reference }}{{ for line in order.lines }}{{ line.name }}{{ end }}";

        var exception = Assert.Throws<TemplateRenderException>(() => Render(Source, TemplateValues.Empty));

        Assert.Equal(["order"], exception.MissingVariables.Distinct());
        Assert.Equal("", Render(Source, TemplateValues.Empty, missing: MissingVariableBehavior.Empty));
    }

    [Fact]
    public void Can_blank_or_keep_missing_placeholders()
    {
        Assert.Equal("[]", Render("[{{ a }}]", TemplateValues.Empty, missing: MissingVariableBehavior.Empty));
        Assert.Equal("[{{a}}]", Render("[{{ a }}]", TemplateValues.Empty, missing: MissingVariableBehavior.Keep));
    }

    [Fact]
    public void Renders_a_supplied_null_as_an_empty_string()
    {
        var values = TemplateValues.Create().Set("a", null);

        Assert.Equal("[]", Render("[{{ a }}]", values));
    }

    /// <summary>
    /// A missing *member* is governed by <see cref="TemplateOptions.RelaxedMemberAccess"/>, not by
    /// <see cref="MissingVariableBehavior"/> — otherwise <c>{{ if x.optional }}</c> could not be written.
    /// </summary>
    [Fact]
    public void A_missing_member_is_empty_rather_than_a_missing_placeholder()
    {
        var values = TemplateValues.Create().Set("customer", new { FirstName = "Huy" });

        Assert.Equal("[]", Render("[{{ customer.middle_name }}]", values));
    }

    [Fact]
    public void Reports_an_invalid_format_string()
    {
        var values = TemplateValues.Create().Set("DATE", new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc));

        var exception = Assert.Throws<TemplateRenderException>(() => Render("{{ DATE | format 'Q' }}", values));

        Assert.Contains("Q", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Builds_values_from_an_anonymous_object()
    {
        var values = TemplateValues.FromObject(new { username = "huy", EMAIL = "huy@example.com" });

        Assert.Equal("huy huy@example.com", Render("{{ USERNAME }} {{ email }}", values));
    }

    // ------------------------------------------------------------------ engines do not mix

    /// <summary>
    /// A renderer only understands its own compiler's output, so a custom engine has to register both
    /// halves. Rendering the other engine's <see cref="CompiledTemplate"/> has to say so rather than
    /// fall through to something that silently produces the wrong text.
    /// </summary>
    [Fact]
    public void Refuses_a_template_compiled_by_another_engine()
    {
        var foreign = new ForeignCompiledTemplate("{{ a }}");
        var context = new TemplateRenderContext(CultureInfo.InvariantCulture, false, MissingVariableBehavior.Empty);

        var exception = Assert.Throws<TemplateRenderException>(
            () => Renderer.Render(foreign, TemplateValues.Empty, context));

        Assert.Contains(nameof(ForeignCompiledTemplate), exception.Message, StringComparison.Ordinal);
    }

    private sealed class ForeignCompiledTemplate(string source) : CompiledTemplate(source, ["a"]);

    // ------------------------------------------------------------------ options.Functions

    private static string RenderWith(TemplateOptions options, string source, TemplateValues values)
        => new ScribanTemplateRenderer(options).Render(
            new ScribanTemplateCompiler(options).Compile(source),
            values,
            new TemplateRenderContext(CultureInfo.GetCultureInfo("vi"), false, MissingVariableBehavior.Throw, "test"));

    [Fact]
    public void Calls_a_registered_function_by_name_and_through_a_pipe()
    {
        var options = new TemplateOptions();
        options.Functions["vnd"] = (decimal amount) => $"{amount:N0} đ";

        Assert.Equal(
            "1.250.000 đ / 1.250.000 đ",
            RenderWith(options, "{{ vnd total }} / {{ total | vnd }}", TemplateValues.Create().Set("total", 1_250_000m)));
    }

    /// <summary>
    /// Everything else in Templar matches names through <see cref="TemplateVariableNameComparer"/>,
    /// and a function registered in C# habits (<c>orderTotal</c>) called from a template in Scriban
    /// habits (<c>order_total</c>) is exactly the case that comparer exists for.
    /// </summary>
    [Theory]
    [InlineData("{{ shortDate d }}")]
    [InlineData("{{ short_date d }}")]
    [InlineData("{{ SHORTDATE d }}")]
    public void Matches_a_function_name_the_way_it_matches_every_other_name(string source)
    {
        var options = new TemplateOptions();
        options.Functions["shortDate"] = (DateTimeOffset value) => value.ToString("yyyy-MM-dd");

        var values = TemplateValues.Create().Set("d", new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal("2026-08-04", RenderWith(options, source, values));
    }

    /// <summary>
    /// Templar formats in the template's culture, not the request's, and a caller's function is not
    /// an exception: <c>$"{amount:N0}"</c> inside a delegate reads the ambient culture, so the
    /// renderer sets it for the duration of the render. Without that, the promise would quietly stop
    /// holding at the boundary of the one place a caller is most likely to format a number.
    /// </summary>
    [Theory]
    [InlineData("vi", "1.250.000")]
    [InlineData("en", "1,250,000")]
    public void Runs_a_function_in_the_templates_culture_not_the_ambient_one(string culture, string expected)
    {
        var options = new TemplateOptions();
        options.Functions["money"] = (decimal amount) => $"{amount:N0}";

        var rendered = new ScribanTemplateRenderer(options).Render(
            new ScribanTemplateCompiler(options).Compile("{{ money total }}"),
            TemplateValues.Create().Set("total", 1_250_000m),
            new TemplateRenderContext(
                CultureInfo.GetCultureInfo(culture), false, MissingVariableBehavior.Throw, "test"));

        Assert.Equal(expected, rendered);
    }

    [Fact]
    public void Lets_a_function_take_several_arguments_and_return_a_number()
    {
        var options = new TemplateOptions();
        options.Functions["add"] = (int a, int b) => a + b;

        Assert.Equal("7", RenderWith(options, "{{ add 3 4 }}", TemplateValues.Empty));
    }

    /// <summary>
    /// The functions object is built once and shared, so it sits *below* the per-render values: a
    /// value must still win, or one template's data could be shadowed by a global registration.
    /// </summary>
    [Fact]
    public void Lets_a_value_shadow_a_function_of_the_same_name()
    {
        var options = new TemplateOptions();
        options.Functions["total"] = () => "from the function";

        Assert.Equal(
            "from the value",
            RenderWith(options, "{{ total }}", TemplateValues.Create().Set("total", "from the value")));
    }

    [Fact]
    public void Lets_a_registered_function_replace_a_builtin()
    {
        var options = new TemplateOptions();
        options.Functions["format"] = (object? value, string _) => $"[{value}]";

        Assert.Equal("[5]", RenderWith(options, "{{ n | format 'N0' }}", TemplateValues.Create().Set("n", 5)));
    }

    [Fact]
    public void Reports_a_function_that_was_never_registered_like_any_other_missing_name()
    {
        var exception = Assert.Throws<TemplateRenderException>(
            () => RenderWith(new TemplateOptions(), "{{ slugify title }}", TemplateValues.Create().Set("title", "x")));

        Assert.Contains("slugify", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_html_body_encodes_what_a_function_returns()
    {
        var options = new TemplateOptions();
        options.Functions["shout"] = (string value) => $"<b>{value}</b>";

        var rendered = new ScribanTemplateRenderer(options).Render(
            new ScribanTemplateCompiler(options).Compile("{{ shout name }}"),
            TemplateValues.Create().Set("name", "Huy"),
            new TemplateRenderContext(CultureInfo.InvariantCulture, true, MissingVariableBehavior.Throw, "test"));

        Assert.Equal("&lt;b&gt;Huy&lt;/b&gt;", rendered);
    }

    /// <summary>A function is free to opt out of that encoding the same way a value does.</summary>
    [Fact]
    public void A_function_can_return_TemplateRaw_to_skip_encoding()
    {
        var options = new TemplateOptions();
        options.Functions["bold"] = (string value) => TemplateRaw.Html($"<b>{value}</b>");

        var rendered = new ScribanTemplateRenderer(options).Render(
            new ScribanTemplateCompiler(options).Compile("{{ bold name }}"),
            TemplateValues.Create().Set("name", "Huy"),
            new TemplateRenderContext(CultureInfo.InvariantCulture, true, MissingVariableBehavior.Throw, "test"));

        Assert.Equal("<b>Huy</b>", rendered);
    }

}
