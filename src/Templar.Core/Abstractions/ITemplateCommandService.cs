namespace Templar.Abstractions;

/// <summary>
/// The write side: create, update and delete stored templates.
/// </summary>
/// <remarks>
/// Every command evicts the affected key from the read cache, so a save is visible to the next
/// query or render immediately. That is the reason to prefer this over calling
/// <see cref="ITemplateWriteStore"/> directly.
/// </remarks>
public interface ITemplateCommandService
{
    /// <summary>
    /// Creates the template, or replaces the stored row with the same (key, culture, channel).
    /// </summary>
    Task SaveAsync(TemplateDefinition template, CancellationToken cancellationToken = default);

    /// <summary>Saves several templates — for example every language of one key.</summary>
    Task SaveAsync(IEnumerable<TemplateDefinition> templates, CancellationToken cancellationToken = default);

    /// <summary>Deletes one variant. Returns false when nothing matched.</summary>
    Task<bool> DeleteAsync(
        string templateKey,
        string culture,
        TemplateChannel channel = TemplateChannel.Email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops cached templates so the next read hits the database. Commands do this for you; call it
    /// yourself only after writing through <see cref="ITemplateWriteStore"/> or changing the table
    /// from outside the application. Pass <see langword="null"/> to clear every key.
    /// </summary>
    ValueTask InvalidateAsync(string? templateKey = null, CancellationToken cancellationToken = default);
}
