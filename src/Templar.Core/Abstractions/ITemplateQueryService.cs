namespace Templar.Abstractions;

/// <summary>
/// The read side: what is stored, and which variant a given request resolves to. Every read is
/// served from one cached store query per template key.
/// </summary>
public interface ITemplateQueryService
{
    /// <summary>Every template key in the store, in ascending order.</summary>
    Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Every stored template — all keys, cultures and channels, including inactive rows — ordered
    /// by key, then culture, then channel. Goes straight to the store, since the cache holds one
    /// entry per key rather than the whole table.
    /// </summary>
    Task<IReadOnlyList<TemplateDefinition>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Every stored variant of one key — all cultures, all channels, including inactive rows.
    /// Empty when the key is unknown.
    /// </summary>
    Task<IReadOnlyList<TemplateDefinition>> GetVariantsAsync(
        string templateKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One exact variant. No culture fallback: <c>vi-VN</c> matches only a row stored as
    /// <c>vi-VN</c>. Inactive rows are returned, since an editor needs to see them.
    /// </summary>
    Task<TemplateDefinition?> FindAsync(
        string templateKey,
        string culture,
        TemplateChannel channel = TemplateChannel.Email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The active variant a render would use, applying culture fallback
    /// (<c>vi-VN</c> → <c>vi</c> → default culture).
    /// </summary>
    Task<TemplateDefinition?> ResolveAsync(
        string templateKey,
        string? culture = null,
        TemplateChannel channel = TemplateChannel.Email,
        CancellationToken cancellationToken = default);
}
