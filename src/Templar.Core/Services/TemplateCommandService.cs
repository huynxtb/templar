using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Templar.Abstractions;
using Templar.Caching;

namespace Templar.Services;

/// <summary>
/// Default write side: forwards to <see cref="ITemplateWriteStore"/> and evicts the affected key
/// from the read cache, which is the step that is easy to forget when writing to the store directly.
/// </summary>
public sealed class TemplateCommandService(
    ITemplateWriteStore store,
    ITemplateCache cache,
    ILogger<TemplateCommandService>? logger = null) : ITemplateCommandService
{
    private readonly ITemplateWriteStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ITemplateCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    public async Task SaveAsync(TemplateDefinition template, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);

        await _store.UpsertAsync(template, cancellationToken).ConfigureAwait(false);
        await InvalidateAsync(template.TemplateKey, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Saved template {Template}.", template);
    }

    public async Task SaveAsync(
        IEnumerable<TemplateDefinition> templates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(templates);

        foreach (var template in templates)
        {
            ArgumentNullException.ThrowIfNull(template);

            await _store.UpsertAsync(template, cancellationToken).ConfigureAwait(false);
            await InvalidateAsync(template.TemplateKey, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> DeleteAsync(
        string templateKey,
        string culture,
        TemplateChannel channel = TemplateChannel.Email,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);

        var deleted = await _store.DeleteAsync(templateKey, culture, channel, cancellationToken).ConfigureAwait(false);
        if (deleted)
        {
            await InvalidateAsync(templateKey, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Deleted template {TemplateKey}/{Culture}/{Channel}.", templateKey, culture, channel);
        }

        return deleted;
    }

    public ValueTask InvalidateAsync(string? templateKey = null, CancellationToken cancellationToken = default)
        => string.IsNullOrWhiteSpace(templateKey)
            ? _cache.ClearAsync(cancellationToken)
            : _cache.RemoveAsync(templateKey, cancellationToken);
}
