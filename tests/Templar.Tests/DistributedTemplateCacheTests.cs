using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Templar.Caching;
using Templar.Stores;
using Xunit;

namespace Templar.Tests;

public class DistributedTemplateCacheTests
{
    private static (TemplarHarness Harness, TemplarHarness.CountingStore Store, SpyDistributedCache Cache) Create(
        Action<TemplateOptions>? configure = null)
    {
        var options = new TemplateOptions();
        configure?.Invoke(options);

        var spy = new SpyDistributedCache();
        var cache = new DistributedTemplateCache(spy, Options.Create(options));
        var store = new TemplarHarness.CountingStore(new InMemoryTemplateStore(TemplarHarness.Seed));

        return (TemplarHarness.Create(store, configure, cache), store, spy);
    }

    [Fact]
    public async Task Serves_a_second_read_from_the_distributed_cache()
    {
        var (harness, store, _) = Create();

        for (var i = 0; i < 3; i++)
            Assert.Equal("vi", (await harness.Queries.ResolveAsync("welcome-user", "vi-VN"))!.Culture);

        Assert.Equal(1, store.Reads);
    }

    [Fact]
    public async Task Round_trips_every_field_through_json()
    {
        var (harness, _, _) = Create();

        // First read populates the cache; the second comes back deserialized.
        await harness.Queries.GetVariantsAsync("welcome-user");
        var cached = await harness.Queries.GetVariantsAsync("welcome-user");

        var vietnamese = cached.Single(t => t.Culture == "vi" && t.Channel == TemplateChannel.Email);
        Assert.Equal("Chào mừng tới XXX", vietnamese.Subject);
        Assert.Equal("Email chào mừng", vietnamese.Name);
        Assert.Equal("Gửi sau khi xác nhận email.", vietnamese.Description);
        Assert.Equal(DateTimeOffset.UnixEpoch, vietnamese.UpdatedAtUtc);
        Assert.Contains(cached, t => t.Channel == TemplateChannel.Other);   // enum by name
        Assert.Contains(cached, t => !t.IsActive);
    }

    [Fact]
    public async Task Prefixes_keys_so_applications_can_share_one_instance()
    {
        var (harness, _, spy) = Create(o => o.CacheKeyPrefix = "myapp:templates:");

        await harness.Queries.GetVariantsAsync("welcome-user");

        Assert.Contains(spy.Keys, key => key.StartsWith("myapp:templates:", StringComparison.Ordinal)
                                         && key.EndsWith(":welcome-user", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Remove_evicts_one_key()
    {
        var (harness, store, _) = Create();

        await harness.Queries.GetVariantsAsync("welcome-user");
        await harness.Cache.RemoveAsync("welcome-user");
        await harness.Queries.GetVariantsAsync("welcome-user");

        Assert.Equal(2, store.Reads);
    }

    [Fact]
    public async Task Clear_makes_every_cached_key_unreachable()
    {
        var (harness, store, _) = Create();

        await harness.Queries.GetVariantsAsync("welcome-user");
        await harness.Commands!.InvalidateAsync();
        await harness.Queries.GetVariantsAsync("welcome-user");

        Assert.Equal(2, store.Reads);
    }

    [Fact]
    public async Task A_cache_that_is_down_does_not_break_reads()
    {
        var harness = Broken();

        var rendered = await harness.Render.RenderAsync(
            new TemplateRenderRequest("welcome-user", "vi", TemplarHarness.Values()));

        Assert.Equal("Chào mừng tới XXX", rendered.Subject);
    }

    /// <summary>
    /// A save evicts the key it wrote, so an unreachable cache would otherwise fail the write. The
    /// eviction is bypassed instead, and the stale entry is left to expire.
    /// </summary>
    [Fact]
    public async Task A_cache_that_is_down_does_not_break_a_save()
    {
        var harness = Broken();

        await harness.Commands!.SaveAsync(TemplarHarness.Seed[0] with { Subject = "Welcome back" });

        Assert.Equal("Welcome back", (await harness.Queries.ResolveAsync("welcome-user", "en"))!.Subject);
    }

    [Fact]
    public async Task A_cache_that_is_down_does_not_break_a_delete()
    {
        var harness = Broken();

        Assert.True(await harness.Commands!.DeleteAsync("welcome-user", "vi", TemplateChannel.Other));
    }

    [Fact]
    public async Task A_cache_that_is_down_does_not_break_a_clear()
    {
        var harness = Broken();

        await harness.Cache.ClearAsync();

        Assert.Equal("Chào mừng tới XXX", (await harness.Queries.ResolveAsync("welcome-user", "vi"))!.Subject);
    }

    private static TemplarHarness Broken()
    {
        var cache = new DistributedTemplateCache(
            new SpyDistributedCache { Fail = true }, Options.Create(new TemplateOptions()));

        return TemplarHarness.Create(new InMemoryTemplateStore(TemplarHarness.Seed), cache: cache);
    }

    /// <summary>
    /// An <see cref="IDistributedCache"/> backed by memory, recording the keys it is asked for and
    /// able to fail on demand. <see cref="MemoryDistributedCache"/> does the real work.
    /// </summary>
    private sealed class SpyDistributedCache : IDistributedCache
    {
        private readonly MemoryDistributedCache _inner = new(Options.Create(new MemoryDistributedCacheOptions()));

        public List<string> Keys { get; } = [];

        public bool Fail { get; init; }

        public byte[]? Get(string key)
        {
            Guard();
            Record(key);
            return _inner.Get(key);
        }

        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            Guard();
            Record(key);
            return await _inner.GetAsync(key, token);
        }

        public void Refresh(string key) => _inner.Refresh(key);

        public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);

        public void Remove(string key) => _inner.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Guard();
            return _inner.RemoveAsync(key, token);
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
            => _inner.Set(key, value, options);

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Guard();
            Record(key);
            return _inner.SetAsync(key, value, options, token);
        }

        private string Record(string key)
        {
            Keys.Add(key);
            return key;
        }

        private void Guard()
        {
            if (Fail) throw new InvalidOperationException("The distributed cache is unavailable.");
        }
    }
}
