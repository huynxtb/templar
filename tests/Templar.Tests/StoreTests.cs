using Microsoft.Extensions.DependencyInjection;
using Templar.Abstractions;
using Templar.Caching;
using Templar.Rendering;
using Templar.Stores;
using Xunit;

namespace Templar.Tests;

public class InMemoryStoreTests
{
    private static TemplateDefinition Template(
        string culture,
        string subject,
        TemplateChannel channel = TemplateChannel.Email) => new()
    {
        TemplateKey = "welcome-user",
        Culture = culture,
        Channel = channel,
        Subject = subject,
        TextBody = "body",
        UpdatedAtUtc = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public async Task Returns_every_variant_of_a_key()
    {
        var store = new InMemoryTemplateStore([Template("en", "en"), Template("vi", "vi"), Template("en", "app", TemplateChannel.InApp)]);

        var set = await store.GetTemplateSetAsync("welcome-user");

        Assert.Equal(3, set.Count);
    }

    [Fact]
    public async Task Returns_an_empty_set_for_an_unknown_key()
    {
        var store = new InMemoryTemplateStore();

        Assert.Empty(await store.GetTemplateSetAsync("nope"));
    }

    [Fact]
    public async Task Upsert_replaces_the_same_key_culture_and_channel()
    {
        var store = new InMemoryTemplateStore([Template("en", "first")]);

        await store.UpsertAsync(Template("en", "second"));
        var set = await store.GetTemplateSetAsync("welcome-user");

        Assert.Equal("second", Assert.Single(set).Subject);
    }

    [Fact]
    public async Task Upsert_of_a_different_channel_adds_a_row()
    {
        var store = new InMemoryTemplateStore([Template("en", "email")]);

        await store.UpsertAsync(Template("en", "in-app", TemplateChannel.InApp));

        Assert.Equal(2, (await store.GetTemplateSetAsync("welcome-user")).Count);
    }

    [Fact]
    public async Task Delete_removes_one_variant_and_reports_whether_it_matched()
    {
        var store = new InMemoryTemplateStore([Template("en", "en"), Template("vi", "vi")]);

        Assert.True(await store.DeleteAsync("welcome-user", "EN", TemplateChannel.Email));
        Assert.False(await store.DeleteAsync("welcome-user", "en", TemplateChannel.Email));
        Assert.Equal("vi", Assert.Single(await store.GetTemplateSetAsync("welcome-user")).Culture);
    }

    [Fact]
    public async Task Lists_keys_in_order()
    {
        var store = new InMemoryTemplateStore(
        [
            Template("en", "a") with { TemplateKey = "reset-password" },
            Template("en", "b") with { TemplateKey = "invoice-paid" },
        ]);

        Assert.Equal(["invoice-paid", "reset-password"], await store.ListTemplateKeysAsync());
    }

    [Fact]
    public async Task Lists_every_template_ordered_by_key_culture_and_channel()
    {
        var store = new InMemoryTemplateStore(
        [
            Template("vi", "vi") with { TemplateKey = "reset-password" },
            Template("en", "app", TemplateChannel.InApp),
            Template("en", "en"),
        ]);

        var all = await store.GetAllTemplatesAsync();

        Assert.Equal(
            [
                ("reset-password", "vi", TemplateChannel.Email),
                ("welcome-user", "en", TemplateChannel.Email),
                ("welcome-user", "en", TemplateChannel.InApp),
            ],
            all.Select(t => (t.TemplateKey, t.Culture, t.Channel)));
    }
}

public class DependencyInjectionTests
{
    private static ServiceProvider Build(Action<TemplateOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddTemplar(configure ?? (o => o.DefaultCulture = "vi"))
            .UseInMemoryStore(
            [
                new TemplateDefinition
                {
                    TemplateKey = "welcome-user",
                    Culture = "vi",
                    Subject = "Chào mừng tới XXX",
                    TextBody = "Xin chào {{username}}",
                    UpdatedAtUtc = DateTimeOffset.UnixEpoch,
                },
            ]);

        // Scope validation on, so a mistake in the registered lifetimes fails the test.
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }

    [Fact]
    public async Task Wires_up_the_three_services()
    {
        await using var provider = Build();
        using var scope = provider.CreateScope();

        var rendered = await scope.ServiceProvider.GetRequiredService<ITemplateRenderService>().RenderAsync(
            new TemplateRenderRequest("welcome-user", values: TemplateValues.FromObject(new { username = "huy" })));

        Assert.Equal("Xin chào huy", rendered.Text);

        var queries = scope.ServiceProvider.GetRequiredService<ITemplateQueryService>();
        Assert.Equal(["welcome-user"], await queries.ListKeysAsync());
        Assert.NotNull(await queries.FindAsync("welcome-user", "vi"));

        var commands = scope.ServiceProvider.GetRequiredService<ITemplateCommandService>();
        Assert.True(await commands.DeleteAsync("welcome-user", "vi"));
        Assert.Null(await queries.ResolveAsync("welcome-user", "vi"));
    }

    [Fact]
    public void Registers_the_database_services_as_scoped()
    {
        using var provider = Build();

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.Same(
            first.ServiceProvider.GetRequiredService<ITemplateQueryService>(),
            first.ServiceProvider.GetRequiredService<ITemplateQueryService>());
        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<ITemplateQueryService>(),
            second.ServiceProvider.GetRequiredService<ITemplateQueryService>());
        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<ITemplateCommandService>(),
            second.ServiceProvider.GetRequiredService<ITemplateCommandService>());
    }

    [Fact]
    public void Registers_the_channel_list_as_a_singleton_that_needs_no_store()
    {
        using var provider = Build();

        // No database is involved, so it resolves from the root provider too.
        var channels = provider.GetRequiredService<ITemplateChannelService>();

        Assert.NotEmpty(channels.GetAll());
        Assert.Same(channels, provider.CreateScope().ServiceProvider.GetRequiredService<ITemplateChannelService>());
    }

    [Fact]
    public void Shares_the_cache_and_the_engine_across_scopes()
    {
        using var provider = Build();

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        // A per-scope cache would not be a cache at all.
        Assert.Same(
            first.ServiceProvider.GetRequiredService<ITemplateCache>(),
            second.ServiceProvider.GetRequiredService<ITemplateCache>());
        Assert.Same(
            first.ServiceProvider.GetRequiredService<ITemplateCompiler>(),
            second.ServiceProvider.GetRequiredService<ITemplateCompiler>());
    }

    [Fact]
    public void Registers_the_store_under_the_read_and_write_interfaces()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        Assert.Same(
            scope.ServiceProvider.GetRequiredService<ITemplateStore>(),
            scope.ServiceProvider.GetRequiredService<ITemplateWriteStore>());
    }

    [Fact]
    public async Task UseDistributedCache_replaces_the_in_process_cache()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.AddTemplar().UseInMemoryStore(TemplarHarness.Seed).UseDistributedCache();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.IsType<DistributedTemplateCache>(provider.GetRequiredService<ITemplateCache>());

        var rendered = await scope.ServiceProvider.GetRequiredService<ITemplateRenderService>()
            .RenderAsync(new TemplateRenderRequest("welcome-user", "vi", TemplarHarness.Values()));

        Assert.Equal("Chào mừng tới XXX", rendered.Subject);
    }
}
