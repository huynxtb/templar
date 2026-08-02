using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Templar.Abstractions;
using Templar.Caching;

namespace Templar.Services;

/// <summary>
/// Default read side. One cached store query per template key; culture fallback and channel
/// selection then happen in memory, so resolving a language costs nothing extra.
/// </summary>
public sealed class TemplateQueryService(
    ITemplateStore store,
    ITemplateCache cache,
    IOptions<TemplateOptions> options,
    ILogger<TemplateQueryService>? logger = null) : ITemplateQueryService
{
    private readonly ITemplateStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ITemplateCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly TemplateOptions _options = TemplateOptions.Validated(options);
    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    public Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken = default)
        => _store.ListTemplateKeysAsync(cancellationToken);

    public Task<IReadOnlyList<TemplateDefinition>> ListAsync(CancellationToken cancellationToken = default)
        => _store.GetAllTemplatesAsync(cancellationToken);

    public async Task<IReadOnlyList<TemplateDefinition>> GetVariantsAsync(
        string templateKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);

        return await _cache.GetOrAddAsync(templateKey, LoadAsync, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TemplateDefinition?> FindAsync(
        string templateKey,
        string culture,
        TemplateChannel channel = TemplateChannel.Email,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);

        var variants = await GetVariantsAsync(templateKey, cancellationToken).ConfigureAwait(false);

        return variants.FirstOrDefault(variant =>
            variant.Channel == channel && CultureFallback.NameComparer.Equals(variant.Culture, culture));
    }

    public async Task<TemplateDefinition?> ResolveAsync(
        string templateKey,
        string? culture = null,
        TemplateChannel channel = TemplateChannel.Email,
        CancellationToken cancellationToken = default)
    {
        var variants = await GetVariantsAsync(templateKey, cancellationToken).ConfigureAwait(false);
        if (variants.Count == 0)
        {
            _logger.LogDebug("Template '{TemplateKey}' does not exist in the store.", templateKey);
            return null;
        }

        var requested = string.IsNullOrWhiteSpace(culture) ? _options.DefaultCulture : culture;
        var candidates = CultureFallback.GetCandidates(requested, _options.DefaultCulture, _options.EnableCultureFallback);

        foreach (var candidate in candidates)
        {
            foreach (var variant in variants)
            {
                if (!variant.IsActive) continue;
                if (variant.Channel != channel) continue;
                if (!CultureFallback.NameComparer.Equals(variant.Culture, candidate)) continue;

                if (!CultureFallback.NameComparer.Equals(candidate, requested))
                {
                    _logger.LogDebug(
                        "Template '{TemplateKey}' has no '{RequestedCulture}' variant; falling back to '{ResolvedCulture}'.",
                        templateKey, requested, variant.Culture);
                }

                return variant;
            }
        }

        return null;
    }

    private Task<IReadOnlyList<TemplateDefinition>> LoadAsync(string templateKey, CancellationToken cancellationToken)
        => _store.GetTemplateSetAsync(templateKey, cancellationToken);
}
