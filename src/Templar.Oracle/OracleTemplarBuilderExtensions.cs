using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Templar.Oracle;
using Templar.Relational;

namespace Templar;

/// <summary>Adds the Oracle store to a <see cref="TemplarBuilder"/>.</summary>
public static class OracleTemplarBuilderExtensions
{
    /// <summary>Stores templates in Oracle Database.</summary>
    /// <example>
    /// <code>
    /// services.AddTemplar()
    ///         .UseOracle(configuration.GetConnectionString("Templates")!, o => o.Schema = "NOTIFY");
    /// </code>
    /// </example>
    public static TemplarBuilder UseOracle(
        this TemplarBuilder builder,
        string connectionString,
        Action<OracleTemplateStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new OracleTemplateStoreOptions { ConnectionString = connectionString };
        configure?.Invoke(options);
        options.Validate();

        return builder.UseRelationalStore(sp =>
            new OracleTemplateStore(options, sp.GetService<ILogger<OracleTemplateStore>>()));
    }
}
