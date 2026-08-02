using Templar.Rendering;
using Xunit;

namespace Templar.Tests;

public class CompilerTests
{
    private readonly MustacheTemplateCompiler _compiler = new();

    [Fact]
    public void Finds_placeholders_in_order_of_first_appearance()
    {
        var compiled = _compiler.Compile("{{username}} {{USER_EMAIL}} {{DATE}} {{username}}");

        Assert.Equal(["username", "USER_EMAIL", "DATE"], compiled.VariableNames);
    }

    [Fact]
    public void Treats_a_template_without_placeholders_as_static()
    {
        var compiled = _compiler.Compile("Hello there.");

        Assert.True(compiled.IsStatic);
        Assert.Empty(compiled.VariableNames);
    }

    [Fact]
    public void Parses_a_format_specifier()
    {
        var compiled = _compiler.Compile("{{DATE:dd/MM/yyyy}}");

        Assert.Equal(["DATE"], compiled.VariableNames);
    }

    [Theory]
    [InlineData("{{}}")]
    [InlineData("{{ }}")]
    [InlineData("{{unclosed")]
    [InlineData("{{two words}}")]
    [InlineData("{{spans\nlines}}")]
    public void Ignores_text_that_only_looks_like_a_placeholder(string source)
    {
        var compiled = _compiler.Compile(source);

        Assert.True(compiled.IsStatic);
    }

    [Fact]
    public void Trims_whitespace_around_a_placeholder_name()
    {
        var compiled = _compiler.Compile("{{  username  }}");

        Assert.Equal(["username"], compiled.VariableNames);
    }

    [Fact]
    public void Escapes_a_doubled_brace_pair()
    {
        var compiled = _compiler.Compile("{{{{username}}");

        Assert.True(compiled.IsStatic);
    }

    [Fact]
    public void Returns_the_same_instance_for_the_same_source()
    {
        var first = _compiler.Compile("{{a}}");
        var second = _compiler.Compile("{{a}}");

        Assert.Same(first, second);
    }
}
