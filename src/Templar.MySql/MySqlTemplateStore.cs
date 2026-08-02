using System.Data.Common;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Templar.Relational;

namespace Templar.MySql;

/// <summary>Options for the MySQL / MariaDB store.</summary>
public sealed class MySqlTemplateStoreOptions : RelationalTemplateStoreOptions;

/// <summary>Template store backed by MySQL or MariaDB.</summary>
public sealed class MySqlTemplateStore(MySqlTemplateStoreOptions options, ILogger<MySqlTemplateStore>? logger = null)
    : RelationalTemplateStore(options, logger)
{
    protected override string ParameterPrefix => "@";

    protected override DbConnection CreateConnection() => new MySqlConnection(Options.ConnectionString);

    protected override string QuoteIdentifier(string identifier) => $"`{identifier.Replace("`", "``")}`";

    /// <summary>MySQL stores the flag in a <c>TINYINT(1)</c>.</summary>
    protected override object ConvertIsActive(bool isActive) => isActive ? 1 : 0;

    protected override IReadOnlyList<string> GetSchemaStatements() =>
    [
        $"""
        CREATE TABLE IF NOT EXISTS {Table} (
            {QuoteIdentifier(Columns.TemplateKey)} VARCHAR(200)  NOT NULL,
            {QuoteIdentifier(Columns.Culture)}     VARCHAR(20)   NOT NULL,
            {QuoteIdentifier(Columns.Channel)}     VARCHAR(20)   NOT NULL,
            {QuoteIdentifier(Columns.Name)}        VARCHAR(200)  NULL,
            {QuoteIdentifier(Columns.Description)} VARCHAR(1000) NULL,
            {QuoteIdentifier(Columns.Subject)}     VARCHAR(1000) NULL,
            {QuoteIdentifier(Columns.TextBody)}    LONGTEXT      NULL,
            {QuoteIdentifier(Columns.HtmlBody)}    LONGTEXT      NULL,
            {QuoteIdentifier(Columns.IsActive)}    TINYINT(1)    NOT NULL DEFAULT 1,
            {QuoteIdentifier(Columns.UpdatedAt)}   DATETIME(6)   NOT NULL,
            PRIMARY KEY ({QuoteIdentifier(Columns.TemplateKey)}, {QuoteIdentifier(Columns.Culture)}, {QuoteIdentifier(Columns.Channel)})
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """,
    ];

    /// <summary>
    /// Uses <c>ON DUPLICATE KEY UPDATE</c> against the composite primary key. The deprecated
    /// <c>VALUES()</c> function is used rather than a row alias so the statement also runs on
    /// MariaDB and on MySQL before 8.0.19.
    /// </summary>
    protected override string BuildUpsertSql() =>
        $"""
        INSERT INTO {Table} (
            {QuoteIdentifier(Columns.TemplateKey)}, {QuoteIdentifier(Columns.Culture)}, {QuoteIdentifier(Columns.Channel)},
            {QuoteIdentifier(Columns.Name)}, {QuoteIdentifier(Columns.Description)},
            {QuoteIdentifier(Columns.Subject)}, {QuoteIdentifier(Columns.TextBody)}, {QuoteIdentifier(Columns.HtmlBody)},
            {QuoteIdentifier(Columns.IsActive)}, {QuoteIdentifier(Columns.UpdatedAt)}
        ) VALUES (
            {Parameter("TemplateKey")}, {Parameter("Culture")}, {Parameter("Channel")},
            {Parameter("Name")}, {Parameter("Description")},
            {Parameter("Subject")}, {Parameter("TextBody")}, {Parameter("HtmlBody")},
            {Parameter("IsActive")}, {Parameter("UpdatedAt")}
        )
        ON DUPLICATE KEY UPDATE
            {QuoteIdentifier(Columns.Name)}        = VALUES({QuoteIdentifier(Columns.Name)}),
            {QuoteIdentifier(Columns.Description)} = VALUES({QuoteIdentifier(Columns.Description)}),
            {QuoteIdentifier(Columns.Subject)}   = VALUES({QuoteIdentifier(Columns.Subject)}),
            {QuoteIdentifier(Columns.TextBody)}  = VALUES({QuoteIdentifier(Columns.TextBody)}),
            {QuoteIdentifier(Columns.HtmlBody)}  = VALUES({QuoteIdentifier(Columns.HtmlBody)}),
            {QuoteIdentifier(Columns.IsActive)}  = VALUES({QuoteIdentifier(Columns.IsActive)}),
            {QuoteIdentifier(Columns.UpdatedAt)} = VALUES({QuoteIdentifier(Columns.UpdatedAt)})
        """;
}
