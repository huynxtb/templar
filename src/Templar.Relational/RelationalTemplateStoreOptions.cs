namespace Templar.Relational;

/// <summary>Connection and naming settings shared by every SQL provider.</summary>
public class RelationalTemplateStoreOptions
{
    /// <summary>ADO.NET connection string for the target database.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Table holding the templates. Defaults to <c>notification_templates</c>.</summary>
    public string TableName { get; set; } = "notification_templates";

    /// <summary>
    /// Schema (SQL Server, PostgreSQL) or database/owner (MySQL, Oracle) qualifying the table.
    /// Leave <see langword="null"/> to use the connection's default.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>Command timeout in seconds. Leave <see langword="null"/> for the provider default.</summary>
    public int? CommandTimeoutSeconds { get; set; }

    /// <summary>Throws when the options cannot be used.</summary>
    public virtual void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new InvalidOperationException($"{GetType().Name}.{nameof(ConnectionString)} must be set.");
        if (string.IsNullOrWhiteSpace(TableName))
            throw new InvalidOperationException($"{GetType().Name}.{nameof(TableName)} must be set.");
        if (CommandTimeoutSeconds is < 0)
            throw new InvalidOperationException($"{GetType().Name}.{nameof(CommandTimeoutSeconds)} cannot be negative.");
    }
}
