namespace Templar.Abstractions;

/// <summary>
/// Resolves a template through <see cref="ITemplateQueryService"/> and substitutes the supplied
/// values into it. This is the service application code uses to produce a message.
/// </summary>
/// <remarks>
/// Not to be confused with <c>ITemplateRenderer</c> in <c>Templar.Rendering</c>, which is the
/// engine underneath: it turns one compiled body plus values into a string and knows nothing about
/// storage, cultures or channels.
/// </remarks>
public interface ITemplateRenderService
{
    /// <summary>Resolves and renders a template.</summary>
    /// <exception cref="TemplateNotFoundException">No active template matched.</exception>
    /// <exception cref="TemplateRenderException">
    /// A placeholder had no value (see <see cref="TemplateOptions.MissingVariableBehavior"/>).
    /// </exception>
    Task<RenderedTemplate> RenderAsync(TemplateRenderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// As <see cref="RenderAsync"/>, but returns <see langword="null"/> instead of throwing when no
    /// template matched. A missing value still throws.
    /// </summary>
    Task<RenderedTemplate?> TryRenderAsync(TemplateRenderRequest request, CancellationToken cancellationToken = default);
}
