namespace Templar.Caching;

/// <summary>
/// Caches the set of rows belonging to one template key. An implementation may be in-process
/// (<see cref="MemoryTemplateCache"/>) or shared between instances
/// (<see cref="DistributedTemplateCache"/>) — which is why eviction is asynchronous.
/// </summary>
public interface ITemplateCache
{
    /// <summary>
    /// Returns the cached set for <paramref name="templateKey"/>, invoking
    /// <paramref name="factory"/> on a miss.
    /// </summary>
    ValueTask<IReadOnlyList<TemplateDefinition>> GetOrAddAsync(
        string templateKey,
        Func<string, CancellationToken, Task<IReadOnlyList<TemplateDefinition>>> factory,
        CancellationToken cancellationToken = default);

    /// <summary>Evicts one template key.</summary>
    ValueTask RemoveAsync(string templateKey, CancellationToken cancellationToken = default);

    /// <summary>Evicts everything.</summary>
    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}
