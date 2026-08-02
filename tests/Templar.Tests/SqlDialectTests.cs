using System.Reflection;
using Templar.MySql;
using Templar.PostgreSql;
using Templar.Relational;
using Xunit;

namespace Templar.Tests;

/// <summary>
/// Checks the SQL each dialect generates. The statements are reached through reflection because
/// they are protected implementation detail, which keeps these tests runnable without a live
/// database — behaviour against real servers is covered by the integration scripts in the README.
/// </summary>
public class SqlDialectTests
{
    private const string SelectColumns = "`template_key`, `culture`, `channel`, `name`, `description`, "
                                         + "`subject`, `text_body`, `html_body`, `is_active`, `updated_at`";

    private static string Sql(RelationalTemplateStore store, string member)
        => (string)typeof(RelationalTemplateStore)
            .GetProperty(member, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(store)!;

    private static MySqlTemplateStore MySql(Action<MySqlTemplateStoreOptions>? configure = null)
    {
        var options = new MySqlTemplateStoreOptions { ConnectionString = "Server=localhost;Database=test" };
        configure?.Invoke(options);
        return new MySqlTemplateStore(options);
    }

    private static PostgreSqlTemplateStore Postgres()
        => new(new PostgreSqlTemplateStoreOptions { ConnectionString = "Host=localhost;Database=test" });

    [Fact]
    public void MySql_quotes_identifiers_with_backticks()
    {
        var sql = Sql(MySql(), "SelectSql");

        Assert.Contains("FROM `notification_templates`", sql, StringComparison.Ordinal);
        Assert.Contains("`template_key` = @TemplateKey", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void MySql_qualifies_the_table_with_the_configured_schema()
    {
        var sql = Sql(MySql(o => o.Schema = "notify"), "SelectSql");

        Assert.Contains("FROM `notify`.`notification_templates`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void MySql_upserts_with_on_duplicate_key_update()
    {
        var sql = Sql(MySql(), "UpsertSql");

        Assert.Contains("ON DUPLICATE KEY UPDATE", sql, StringComparison.Ordinal);
        Assert.Contains("`subject`   = VALUES(`subject`)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgreSql_quotes_identifiers_with_double_quotes_and_upserts_on_conflict()
    {
        var store = Postgres();

        Assert.Contains("FROM \"public\".\"notification_templates\"", Sql(store, "SelectSql"), StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT", Sql(store, "UpsertSql"), StringComparison.Ordinal);
        Assert.Contains("EXCLUDED.\"html_body\"", Sql(store, "UpsertSql"), StringComparison.Ordinal);
    }

    [Fact]
    public void Select_lists_the_columns_the_reader_maps_by_ordinal()
    {
        var sql = Sql(MySql(), "SelectSql");

        Assert.StartsWith($"SELECT {SelectColumns}", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Select_all_reads_the_same_columns_with_no_filter_and_a_stable_order()
    {
        var sql = Sql(MySql(), "SelectAllSql");

        Assert.StartsWith($"SELECT {SelectColumns} FROM `notification_templates`", sql, StringComparison.Ordinal);
        Assert.EndsWith("ORDER BY `template_key`, `culture`, `channel`", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void An_identifier_containing_the_quote_character_is_escaped()
    {
        var sql = Sql(MySql(o => o.TableName = "we`ird"), "SelectSql");

        Assert.Contains("FROM `we``ird`", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_connection_string_is_rejected_early()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new MySqlTemplateStore(new MySqlTemplateStoreOptions()));

        Assert.Contains(nameof(RelationalTemplateStoreOptions.ConnectionString), exception.Message, StringComparison.Ordinal);
    }
}
