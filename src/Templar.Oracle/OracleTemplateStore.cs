using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;
using Templar.Relational;

namespace Templar.Oracle;

/// <summary>Options for the Oracle store.</summary>
public sealed class OracleTemplateStoreOptions : RelationalTemplateStoreOptions
{
    /// <summary>
    /// By default identifiers are upper-cased before quoting, which is how Oracle stores names
    /// created by conventional DDL. Set this to true when the table was created with quoted
    /// lower-case names.
    /// </summary>
    public bool PreserveIdentifierCase { get; set; }
}

/// <summary>Template store backed by Oracle Database.</summary>
public sealed class OracleTemplateStore(OracleTemplateStoreOptions options, ILogger<OracleTemplateStore>? logger = null)
    : RelationalTemplateStore(options, logger)
{
    protected override string ParameterPrefix => ":";

    protected override DbConnection CreateConnection() => new OracleConnection(Options.ConnectionString);

    protected override string QuoteIdentifier(string identifier)
    {
        var name = options.PreserveIdentifierCase ? identifier : identifier.ToUpperInvariant();
        return $"\"{name.Replace("\"", "\"\"")}\"";
    }

    /// <summary>
    /// ODP.NET binds parameters positionally unless told otherwise; named binding is required here
    /// because the generated MERGE mentions parameters in a different order than they are added.
    /// </summary>
    protected override void PrepareCommand(DbCommand command)
    {
        if (command is OracleCommand oracleCommand) oracleCommand.BindByName = true;
    }

    /// <summary>Oracle has no boolean column type; the flag lives in a <c>NUMBER(1)</c>.</summary>
    protected override object ConvertIsActive(bool isActive) => isActive ? 1 : 0;

    /// <summary>ODP.NET maps <see cref="DbType.DateTime"/> to <c>TIMESTAMP</c>, keeping sub-second precision.</summary>
    protected override DbType? TimestampDbType => DbType.DateTime;

    /// <summary>
    /// Bodies are CLOBs, so they are bound explicitly: a default string bind is treated as a
    /// VARCHAR2 and rejects values longer than 4000 bytes.
    /// </summary>
    protected override void AddTemplateParameters(DbCommand command, TemplateDefinition template)
    {
        AddParameter(command, "TemplateKey", template.TemplateKey, DbType.String);
        AddParameter(command, "Culture", template.Culture, DbType.String);
        AddParameter(command, "Channel", template.Channel.ToString(), DbType.String);
        AddParameter(command, "Name", template.Name, DbType.String);
        AddParameter(command, "Description", template.Description, DbType.String);
        AddParameter(command, "Subject", template.Subject, DbType.String);
        AddClobParameter(command, "TextBody", template.TextBody);
        AddClobParameter(command, "HtmlBody", template.HtmlBody);
        AddParameter(command, "IsActive", ConvertIsActive(template.IsActive), DbType.Int32);
        AddParameter(command, "UpdatedAt", template.UpdatedAtUtc.UtcDateTime, TimestampDbType);
    }

    private static void AddClobParameter(DbCommand command, string name, string? value)
    {
        var parameter = new OracleParameter(name, OracleDbType.Clob)
        {
            Value = value ?? (object)DBNull.Value,
        };

        command.Parameters.Add(parameter);
    }

    protected override IReadOnlyList<string> GetSchemaStatements() =>
    [
        // Oracle has no CREATE TABLE IF NOT EXISTS before 23c, so ORA-00955 ("name is already
        // used by an existing object") is swallowed to keep the call idempotent.
        $"""
        BEGIN
            EXECUTE IMMEDIATE '
                CREATE TABLE {Table} (
                    {QuoteIdentifier(Columns.TemplateKey)} VARCHAR2(200 CHAR) NOT NULL,
                    {QuoteIdentifier(Columns.Culture)}     VARCHAR2(20 CHAR)  NOT NULL,
                    {QuoteIdentifier(Columns.Channel)}     VARCHAR2(20 CHAR)  NOT NULL,
                    {QuoteIdentifier(Columns.Name)}        VARCHAR2(200 CHAR),
                    {QuoteIdentifier(Columns.Description)} VARCHAR2(1000 CHAR),
                    {QuoteIdentifier(Columns.Subject)}     VARCHAR2(1000 CHAR),
                    {QuoteIdentifier(Columns.TextBody)}    CLOB,
                    {QuoteIdentifier(Columns.HtmlBody)}    CLOB,
                    {QuoteIdentifier(Columns.IsActive)}    NUMBER(1)  DEFAULT 1 NOT NULL,
                    {QuoteIdentifier(Columns.UpdatedAt)}   TIMESTAMP(3) NOT NULL,
                    CONSTRAINT {QuoteIdentifier($"PK_{Options.TableName}")} PRIMARY KEY (
                        {QuoteIdentifier(Columns.TemplateKey)}, {QuoteIdentifier(Columns.Culture)}, {QuoteIdentifier(Columns.Channel)}
                    )
                )';
        EXCEPTION
            WHEN OTHERS THEN
                IF SQLCODE != -955 THEN RAISE; END IF;
        END;
        """,
    ];

    protected override string BuildUpsertSql() =>
        $"""
        MERGE INTO {Table} target
        USING (SELECT {Parameter("TemplateKey")} AS {QuoteIdentifier(Columns.TemplateKey)},
                      {Parameter("Culture")}     AS {QuoteIdentifier(Columns.Culture)},
                      {Parameter("Channel")}     AS {QuoteIdentifier(Columns.Channel)}
               FROM dual) source
            ON (target.{QuoteIdentifier(Columns.TemplateKey)} = source.{QuoteIdentifier(Columns.TemplateKey)}
                AND target.{QuoteIdentifier(Columns.Culture)} = source.{QuoteIdentifier(Columns.Culture)}
                AND target.{QuoteIdentifier(Columns.Channel)} = source.{QuoteIdentifier(Columns.Channel)})
        WHEN MATCHED THEN UPDATE SET
            target.{QuoteIdentifier(Columns.Name)}        = {Parameter("Name")},
            target.{QuoteIdentifier(Columns.Description)} = {Parameter("Description")},
            target.{QuoteIdentifier(Columns.Subject)}   = {Parameter("Subject")},
            target.{QuoteIdentifier(Columns.TextBody)}  = {Parameter("TextBody")},
            target.{QuoteIdentifier(Columns.HtmlBody)}  = {Parameter("HtmlBody")},
            target.{QuoteIdentifier(Columns.IsActive)}  = {Parameter("IsActive")},
            target.{QuoteIdentifier(Columns.UpdatedAt)} = {Parameter("UpdatedAt")}
        WHEN NOT MATCHED THEN INSERT (
            {QuoteIdentifier(Columns.TemplateKey)}, {QuoteIdentifier(Columns.Culture)}, {QuoteIdentifier(Columns.Channel)},
            {QuoteIdentifier(Columns.Name)}, {QuoteIdentifier(Columns.Description)},
            {QuoteIdentifier(Columns.Subject)}, {QuoteIdentifier(Columns.TextBody)}, {QuoteIdentifier(Columns.HtmlBody)},
            {QuoteIdentifier(Columns.IsActive)}, {QuoteIdentifier(Columns.UpdatedAt)}
        ) VALUES (
            source.{QuoteIdentifier(Columns.TemplateKey)}, source.{QuoteIdentifier(Columns.Culture)}, source.{QuoteIdentifier(Columns.Channel)},
            {Parameter("Name")}, {Parameter("Description")},
            {Parameter("Subject")}, {Parameter("TextBody")}, {Parameter("HtmlBody")},
            {Parameter("IsActive")}, {Parameter("UpdatedAt")}
        )
        """;
}
