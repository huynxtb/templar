using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Templar.PostgreSql;
using Templar.Relational;

namespace Templar;

/// <summary>Adds the PostgreSQL store to a <see cref="TemplarBuilder"/>.</summary>
public static class PostgreSqlTemplarBuilderExtensions
{
    /// <summary>Stores templates in PostgreSQL.</summary>
    /// <example>
    /// <code>
    /// services.AddTemplar()
    ///         .UsePostgreSql(configuration.GetConnectionString("Templates")!);
    /// </code>
    /// </example>
    public static TemplarBuilder UsePostgreSql(
        this TemplarBuilder builder,
        string connectionString,
        Action<PostgreSqlTemplateStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new PostgreSqlTemplateStoreOptions { ConnectionString = connectionString };
        configure?.Invoke(options);
        options.Validate();

        return builder.UseRelationalStore(sp =>
            new PostgreSqlTemplateStore(options, sp.GetService<ILogger<PostgreSqlTemplateStore>>()));
    }
}
