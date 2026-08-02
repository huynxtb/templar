using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Templar.MySql;
using Templar.Relational;

namespace Templar;

/// <summary>Adds the MySQL / MariaDB store to a <see cref="TemplarBuilder"/>.</summary>
public static class MySqlTemplarBuilderExtensions
{
    /// <summary>
    /// Stores templates in MySQL or MariaDB.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddTemplar(o => o.DefaultCulture = "en")
    ///         .UseMySql(configuration.GetConnectionString("Templates")!);
    /// </code>
    /// </example>
    public static TemplarBuilder UseMySql(
        this TemplarBuilder builder,
        string connectionString,
        Action<MySqlTemplateStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new MySqlTemplateStoreOptions { ConnectionString = connectionString };
        configure?.Invoke(options);
        options.Validate();

        return builder.UseRelationalStore(sp =>
            new MySqlTemplateStore(options, sp.GetService<ILogger<MySqlTemplateStore>>()));
    }
}
