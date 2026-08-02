using Templar.Stores;
using Xunit;

namespace Templar.Tests;

public class TemplateQueryServiceTests
{
    [Fact]
    public async Task Lists_keys_from_the_store()
    {
        var keys = await TemplarHarness.Create().Queries.ListKeysAsync();

        Assert.Equal(["welcome-user"], keys);
    }

    [Fact]
    public async Task Lists_every_template_including_inactive_ones()
    {
        var templates = await TemplarHarness.Create().Queries.ListAsync();

        Assert.Equal(5, templates.Count);
        Assert.Contains(templates, t => !t.IsActive);
        Assert.All(templates, t => Assert.Equal("welcome-user", t.TemplateKey));
    }

    [Fact]
    public async Task Listing_every_template_goes_to_the_store_each_time()
    {
        var store = new TemplarHarness.CountingStore(new InMemoryTemplateStore(TemplarHarness.Seed));
        var harness = TemplarHarness.Create(store);

        await harness.Queries.ListAsync();
        await harness.Queries.ListAsync();

        // The cache holds one entry per key, so there is nothing for a whole-table read to hit.
        Assert.Equal(2, store.Reads);
    }

    [Fact]
    public async Task Returns_every_variant_of_a_key_including_inactive_ones()
    {
        var variants = await TemplarHarness.Create().Queries.GetVariantsAsync("welcome-user");

        Assert.Equal(5, variants.Count);
        Assert.Contains(variants, v => !v.IsActive);
        Assert.Contains(variants, v => v.Channel == TemplateChannel.Other);
    }

    [Fact]
    public async Task Returns_an_empty_set_for_an_unknown_key()
    {
        Assert.Empty(await TemplarHarness.Create().Queries.GetVariantsAsync("nope"));
    }

    [Fact]
    public async Task Find_matches_the_exact_culture_and_channel_only()
    {
        var queries = TemplarHarness.Create().Queries;

        Assert.Equal("Chào mừng tới XXX", (await queries.FindAsync("welcome-user", "vi"))!.Subject);
        Assert.Equal("Chào mừng tới XXX", (await queries.FindAsync("welcome-user", "VI"))!.Subject);
        Assert.Null(await queries.FindAsync("welcome-user", "vi-VN"));               // no fallback
        Assert.Null(await queries.FindAsync("welcome-user", "vi", TemplateChannel.InApp));
    }

    [Fact]
    public async Task Find_returns_inactive_rows_so_an_editor_can_see_them()
    {
        var inactive = await TemplarHarness.Create().Queries.FindAsync("welcome-user", "fr");

        Assert.NotNull(inactive);
        Assert.False(inactive.IsActive);
    }

    [Fact]
    public async Task Resolve_applies_culture_fallback_and_skips_inactive_rows()
    {
        var queries = TemplarHarness.Create(o => o.DefaultCulture = "en").Queries;

        Assert.Equal("vi", (await queries.ResolveAsync("welcome-user", "vi-VN"))!.Culture);
        Assert.Equal("en", (await queries.ResolveAsync("welcome-user", "ja"))!.Culture);
        Assert.Equal("en", (await queries.ResolveAsync("welcome-user"))!.Culture);

        // fr exists but is inactive, so it falls through to the default culture.
        Assert.Equal("en", (await queries.ResolveAsync("welcome-user", "fr"))!.Culture);
    }

    [Fact]
    public async Task Resolve_carries_the_name_and_description_metadata()
    {
        var definition = await TemplarHarness.Create().Queries.ResolveAsync("welcome-user", "vi");

        Assert.Equal("Email chào mừng", definition!.Name);
        Assert.Equal("Gửi sau khi xác nhận email.", definition.Description);
    }

    [Fact]
    public async Task Reads_the_store_once_per_key_while_cached()
    {
        var store = new TemplarHarness.CountingStore(new InMemoryTemplateStore(TemplarHarness.Seed));
        var harness = TemplarHarness.Create(store);

        for (var i = 0; i < 3; i++)
            await harness.Queries.ResolveAsync("welcome-user", "vi");

        Assert.Equal(1, store.Reads);

        await harness.Cache.RemoveAsync("welcome-user");
        await harness.Queries.ResolveAsync("welcome-user", "vi");

        Assert.Equal(2, store.Reads);
    }

    [Fact]
    public async Task Reads_the_store_every_time_when_the_cache_is_off()
    {
        var store = new TemplarHarness.CountingStore(new InMemoryTemplateStore(TemplarHarness.Seed));
        var harness = TemplarHarness.Create(store, o => o.EnableCache = false);

        for (var i = 0; i < 3; i++)
            await harness.Queries.ResolveAsync("welcome-user", "vi");

        Assert.Equal(3, store.Reads);
    }
}
