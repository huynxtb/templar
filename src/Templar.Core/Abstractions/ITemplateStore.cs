namespace Templar.Abstractions;

/// <summary>
/// Read access to the template storage. Implemented once per database engine
/// (<c>Templar.MySql</c>, <c>Templar.SqlServer</c>, …).
/// </summary>
/// <remarks>
/// A store returns every row for a key — all cultures and all channels — in one call. Culture
/// fallback and channel selection happen in <see cref="ITemplateQueryService"/>, which keeps reads to
/// one round trip and makes a single cache entry per template key sufficient.
/// </remarks>
public interface ITemplateStore
{
    /// <summary>
    /// Loads every stored variant of <paramref name="templateKey"/>, including inactive rows.
    /// Returns an empty list when the key is unknown.
    /// </summary>
    Task<IReadOnlyList<TemplateDefinition>> GetTemplateSetAsync(
        string templateKey,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the distinct template keys held by the store, in ascending order.</summary>
    Task<IReadOnlyList<string>> ListTemplateKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads every row in the store — all keys, cultures and channels, including inactive ones —
    /// ordered by key, then culture, then channel. Reads the whole table, so it is meant for
    /// administration screens and seeding, not for a render path.
    /// </summary>
    Task<IReadOnlyList<TemplateDefinition>> GetAllTemplatesAsync(CancellationToken cancellationToken = default);
}
