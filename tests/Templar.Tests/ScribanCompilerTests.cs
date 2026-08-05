using Templar.Scriban;
using Xunit;

namespace Templar.Tests;

public class ScribanCompilerTests
{
    private readonly ScribanTemplateCompiler _compiler = new(new TemplateOptions());

    [Fact]
    public void Finds_free_variables_in_order_of_first_appearance()
    {
        var compiled = _compiler.Compile("{{ username }} {{ email }} {{ username }}");

        Assert.Equal(["username", "email"], compiled.VariableNames);
    }

    [Fact]
    public void Does_not_report_a_loop_variable_or_a_member_name_as_a_free_variable()
    {
        var compiled = _compiler.Compile("{{ for line in order.lines }}{{ line.name }}{{ end }}");

        Assert.Equal(["order"], compiled.VariableNames);
    }

    /// <summary>
    /// <see cref="CompiledTemplate.VariableNames"/> is syntactic — the compiler has not met the
    /// globals yet, so a called name lands there whether it turns out to be a value, one of Templar's
    /// builtins or a caller's <see cref="TemplateOptions.Functions"/> entry. Rendering resolves them;
    /// only a name that resolves to nothing is reported missing.
    /// </summary>
    [Fact]
    public void Reports_a_called_function_among_the_free_variables()
    {
        var compiled = _compiler.Compile("{{ vnd total }} {{ d | format 'D' }}");

        Assert.Equal(["vnd", "total", "d", "format"], compiled.VariableNames);
    }

    [Fact]
    public void Treats_a_template_without_code_as_static()
    {
        var compiled = _compiler.Compile("Hello there.");

        Assert.True(compiled.IsStatic);
        Assert.Empty(compiled.VariableNames);
    }

    [Fact]
    public void An_expression_with_no_variable_is_still_not_static()
    {
        var compiled = _compiler.Compile("{{ 2 + 2 }}");

        Assert.False(compiled.IsStatic);
        Assert.Empty(compiled.VariableNames);
    }

    [Theory]
    [InlineData("{{ if x }}no end")]
    [InlineData("{{ 1 + }}")]
    public void Reports_a_syntax_error(string source)
    {
        var exception = Assert.Throws<TemplateCompilationException>(() => _compiler.Compile(source));

        Assert.NotEmpty(exception.Errors);
    }

    /// <summary>
    /// Scriban renders <c>{{DATE:dd/MM/yyyy}}</c> as an empty string rather than failing, so a table
    /// still written in Templar 1.0's syntax would lose values with no signal at all.
    /// </summary>
    [Fact]
    public void Rejects_the_legacy_format_syntax_with_a_migration_hint()
    {
        var exception = Assert.Throws<TemplateCompilationException>(
            () => _compiler.Compile("Sent on {{DATE:dd/MM/yyyy}}."));

        Assert.Contains("{{ DATE | format 'dd/MM/yyyy' }}", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Allows_the_legacy_format_syntax_when_asked_to()
    {
        var compiler = new ScribanTemplateCompiler(new TemplateOptions { RejectLegacyFormatSyntax = false });

        Assert.False(compiler.Compile("{{DATE:dd/MM/yyyy}}").IsStatic);
    }

    [Theory]
    [InlineData("{{ vip ? 'yes' : 'no' }}")]
    [InlineData("{{ fn arg: 1 }}")]
    [InlineData("<style>a { color: red }</style>")]
    public void Does_not_mistake_valid_scriban_for_the_legacy_format_syntax(string source)
        => _compiler.Compile(source);

    [Fact]
    public void Returns_the_same_instance_for_the_same_source()
    {
        var first = _compiler.Compile("{{ a }}");
        var second = _compiler.Compile("{{ a }}");

        Assert.Same(first, second);
    }

    [Fact]
    public void Parses_liquid_when_configured_to()
    {
        var compiler = new ScribanTemplateCompiler(new TemplateOptions { UseLiquidSyntax = true });

        Assert.False(compiler.Compile("{% if vip %}VIP{% endif %}").IsStatic);
    }
}
