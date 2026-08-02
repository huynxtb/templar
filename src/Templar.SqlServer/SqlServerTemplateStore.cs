using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Templar.Relational;

namespace Templar.SqlServer;

/// <summary>Options for the SQL Server store.</summary>
public sealed class SqlServerTemplateStoreOptions : RelationalTemplateStoreOptions
{
    public SqlServerTemplateStoreOptions() => Schema = "dbo";
}

/// <summary>Template store backed by Microsoft SQL Server (or Azure SQL).</summary>
public sealed class SqlServerTemplateStore(
    SqlServerTemplateStoreOptions options,
    ILogger<SqlServerTemplateStore>? logger = null) : RelationalTemplateStore(options, logger)
{
    protected override string ParameterPrefix => "@";

    protected override DbConnection CreateConnection() => new SqlConnection(Options.ConnectionString);

    protected override string QuoteIdentifier(string identifier) => $"[{identifier.Replace("]", "]]")}]";

    protected override IReadOnlyList<string> GetSchemaStatements() =>
    [
        $"""
        IF OBJECT_ID(N'{Table.Replace("'", "''")}', N'U') IS NULL
        BEGIN
            CREATE TABLE {Table} (
                {QuoteIdentifier(Columns.TemplateKey)} NVARCHAR(200)  NOT NULL,
                {QuoteIdentifier(Columns.Culture)}     NVARCHAR(20)   NOT NULL,
                {QuoteIdentifier(Columns.Channel)}     NVARCHAR(20)   NOT NULL,
                {QuoteIdentifier(Columns.Name)}        NVARCHAR(200)  NULL,
                {QuoteIdentifier(Columns.Description)} NVARCHAR(1000) NULL,
                {QuoteIdentifier(Columns.Subject)}     NVARCHAR(1000) NULL,
                {QuoteIdentifier(Columns.TextBody)}    NVARCHAR(MAX)  NULL,
                {QuoteIdentifier(Columns.HtmlBody)}    NVARCHAR(MAX)  NULL,
                {QuoteIdentifier(Columns.IsActive)}    BIT            NOT NULL DEFAULT 1,
                {QuoteIdentifier(Columns.UpdatedAt)}   DATETIME2(3)   NOT NULL,
                CONSTRAINT {QuoteIdentifier($"PK_{Options.TableName}")} PRIMARY KEY CLUSTERED (
                    {QuoteIdentifier(Columns.TemplateKey)}, {QuoteIdentifier(Columns.Culture)}, {QuoteIdentifier(Columns.Channel)}
                )
            );
        END
        """,
    ];

    /// <summary>
    /// <c>MERGE … WITH (HOLDLOCK)</c> is the documented race-free upsert on SQL Server: the hint
    /// keeps the key range locked between the existence check and the insert.
    /// </summary>
    protected override string BuildUpsertSql() =>
        $"""
        MERGE {Table} WITH (HOLDLOCK) AS target
        USING (SELECT {Parameter("TemplateKey")} AS {QuoteIdentifier(Columns.TemplateKey)},
                      {Parameter("Culture")}     AS {QuoteIdentifier(Columns.Culture)},
                      {Parameter("Channel")}     AS {QuoteIdentifier(Columns.Channel)}) AS source
            ON  target.{QuoteIdentifier(Columns.TemplateKey)} = source.{QuoteIdentifier(Columns.TemplateKey)}
            AND target.{QuoteIdentifier(Columns.Culture)}     = source.{QuoteIdentifier(Columns.Culture)}
            AND target.{QuoteIdentifier(Columns.Channel)}     = source.{QuoteIdentifier(Columns.Channel)}
        WHEN MATCHED THEN UPDATE SET
            {QuoteIdentifier(Columns.Name)}        = {Parameter("Name")},
            {QuoteIdentifier(Columns.Description)} = {Parameter("Description")},
            {QuoteIdentifier(Columns.Subject)}   = {Parameter("Subject")},
            {QuoteIdentifier(Columns.TextBody)}  = {Parameter("TextBody")},
            {QuoteIdentifier(Columns.HtmlBody)}  = {Parameter("HtmlBody")},
            {QuoteIdentifier(Columns.IsActive)}  = {Parameter("IsActive")},
            {QuoteIdentifier(Columns.UpdatedAt)} = {Parameter("UpdatedAt")}
        WHEN NOT MATCHED THEN INSERT (
            {QuoteIdentifier(Columns.TemplateKey)}, {QuoteIdentifier(Columns.Culture)}, {QuoteIdentifier(Columns.Channel)},
            {QuoteIdentifier(Columns.Name)}, {QuoteIdentifier(Columns.Description)},
            {QuoteIdentifier(Columns.Subject)}, {QuoteIdentifier(Columns.TextBody)}, {QuoteIdentifier(Columns.HtmlBody)},
            {QuoteIdentifier(Columns.IsActive)}, {QuoteIdentifier(Columns.UpdatedAt)}
        ) VALUES (
            {Parameter("TemplateKey")}, {Parameter("Culture")}, {Parameter("Channel")},
            {Parameter("Name")}, {Parameter("Description")},
            {Parameter("Subject")}, {Parameter("TextBody")}, {Parameter("HtmlBody")},
            {Parameter("IsActive")}, {Parameter("UpdatedAt")}
        );
        """;
}
