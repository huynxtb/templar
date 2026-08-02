using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Templar.Relational;
using Templar.SqlServer;

namespace Templar;

/// <summary>Adds the SQL Server store to a <see cref="TemplarBuilder"/>.</summary>
public static class SqlServerTemplarBuilderExtensions
{
    /// <summary>Stores templates in Microsoft SQL Server or Azure SQL.</summary>
    /// <example>
    /// <code>
    /// services.AddTemplar()
    ///         .UseSqlServer(configuration.GetConnectionString("Templates")!, o => o.Schema = "notify");
    /// </code>
    /// </example>
    public static TemplarBuilder UseSqlServer(
        this TemplarBuilder builder,
        string connectionString,
        Action<SqlServerTemplateStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new SqlServerTemplateStoreOptions { ConnectionString = connectionString };
        configure?.Invoke(options);
        options.Validate();

        return builder.UseRelationalStore(sp =>
            new SqlServerTemplateStore(options, sp.GetService<ILogger<SqlServerTemplateStore>>()));
    }
}
