using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Templar.Abstractions;

namespace Templar.Caching;

/// <summary>
/// In-process cache backed by its own <see cref="MemoryCache"/> instance, so clearing it never
/// touches the application's shared cache.
/// </summary>
/// <remarks>
/// Both hits and misses are cached for <see cref="TemplateOptions.CacheDuration"/>. Concurrent
/// callers for the same key share one database round trip. Writes made directly through
/// <see cref="ITemplateWriteStore"/> are not observed automatically — call
/// <see cref="Abstractions.ITemplateCommandService.InvalidateAsync"/> after editing a template.
/// </remarks>
public sealed class MemoryTemplateCache(IOptions<TemplateOptions> options) : ITemplateCache, IDisposable
{
    private readonly TimeSpan _duration = TemplateOptions.Validated(options).CacheDuration;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public async ValueTask<IReadOnlyList<TemplateDefinition>> GetOrAddAsync(
        string templateKey,
        Func<string, CancellationToken, Task<IReadOnlyList<TemplateDefinition>>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentNullException.ThrowIfNull(factory);

        if (_cache.TryGetValue(templateKey, out Task<IReadOnlyList<TemplateDefinition>>? cached) && cached is not null)
            return await cached.ConfigureAwait(false);

        // The task itself is cached so simultaneous callers wait on a single load. A cancellation
        // token from one caller must not poison the shared task, hence CancellationToken.None
        // is deliberately *not* used here: the factory gets the first caller's token, and a failed
        // task is evicted immediately below so the next caller retries.
        var entry = _cache.GetOrCreate(templateKey, cacheEntry =>
        {
            cacheEntry.AbsoluteExpirationRelativeToNow = _duration;
            return factory(templateKey, cancellationToken);
        })!;

        try
        {
            return await entry.ConfigureAwait(false);
        }
        catch
        {
            _cache.Remove(templateKey);
            throw;
        }
    }

    public ValueTask RemoveAsync(string templateKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);

        _cache.Remove(templateKey);
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        _cache.Clear();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => _cache.Dispose();
}
