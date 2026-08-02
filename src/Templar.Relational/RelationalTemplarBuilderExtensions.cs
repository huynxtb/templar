using Microsoft.Extensions.DependencyInjection;
using Templar.Abstractions;

namespace Templar.Relational;

/// <summary>Registration helper shared by the SQL providers.</summary>
public static class RelationalTemplarBuilderExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TStore"/> and points <see cref="ITemplateStore"/>,
    /// <see cref="ITemplateWriteStore"/> and <see cref="ITemplateSchemaInitializer"/> at it.
    /// </summary>
    /// <remarks>
    /// Scoped, like any other database access: the store opens and closes a connection per call, so
    /// one instance per request keeps its lifetime aligned with the request it serves. Resolve it
    /// inside a scope — <c>IServiceProvider.CreateScope()</c> — when using it outside a request.
    /// </remarks>
    public static TemplarBuilder UseRelationalStore<TStore>(
        this TemplarBuilder builder,
        Func<IServiceProvider, TStore> factory)
        where TStore : RelationalTemplateStore
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);

        builder.Services.AddScoped(factory);
        builder.Services.AddScoped<ITemplateStore>(sp => sp.GetRequiredService<TStore>());
        builder.Services.AddScoped<ITemplateWriteStore>(sp => sp.GetRequiredService<TStore>());
        builder.Services.AddScoped<ITemplateSchemaInitializer>(sp => sp.GetRequiredService<TStore>());

        return builder;
    }
}
