using Microsoft.Extensions.Options;
using Templar.Abstractions;
using Templar.Rendering;

namespace Templar.Services;

/// <summary>
/// Default render side: asks <see cref="ITemplateQueryService"/> which variant applies, then
/// compiles (cached) and substitutes values into the requested parts.
/// </summary>
public sealed class TemplateRenderService(
    ITemplateQueryService queries,
    ITemplateCompiler compiler,
    ITemplateRenderer renderer,
    IOptions<TemplateOptions> options) : ITemplateRenderService
{
    private readonly ITemplateQueryService _queries = queries ?? throw new ArgumentNullException(nameof(queries));
    private readonly ITemplateCompiler _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
    private readonly ITemplateRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly TemplateOptions _options = TemplateOptions.Validated(options);

    public async Task<RenderedTemplate> RenderAsync(
        TemplateRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var culture = request.Culture ?? _options.DefaultCulture;
        var definition = await _queries
                             .ResolveAsync(request.TemplateKey, culture, request.Channel, cancellationToken)
                             .ConfigureAwait(false)
                         ?? throw new TemplateNotFoundException(request.TemplateKey, culture, request.Channel);

        return Render(definition, request);
    }

    public async Task<RenderedTemplate?> TryRenderAsync(
        TemplateRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var definition = await _queries
            .ResolveAsync(request.TemplateKey, request.Culture, request.Channel, cancellationToken)
            .ConfigureAwait(false);

        return definition is null ? null : Render(definition, request);
    }

    private RenderedTemplate Render(TemplateDefinition definition, TemplateRenderRequest request)
    {
        var formattingCulture = CultureFallback.GetFormattingCulture(definition.Culture);
        var missing = request.MissingVariableBehavior ?? _options.MissingVariableBehavior;

        string? RenderPart(string? source, TemplateParts part, bool html)
        {
            if (!request.Parts.HasFlag(part) || string.IsNullOrEmpty(source)) return null;

            var context = new TemplateRenderContext(
                formattingCulture,
                htmlEncode: html && _options.HtmlEncodeValues,
                missing,
                $"{definition.TemplateKey}/{definition.Culture}/{definition.Channel}/{part}");

            return _renderer.Render(_compiler.Compile(source), request.Values, context);
        }

        return new RenderedTemplate
        {
            TemplateKey = definition.TemplateKey,
            Culture = definition.Culture,
            Channel = definition.Channel,
            Subject = RenderPart(definition.Subject, TemplateParts.Subject, html: false),
            Text = RenderPart(definition.TextBody, TemplateParts.Text, html: false),
            Html = RenderPart(definition.HtmlBody, TemplateParts.Html, html: true),
        };
    }
}
