using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Templar.Abstractions;

namespace Templar.Relational;

/// <summary>
/// ADO.NET implementation of <see cref="ITemplateWriteStore"/> shared by the MySQL, SQL Server,
/// PostgreSQL and Oracle providers. Subclasses supply the connection object and the handful of
/// statements whose syntax differs between engines.
/// </summary>
/// <remarks>
/// Plain ADO.NET is used on purpose: it keeps the packages dependency-free apart from the database
/// driver itself, and it gives each engine full control over identifier quoting and parameter
/// binding (Oracle in particular needs named binding switched on explicitly).
/// </remarks>
public abstract class RelationalTemplateStore(RelationalTemplateStoreOptions options, ILogger? logger = null)
    : ITemplateWriteStore, ITemplateSchemaInitializer
{
    /// <summary>Column names, fixed so the shared read path can map by ordinal.</summary>
    protected static class Columns
    {
        public const string TemplateKey = "template_key";
        public const string Culture = "culture";
        public const string Channel = "channel";
        public const string Name = "name";
        public const string Description = "description";
        public const string Subject = "subject";
        public const string TextBody = "text_body";
        public const string HtmlBody = "html_body";
        public const string IsActive = "is_active";
        public const string UpdatedAt = "updated_at";
    }

    private string? _selectSql;
    private string? _selectAllSql;
    private string? _listKeysSql;
    private string? _deleteSql;
    private string? _upsertSql;

    protected RelationalTemplateStoreOptions Options { get; } = Validated(options);

    protected ILogger Logger { get; } = logger ?? NullLogger.Instance;

    private static RelationalTemplateStoreOptions Validated(RelationalTemplateStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return options;
    }

    /// <summary>Prefix used for bind parameters: <c>@</c> for most engines, <c>:</c> for Oracle.</summary>
    protected abstract string ParameterPrefix { get; }

    /// <summary>Creates a closed connection to the target database.</summary>
    protected abstract DbConnection CreateConnection();

    /// <summary>Quotes an identifier for this engine, e.g. <c>[name]</c>, <c>`name`</c> or <c>"name"</c>.</summary>
    protected abstract string QuoteIdentifier(string identifier);

    /// <summary>DDL statements that create the table and its constraints if missing.</summary>
    protected abstract IReadOnlyList<string> GetSchemaStatements();

    /// <summary>Engine-specific insert-or-update statement.</summary>
    protected abstract string BuildUpsertSql();

    /// <summary>Hook for provider-specific command setup. Oracle uses it to enable named binding.</summary>
    protected virtual void PrepareCommand(DbCommand command) { }

    /// <summary>The table name, quoted and qualified with <see cref="RelationalTemplateStoreOptions.Schema"/>.</summary>
    protected string Table => string.IsNullOrWhiteSpace(Options.Schema)
        ? QuoteIdentifier(Options.TableName)
        : $"{QuoteIdentifier(Options.Schema)}.{QuoteIdentifier(Options.TableName)}";

    /// <summary>Writes <c>@Name</c> / <c>:Name</c> for a bind parameter.</summary>
    protected string Parameter(string name) => ParameterPrefix + name;

    /// <summary>Column list used by every read, in the order <see cref="MapTemplate"/> expects.</summary>
    protected string SelectColumns =>
        string.Join(", ", new[]
        {
            QuoteIdentifier(Columns.TemplateKey),
            QuoteIdentifier(Columns.Culture),
            QuoteIdentifier(Columns.Channel),
            QuoteIdentifier(Columns.Name),
            QuoteIdentifier(Columns.Description),
            QuoteIdentifier(Columns.Subject),
            QuoteIdentifier(Columns.TextBody),
            QuoteIdentifier(Columns.HtmlBody),
            QuoteIdentifier(Columns.IsActive),
            QuoteIdentifier(Columns.UpdatedAt),
        });

    protected virtual string SelectSql => _selectSql ??=
        $"SELECT {SelectColumns} FROM {Table} WHERE {QuoteIdentifier(Columns.TemplateKey)} = {Parameter("TemplateKey")}";

    protected virtual string SelectAllSql => _selectAllSql ??=
        $"SELECT {SelectColumns} FROM {Table} ORDER BY {QuoteIdentifier(Columns.TemplateKey)}, "
        + $"{QuoteIdentifier(Columns.Culture)}, {QuoteIdentifier(Columns.Channel)}";

    protected virtual string ListKeysSql => _listKeysSql ??=
        $"SELECT DISTINCT {QuoteIdentifier(Columns.TemplateKey)} FROM {Table} ORDER BY {QuoteIdentifier(Columns.TemplateKey)}";

    protected virtual string DeleteSql => _deleteSql ??=
        $"DELETE FROM {Table} WHERE {QuoteIdentifier(Columns.TemplateKey)} = {Parameter("TemplateKey")} "
        + $"AND {QuoteIdentifier(Columns.Culture)} = {Parameter("Culture")} "
        + $"AND {QuoteIdentifier(Columns.Channel)} = {Parameter("Channel")}";

    protected string UpsertSql => _upsertSql ??= BuildUpsertSql();

    public virtual async Task<IReadOnlyList<TemplateDefinition>> GetTemplateSetAsync(
        string templateKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = CreateCommand(connection, SelectSql);
        AddParameter(command, "TemplateKey", templateKey);

        var results = new List<TemplateDefinition>(4);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(MapTemplate(reader));

        return results;
    }

    public virtual async Task<IReadOnlyList<TemplateDefinition>> GetAllTemplatesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = CreateCommand(connection, SelectAllSql);

        var results = new List<TemplateDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(MapTemplate(reader));

        return results;
    }

    public virtual async Task<IReadOnlyList<string>> ListTemplateKeysAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = CreateCommand(connection, ListKeysSql);

        var keys = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            keys.Add(reader.GetString(0));

        return keys;
    }

    public virtual async Task UpsertAsync(TemplateDefinition template, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(template.TemplateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(template.Culture);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = CreateCommand(connection, UpsertSql);
        AddTemplateParameters(command, template);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<bool> DeleteAsync(
        string templateKey,
        string culture,
        TemplateChannel channel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = CreateCommand(connection, DeleteSql);
        AddParameter(command, "TemplateKey", templateKey);
        AddParameter(command, "Culture", culture);
        AddParameter(command, "Channel", channel.ToString());

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public virtual async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var statement in GetSchemaStatements())
        {
            await using var command = CreateCommand(connection, statement);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        Logger.LogInformation("Template table {Table} is ready.", Table);
    }

    /// <summary>Opens a connection using <see cref="CreateConnection"/>.</summary>
    protected async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = CreateConnection();
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Creates a command carrying the configured timeout and provider tweaks.</summary>
    protected DbCommand CreateCommand(DbConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        if (Options.CommandTimeoutSeconds is { } timeout) command.CommandTimeout = timeout;

        PrepareCommand(command);
        return command;
    }

    /// <summary>Adds a bind parameter, translating <see langword="null"/> to <see cref="DBNull"/>.</summary>
    protected DbParameter AddParameter(DbCommand command, string name, object? value, DbType? dbType = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        if (dbType is { } type) parameter.DbType = type;

        command.Parameters.Add(parameter);
        return parameter;
    }

    /// <summary>
    /// Binds every column of <paramref name="template"/>. Parameters are added in the order the
    /// generated SQL uses them, which also keeps positional binders happy.
    /// </summary>
    protected virtual void AddTemplateParameters(DbCommand command, TemplateDefinition template)
    {
        AddParameter(command, "TemplateKey", template.TemplateKey, DbType.String);
        AddParameter(command, "Culture", template.Culture, DbType.String);
        AddParameter(command, "Channel", template.Channel.ToString(), DbType.String);
        AddParameter(command, "Name", template.Name, DbType.String);
        AddParameter(command, "Description", template.Description, DbType.String);
        AddParameter(command, "Subject", template.Subject, DbType.String);
        AddParameter(command, "TextBody", template.TextBody, DbType.String);
        AddParameter(command, "HtmlBody", template.HtmlBody, DbType.String);
        AddParameter(command, "IsActive", ConvertIsActive(template.IsActive));
        AddParameter(command, "UpdatedAt", template.UpdatedAtUtc.UtcDateTime, TimestampDbType);
    }

    /// <summary>
    /// <see cref="DbType"/> applied to the <c>updated_at</c> parameter. Providers that infer a
    /// better type from the value itself — Npgsql maps a UTC <see cref="DateTime"/> to
    /// <c>timestamptz</c> — return <see langword="null"/> to leave it unset.
    /// </summary>
    protected virtual DbType? TimestampDbType => DbType.DateTime2;

    /// <summary>
    /// Converts the flag to the type the engine's column uses. Engines without a boolean type
    /// override this to send 0/1.
    /// </summary>
    protected virtual object ConvertIsActive(bool isActive) => isActive;

    /// <summary>Maps the current row of a reader produced from <see cref="SelectColumns"/>.</summary>
    protected virtual TemplateDefinition MapTemplate(DbDataReader reader)
    {
        var templateKey = reader.GetString(0);
        var culture = reader.GetString(1);
        var channelName = reader.GetString(2);

        if (!Enum.TryParse<TemplateChannel>(channelName, ignoreCase: true, out var channel))
        {
            throw new InvalidOperationException(
                $"Template '{templateKey}' ({culture}) has an unknown channel '{channelName}' in {Table}. "
                + $"Expected one of: {string.Join(", ", Enum.GetNames<TemplateChannel>())}.");
        }

        return new TemplateDefinition
        {
            TemplateKey = templateKey,
            Culture = culture,
            Channel = channel,
            Name = GetNullableString(reader, 3),
            Description = GetNullableString(reader, 4),
            Subject = GetNullableString(reader, 5),
            TextBody = GetNullableString(reader, 6),
            HtmlBody = GetNullableString(reader, 7),
            IsActive = GetBoolean(reader, 8),
            UpdatedAtUtc = GetTimestamp(reader, 9),
        };
    }

    private static string? GetNullableString(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    /// <summary>Reads a flag stored as a boolean, a bit, or a numeric 0/1 depending on the engine.</summary>
    private static bool GetBoolean(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return false;

        var value = reader.GetValue(ordinal);
        return value switch
        {
            bool flag => flag,
            byte b => b != 0,
            short s => s != 0,
            int i => i != 0,
            long l => l != 0,
            decimal d => d != 0m,
            string s => bool.TryParse(s, out var parsed) ? parsed : s is "1" or "Y" or "y",
            _ => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// Reads a timestamp as UTC. Columns are written as UTC, so a value that comes back without
    /// offset information is tagged rather than converted.
    /// </summary>
    private static DateTimeOffset GetTimestamp(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return default;

        return reader.GetValue(ordinal) switch
        {
            DateTimeOffset offset => offset.ToUniversalTime(),
            DateTime { Kind: DateTimeKind.Local } local => new DateTimeOffset(local.ToUniversalTime()),
            DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            var other => new DateTimeOffset(
                DateTime.SpecifyKind(Convert.ToDateTime(other, CultureInfo.InvariantCulture), DateTimeKind.Utc)),
        };
    }
}
