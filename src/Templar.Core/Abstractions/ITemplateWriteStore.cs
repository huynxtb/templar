namespace Templar.Abstractions;

/// <summary>Write access, used by admin tooling and by seeding code.</summary>
public interface ITemplateWriteStore : ITemplateStore
{
    /// <summary>
    /// Inserts <paramref name="template"/>, or replaces the existing row with the same
    /// (key, culture, channel).
    /// </summary>
    Task UpsertAsync(TemplateDefinition template, CancellationToken cancellationToken = default);

    /// <summary>Deletes one row. Returns false when nothing matched.</summary>
    Task<bool> DeleteAsync(
        string templateKey,
        string culture,
        TemplateChannel channel,
        CancellationToken cancellationToken = default);
}
