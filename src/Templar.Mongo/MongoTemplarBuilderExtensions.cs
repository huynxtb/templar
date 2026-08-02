using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Templar.Abstractions;
using Templar.Mongo;

namespace Templar;

/// <summary>Adds the MongoDB store to a <see cref="TemplarBuilder"/>.</summary>
public static class MongoTemplarBuilderExtensions
{
    /// <summary>Stores templates in MongoDB.</summary>
    /// <example>
    /// <code>
    /// services.AddTemplar()
    ///         .UseMongo("mongodb://localhost:27017", o => o.DatabaseName = "notifications");
    /// </code>
    /// </example>
    public static TemplarBuilder UseMongo(
        this TemplarBuilder builder,
        string connectionString,
        Action<MongoTemplateStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new MongoTemplateStoreOptions { ConnectionString = connectionString };
        configure?.Invoke(options);
        options.Validate();

        // MongoClient owns the connection pool and is designed to be shared, so the collection is a
        // singleton; the store around it is scoped like every other database access.
        builder.Services.AddSingleton<IMongoCollection<MongoTemplateDocument>>(_ =>
            new MongoClient(options.ConnectionString)
                .GetDatabase(options.DatabaseName)
                .GetCollection<MongoTemplateDocument>(options.CollectionName));

        builder.Services.AddScoped(sp => new MongoTemplateStore(
            sp.GetRequiredService<IMongoCollection<MongoTemplateDocument>>(),
            sp.GetService<ILogger<MongoTemplateStore>>()));

        builder.Services.AddScoped<ITemplateStore>(sp => sp.GetRequiredService<MongoTemplateStore>());
        builder.Services.AddScoped<ITemplateWriteStore>(sp => sp.GetRequiredService<MongoTemplateStore>());
        builder.Services.AddScoped<ITemplateSchemaInitializer>(sp => sp.GetRequiredService<MongoTemplateStore>());

        return builder;
    }
}
