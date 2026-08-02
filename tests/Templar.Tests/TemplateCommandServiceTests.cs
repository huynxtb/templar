using Templar.Stores;
using Xunit;

namespace Templar.Tests;

public class TemplateCommandServiceTests
{
    private static TemplateDefinition Template(string culture, string subject) => new()
    {
        TemplateKey = "welcome-user",
        Culture = culture,
        Channel = TemplateChannel.Email,
        Subject = subject,
        TextBody = "Hello {{username}}",
        UpdatedAtUtc = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public async Task Save_creates_a_template()
    {
        var harness = TemplarHarness.Create(new InMemoryTemplateStore());

        await harness.Commands!.SaveAsync(Template("en", "Welcome"));

        Assert.Equal("Welcome", (await harness.Queries.FindAsync("welcome-user", "en"))!.Subject);
    }

    [Fact]
    public async Task Save_replaces_the_same_key_culture_and_channel()
    {
        var harness = TemplarHarness.Create(new InMemoryTemplateStore([Template("en", "First")]));

        await harness.Commands!.SaveAsync(Template("en", "Second"));

        var variants = await harness.Queries.GetVariantsAsync("welcome-user");
        Assert.Equal("Second", Assert.Single(variants).Subject);
    }

    [Fact]
    public async Task Save_accepts_several_templates_at_once()
    {
        var harness = TemplarHarness.Create(new InMemoryTemplateStore());

        await harness.Commands!.SaveAsync([Template("en", "Welcome"), Template("vi", "Chào mừng")]);

        Assert.Equal(2, (await harness.Queries.GetVariantsAsync("welcome-user")).Count);
    }

    [Fact]
    public async Task Delete_removes_one_variant_and_reports_whether_it_matched()
    {
        var harness = TemplarHarness.Create(
            new InMemoryTemplateStore([Template("en", "Welcome"), Template("vi", "Chào mừng")]));

        Assert.True(await harness.Commands!.DeleteAsync("welcome-user", "en"));
        Assert.False(await harness.Commands.DeleteAsync("welcome-user", "en"));

        var remaining = await harness.Queries.GetVariantsAsync("welcome-user");
        Assert.Equal("vi", Assert.Single(remaining).Culture);
    }

    // The point of routing writes through the command service: the read cache is dropped for you.
    [Fact]
    public async Task Save_makes_the_change_visible_to_a_cached_reader()
    {
        var store = new InMemoryTemplateStore([Template("en", "First")]);
        var harness = TemplarHarness.Create(store);

        Assert.Equal("First", (await harness.Queries.ResolveAsync("welcome-user", "en"))!.Subject);

        await harness.Commands!.SaveAsync(Template("en", "Second"));

        Assert.Equal("Second", (await harness.Queries.ResolveAsync("welcome-user", "en"))!.Subject);
    }

    [Fact]
    public async Task Delete_makes_the_removal_visible_to_a_cached_reader()
    {
        var harness = TemplarHarness.Create(new InMemoryTemplateStore([Template("en", "First")]));

        Assert.NotNull(await harness.Queries.ResolveAsync("welcome-user", "en"));

        await harness.Commands!.DeleteAsync("welcome-user", "en");

        Assert.Null(await harness.Queries.ResolveAsync("welcome-user", "en"));
    }

    [Fact]
    public async Task Invalidate_without_a_key_clears_everything()
    {
        var store = new TemplarHarness.CountingStore(new InMemoryTemplateStore(TemplarHarness.Seed));
        var harness = TemplarHarness.Create(store);

        await harness.Queries.ResolveAsync("welcome-user", "vi");
        await harness.Queries.ResolveAsync("welcome-user", "vi");
        Assert.Equal(1, store.Reads);

        await harness.Commands!.InvalidateAsync();
        await harness.Queries.ResolveAsync("welcome-user", "vi");

        Assert.Equal(2, store.Reads);
    }
}
