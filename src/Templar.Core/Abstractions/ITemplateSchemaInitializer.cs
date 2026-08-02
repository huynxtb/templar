namespace Templar.Abstractions;

/// <summary>
/// Creates the table / collection and indexes the store needs. Handy for tests, samples and
/// single-binary deployments; production schemas usually come from real migrations instead.
/// </summary>
public interface ITemplateSchemaInitializer
{
    /// <summary>Creates the storage if it does not already exist. Safe to call repeatedly.</summary>
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);
}
