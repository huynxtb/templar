namespace Templar.Caching;

/// <summary>
/// Pass-through cache used when <see cref="TemplateOptions.EnableCache"/> is false: every render
/// re-reads the store, which is what you want while authoring templates.
/// </summary>
public sealed class NullTemplateCache : ITemplateCache
{
    public static readonly NullTemplateCache Instance = new();

    public async ValueTask<IReadOnlyList<TemplateDefinition>> GetOrAddAsync(
        string templateKey,
        Func<string, CancellationToken, Task<IReadOnlyList<TemplateDefinition>>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return await factory(templateKey, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask RemoveAsync(string templateKey, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}
