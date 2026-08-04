using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Templar.Abstractions;
using Templar.Caching;
using Templar.Rendering;
using Templar.Scriban;
using Templar.Services;
using Templar.Stores;

namespace Templar;

/// <summary>Registration helpers for the core services.</summary>
public static class TemplarServiceCollectionExtensions
{
    /// <summary>
    /// Registers the rendering engine, the cache and the three services —
    /// <see cref="ITemplateQueryService"/>, <see cref="ITemplateCommandService"/> and
    /// <see cref="ITemplateRenderService"/> — plus <see cref="ITemplateChannelService"/>. A database
    /// provider must be added on the returned builder, otherwise nothing supplies
    /// <see cref="ITemplateStore"/> and resolving a service will fail.
    /// </summary>
    /// <remarks>
    /// The engine is Scriban, so stored bodies can use <c>{{ if }}</c>, <c>{{ for }}</c> and pipes
    /// without any further registration. Call
    /// <see cref="ScribanTemplarBuilderExtensions.UseScriban"/> to tune it.
    /// </remarks>
    public static TemplarBuilder AddTemplar(
        this IServiceCollection services,
        Action<TemplateOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<TemplateOptions>();
        if (configure is not null) optionsBuilder.Configure(configure);

        services.AddOptions<ScribanOptions>();

        // Stateless and thread-safe: the compiler and renderer hold only their own caches, and the
        // channel list is fixed at compile time.
        services.TryAddSingleton<ITemplateCompiler>(provider => new ScribanTemplateCompiler(
            ScribanOptions.Validated(provider.GetRequiredService<IOptions<ScribanOptions>>()),
            provider.GetRequiredService<IOptions<TemplateOptions>>()));
        services.TryAddSingleton<ITemplateRenderer>(provider => new ScribanTemplateRenderer(
            ScribanOptions.Validated(provider.GetRequiredService<IOptions<ScribanOptions>>())));
        services.TryAddSingleton<ITemplateChannelService, TemplateChannelService>();

        services.TryAddSingleton<ITemplateCache>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<TemplateOptions>>();
            return options.Value.EnableCache
                ? new MemoryTemplateCache(options)
                : NullTemplateCache.Instance;
        });

        // These three talk to the database, so they are scoped like a DbContext or unit of work.
        services.TryAddScoped<ITemplateQueryService, TemplateQueryService>();
        services.TryAddScoped<ITemplateRenderService, TemplateRenderService>();
        services.TryAddScoped<ITemplateCommandService, TemplateCommandService>();

        return new TemplarBuilder(services);
    }

    /// <summary>
    /// Caches templates in the application's <see cref="IDistributedCache"/> — Redis, SQL Server, or
    /// any other implementation — instead of in process memory, so every instance shares one copy
    /// and an edit on one node is seen by the others.
    /// </summary>
    /// <remarks>
    /// Register the distributed cache itself as usual, for example
    /// <c>services.AddStackExchangeRedisCache(…)</c>. Set
    /// <see cref="TemplateOptions.CacheKeyPrefix"/> when several applications share one instance.
    /// This has no effect when <see cref="TemplateOptions.EnableCache"/> is false.
    /// </remarks>
    public static TemplarBuilder UseDistributedCache(this TemplarBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.RemoveAll<ITemplateCache>();
        builder.Services.AddSingleton<ITemplateCache>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<TemplateOptions>>();
            if (!options.Value.EnableCache) return NullTemplateCache.Instance;

            return new DistributedTemplateCache(
                provider.GetRequiredService<IDistributedCache>(),
                options,
                provider.GetService<ILogger<DistributedTemplateCache>>());
        });

        return builder;
    }

    /// <summary>
    /// Uses an <see cref="InMemoryTemplateStore"/>. Intended for tests and samples; templates
    /// disappear when the process exits.
    /// </summary>
    /// <remarks>
    /// Registered as a singleton, unlike the database providers: this store *is* the data, so a
    /// scoped instance would start empty on every request.
    /// </remarks>
    public static TemplarBuilder UseInMemoryStore(
        this TemplarBuilder builder,
        IEnumerable<TemplateDefinition>? seed = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var store = seed is null ? new InMemoryTemplateStore() : new InMemoryTemplateStore(seed);

        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton<ITemplateStore>(store);
        builder.Services.AddSingleton<ITemplateWriteStore>(store);

        return builder;
    }
}
