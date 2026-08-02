using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Templar.Caching;

/// <summary>
/// Caches templates in an <see cref="IDistributedCache"/> — Redis, SQL Server, or any other
/// implementation the application registers — so several instances share one copy and an edit made
/// on one node is visible to all of them.
/// </summary>
/// <remarks>
/// <para>
/// Entries are JSON, keyed as <c>{prefix}{generation}:{templateKey}</c>, and expire after
/// <see cref="TemplateOptions.CacheDuration"/>.
/// </para>
/// <para>
/// <see cref="IDistributedCache"/> cannot delete by pattern, so <see cref="ClearAsync"/> instead
/// bumps a shared generation counter, which makes every existing key unreachable and lets the old
/// entries expire on their own. The generation is re-read at most every
/// <see cref="GenerationRefresh"/>, so a clear made on another node takes effect within that window;
/// <see cref="RemoveAsync"/> is exact and immediate.
/// </para>
/// <para>
/// A distributed cache costs a network round trip per read. Templates change rarely, so pairing this
/// with a short in-process cache in front of it is usually worthwhile.
/// </para>
/// <para>
/// Every operation degrades instead of throwing: a cache that cannot be reached is logged and
/// bypassed, so a read falls through to the store and a failed eviction or clear leaves the stale
/// entry to expire on its own rather than failing the save that asked for it.
/// </para>
/// </remarks>
public sealed class DistributedTemplateCache(
    IDistributedCache cache,
    IOptions<TemplateOptions> options,
    ILogger<DistributedTemplateCache>? logger = null) : ITemplateCache
{
    /// <summary>How long a fetched generation counter is trusted before it is read again.</summary>
    public static readonly TimeSpan GenerationRefresh = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // Channels are written by name so adding an enum member cannot change how existing
        // payloads deserialize.
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IDistributedCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));

    private readonly TemplateOptions _options = TemplateOptions.Validated(options);
    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    // Initialisers run in declaration order, so null options are already rejected by _options above.
    private readonly string _prefix = options.Value.CacheKeyPrefix;
    private readonly string _generationKey = $"{options.Value.CacheKeyPrefix}generation";

    private string _generation = "0";
    private long _generationFetchedAt = -1;

    public async ValueTask<IReadOnlyList<TemplateDefinition>> GetOrAddAsync(
        string templateKey,
        Func<string, CancellationToken, Task<IReadOnlyList<TemplateDefinition>>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentNullException.ThrowIfNull(factory);

        var key = await BuildKeyAsync(templateKey, cancellationToken).ConfigureAwait(false);

        var cached = await TryReadAsync(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null) return cached;

        var loaded = await factory(templateKey, cancellationToken).ConfigureAwait(false);

        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(loaded, SerializerOptions);
            await _cache.SetAsync(
                    key,
                    payload,
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _options.CacheDuration },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A cache that is down must not take template rendering with it.
            _logger.LogWarning(exception, "Could not write template '{TemplateKey}' to the distributed cache.", templateKey);
        }

        return loaded;
    }

    public async ValueTask RemoveAsync(string templateKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);

        var key = await BuildKeyAsync(templateKey, cancellationToken).ConfigureAwait(false);

        try
        {
            await _cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A cache that is down must not fail the save or delete that asked for this eviction. The
            // entry then survives until CacheDuration expires it, so this key can be served stale
            // until then — which is why the warning names it.
            _logger.LogWarning(
                exception, "Could not evict template '{TemplateKey}' from the distributed cache.", templateKey);
        }
    }

    public async ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        // Move every reader to a new key space. Old entries are orphaned and expire by themselves.
        var next = Guid.NewGuid().ToString("N")[..8];

        try
        {
            await _cache.SetStringAsync(
                    _generationKey,
                    next,
                    new DistributedCacheEntryOptions(),   // never expires: it is the pointer to the live key space
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The generation is deliberately left alone: bumping it locally would move this node to a
            // key space the others never hear about, and the next refresh would read the old value
            // back anyway. Nothing was cleared, so say so and carry on.
            _logger.LogWarning(exception, "Could not clear the distributed template cache.");
            return;
        }

        Volatile.Write(ref _generation, next);
        Volatile.Write(ref _generationFetchedAt, Stopwatch.GetTimestamp());

        _logger.LogInformation("Cleared the distributed template cache (generation {Generation}).", next);
    }

    private async ValueTask<string> BuildKeyAsync(string templateKey, CancellationToken cancellationToken)
        => $"{_prefix}{await GetGenerationAsync(cancellationToken).ConfigureAwait(false)}:{templateKey}";

    private async ValueTask<string> GetGenerationAsync(CancellationToken cancellationToken)
    {
        var fetchedAt = Volatile.Read(ref _generationFetchedAt);
        if (fetchedAt >= 0 && Stopwatch.GetElapsedTime(fetchedAt) < GenerationRefresh)
            return Volatile.Read(ref _generation);

        try
        {
            var stored = await _cache.GetStringAsync(_generationKey, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(stored)) Volatile.Write(ref _generation, stored);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Could not read the template cache generation; using the last known value.");
        }

        Volatile.Write(ref _generationFetchedAt, Stopwatch.GetTimestamp());
        return Volatile.Read(ref _generation);
    }

    private async ValueTask<IReadOnlyList<TemplateDefinition>?> TryReadAsync(
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = await _cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
            if (payload is null or { Length: 0 }) return null;

            return JsonSerializer.Deserialize<TemplateDefinition[]>(payload, SerializerOptions);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Unreadable or corrupt entry: fall back to the store rather than fail the render.
            _logger.LogWarning(exception, "Ignoring an unreadable distributed cache entry '{Key}'.", key);
            return null;
        }
    }
}
