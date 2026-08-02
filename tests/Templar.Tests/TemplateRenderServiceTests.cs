using Xunit;

namespace Templar.Tests;

public class TemplateRenderServiceTests
{
    [Fact]
    public async Task Renders_the_requested_language()
    {
        var render = TemplarHarness.Create().Render;

        var vietnamese = await render.RenderAsync(new TemplateRenderRequest("welcome-user", "vi", TemplarHarness.Values()));
        var english = await render.RenderAsync(new TemplateRenderRequest("welcome-user", "en", TemplarHarness.Values()));

        Assert.Equal("Chào mừng tới XXX", vietnamese.Subject);
        Assert.Equal("Xin chào huy, chào mừng tới XXX, đây là email của bạn huy@example.com", vietnamese.Text);
        Assert.Equal("<p>Hello huy, welcome to XXX, this is your email huy@example.com</p>", english.Html);
    }

    [Fact]
    public async Task Falls_back_from_a_specific_culture_to_its_parent()
    {
        var result = await TemplarHarness.Create().Render
            .RenderAsync(new TemplateRenderRequest("welcome-user", "vi-VN", TemplarHarness.Values()));

        Assert.Equal("vi", result.Culture);
    }

    [Fact]
    public async Task Falls_back_to_the_default_culture_for_an_unknown_language()
    {
        var result = await TemplarHarness.Create(o => o.DefaultCulture = "en").Render
            .RenderAsync(new TemplateRenderRequest("welcome-user", "ja", TemplarHarness.Values()));

        Assert.Equal("en", result.Culture);
    }

    [Fact]
    public async Task Does_not_fall_back_when_fallback_is_disabled()
    {
        var render = TemplarHarness.Create(o => o.EnableCultureFallback = false).Render;

        await Assert.ThrowsAsync<TemplateNotFoundException>(
            () => render.RenderAsync(new TemplateRenderRequest("welcome-user", "ja", TemplarHarness.Values())));
    }

    [Fact]
    public async Task Uses_the_default_culture_when_none_is_requested()
    {
        var result = await TemplarHarness.Create(o => o.DefaultCulture = "vi").Render
            .RenderAsync(new TemplateRenderRequest("welcome-user", values: TemplarHarness.Values()));

        Assert.Equal("vi", result.Culture);
    }

    [Fact]
    public async Task Keeps_channels_apart()
    {
        var inApp = await TemplarHarness.Create().Render.RenderAsync(new TemplateRenderRequest
        {
            TemplateKey = "welcome-user",
            Culture = "en",
            Channel = TemplateChannel.InApp,
            Values = TemplarHarness.Values(),
        });

        Assert.Equal("Welcome!", inApp.Subject);
        Assert.Equal("Hi huy 👋", inApp.Text);
        Assert.False(inApp.HasHtml);
    }

    [Fact]
    public async Task Serves_the_Other_channel_alongside_email_and_in_app()
    {
        var result = await TemplarHarness.Create().Render.RenderAsync(new TemplateRenderRequest
        {
            TemplateKey = "welcome-user",
            Culture = "vi",
            Channel = TemplateChannel.Other,
            Values = TemplarHarness.Values(),
        });

        Assert.Equal("XXX: chao huy", result.Text);
        Assert.Null(result.Subject);
        Assert.False(result.HasHtml);
    }

    [Fact]
    public async Task Does_not_render_the_name_or_description_metadata()
    {
        var rendered = await TemplarHarness.Create().Render
            .RenderAsync(new TemplateRenderRequest("welcome-user", "vi", TemplarHarness.Values()));

        Assert.DoesNotContain("Gửi sau khi", rendered.Text!, StringComparison.Ordinal);
        Assert.DoesNotContain("Email chào mừng", rendered.Subject!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ignores_inactive_templates()
    {
        var render = TemplarHarness.Create(o => o.EnableCultureFallback = false).Render;

        var result = await render.TryRenderAsync(new TemplateRenderRequest("welcome-user", "fr", TemplarHarness.Values()));

        Assert.Null(result);
    }

    [Fact]
    public async Task Renders_only_the_requested_parts()
    {
        var result = await TemplarHarness.Create().Render.RenderAsync(new TemplateRenderRequest
        {
            TemplateKey = "welcome-user",
            Culture = "en",
            Values = TemplarHarness.Values(),
            Parts = TemplateParts.Subject,
        });

        Assert.Equal("Welcome to XXX", result.Subject);
        Assert.Null(result.Text);
        Assert.Null(result.Html);
    }

    [Fact]
    public async Task Throws_for_an_unknown_key()
    {
        var render = TemplarHarness.Create().Render;

        var exception = await Assert.ThrowsAsync<TemplateNotFoundException>(
            () => render.RenderAsync(new TemplateRenderRequest("nope", "en")));

        Assert.Equal("nope", exception.TemplateKey);
    }

    [Fact]
    public async Task A_per_request_missing_variable_behavior_overrides_the_global_one()
    {
        var result = await TemplarHarness.Create().Render.RenderAsync(new TemplateRenderRequest
        {
            TemplateKey = "welcome-user",
            Culture = "en",
            Values = TemplateValues.Create().Set("username", "huy"),
            Parts = TemplateParts.Text,
            MissingVariableBehavior = MissingVariableBehavior.Empty,
        });

        Assert.Equal("Hello huy, welcome to XXX, this is your email ", result.Text);
    }
}
