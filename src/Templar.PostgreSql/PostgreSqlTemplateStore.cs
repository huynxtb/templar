using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Npgsql;
using Templar.Relational;

namespace Templar.PostgreSql;

/// <summary>Options for the PostgreSQL store.</summary>
public sealed class PostgreSqlTemplateStoreOptions : RelationalTemplateStoreOptions
{
    public PostgreSqlTemplateStoreOptions() => Schema = "public";
}

/// <summary>Template store backed by PostgreSQL.</summary>
public sealed class PostgreSqlTemplateStore(
    PostgreSqlTemplateStoreOptions options,
    ILogger<PostgreSqlTemplateStore>? logger = null) : RelationalTemplateStore(options, logger)
{
    protected override string ParameterPrefix => "@";

    protected override DbConnection CreateConnection() => new NpgsqlConnection(Options.ConnectionString);

    protected override string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    /// <summary>
    /// Left unset so Npgsql infers <c>timestamptz</c> from the value's UTC kind; forcing
    /// <see cref="DbType.DateTime2"/> would bind a <c>timestamp without time zone</c> instead and
    /// the insert would be rejected.
    /// </summary>
    protected override DbType? TimestampDbType => null;

    protected override IReadOnlyList<string> GetSchemaStatements() =>
    [
        $"""
        CREATE TABLE IF NOT EXISTS {Table} (
            {QuoteIdentifier(Columns.TemplateKey)} varchar(200) NOT NULL,
            {QuoteIdentifier(Columns.Culture)}     varchar(20)  NOT NULL,
            {QuoteIdentifier(Columns.Channel)}     varchar(20)  NOT NULL,
            {QuoteIdentifier(Columns.Name)}        varchar(200) NULL,
            {QuoteIdentifier(Columns.Description)} text         NULL,
            {QuoteIdentifier(Columns.Subject)}     text         NULL,
            {QuoteIdentifier(Columns.TextBody)}    text         NULL,
            {QuoteIdentifier(Columns.HtmlBody)}    text         NULL,
            {QuoteIdentifier(Columns.IsActive)}    boolean      NOT NULL DEFAULT true,
            {QuoteIdentifier(Columns.UpdatedAt)}   timestamptz  NOT NULL,
            PRIMARY KEY ({QuoteIdentifier(Columns.TemplateKey)}, {QuoteIdentifier(Columns.Culture)}, {QuoteIdentifier(Columns.Channel)})
        )
        """,
    ];

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
        ON CONFLICT ({QuoteIdentifier(Columns.TemplateKey)}, {QuoteIdentifier(Columns.Culture)}, {QuoteIdentifier(Columns.Channel)})
        DO UPDATE SET
            {QuoteIdentifier(Columns.Name)}        = EXCLUDED.{QuoteIdentifier(Columns.Name)},
            {QuoteIdentifier(Columns.Description)} = EXCLUDED.{QuoteIdentifier(Columns.Description)},
            {QuoteIdentifier(Columns.Subject)}   = EXCLUDED.{QuoteIdentifier(Columns.Subject)},
            {QuoteIdentifier(Columns.TextBody)}  = EXCLUDED.{QuoteIdentifier(Columns.TextBody)},
            {QuoteIdentifier(Columns.HtmlBody)}  = EXCLUDED.{QuoteIdentifier(Columns.HtmlBody)},
            {QuoteIdentifier(Columns.IsActive)}  = EXCLUDED.{QuoteIdentifier(Columns.IsActive)},
            {QuoteIdentifier(Columns.UpdatedAt)} = EXCLUDED.{QuoteIdentifier(Columns.UpdatedAt)}
        """;
}
