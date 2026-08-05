using Microsoft.Extensions.Options;
using Templar.Abstractions;
using Templar.Caching;
using Templar.Mongo;
using Templar.MySql;
using Templar.Oracle;
using Templar.PostgreSql;
using Templar.Scriban;
using Templar.Services;
using Templar.SqlServer;
using Xunit;

namespace Templar.Tests;

/// <summary>
/// Exercises a provider against a real server: DDL, both upsert paths, Unicode, a body far larger
/// than an inline string limit, UTC round-tripping, and a render through the query and render
/// services. Each test is skipped unless its connection string is exported —
/// see the README for the variable names.
/// </summary>
public class DatabaseIntegrationTests
{
    private const string Table = "notification_templates_it";

    public const string PostgresVariable = "TEMPLAR_POSTGRES";
    public const string MySqlVariable = "TEMPLAR_MYSQL";
    public const string SqlServerVariable = "TEMPLAR_SQLSERVER";
    public const string OracleVariable = "TEMPLAR_ORACLE";
    public const string MongoVariable = "TEMPLAR_MONGO";

    [DatabaseFact(PostgresVariable)]
    public Task PostgreSql_round_trips_a_template()
        => RoundTripAsync(new PostgreSqlTemplateStore(new PostgreSqlTemplateStoreOptions
        {
            ConnectionString = DatabaseFactAttribute.ConnectionString(PostgresVariable),
            TableName = Table,
        }));

    [DatabaseFact(MySqlVariable)]
    public Task MySql_round_trips_a_template()
        => RoundTripAsync(new MySqlTemplateStore(new MySqlTemplateStoreOptions
        {
            ConnectionString = DatabaseFactAttribute.ConnectionString(MySqlVariable),
            TableName = Table,
        }));

    [DatabaseFact(SqlServerVariable)]
    public Task SqlServer_round_trips_a_template()
        => RoundTripAsync(new SqlServerTemplateStore(new SqlServerTemplateStoreOptions
        {
            ConnectionString = DatabaseFactAttribute.ConnectionString(SqlServerVariable),
            TableName = Table,
        }));

    [DatabaseFact(OracleVariable)]
    public Task Oracle_round_trips_a_template()
        => RoundTripAsync(new OracleTemplateStore(new OracleTemplateStoreOptions
        {
            ConnectionString = DatabaseFactAttribute.ConnectionString(OracleVariable),
            TableName = Table,
        }));

    [DatabaseFact(MongoVariable)]
    public Task Mongo_round_trips_a_template()
        => RoundTripAsync(new MongoTemplateStore(new MongoTemplateStoreOptions
        {
            ConnectionString = DatabaseFactAttribute.ConnectionString(MongoVariable),
            DatabaseName = "notifications_it",
            CollectionName = Table,
        }));

    private static async Task RoundTripAsync<TStore>(TStore store)
        where TStore : ITemplateWriteStore, ITemplateSchemaInitializer
    {
        const string key = "integration-welcome";
        var updatedAt = new DateTimeOffset(2026, 7, 31, 9, 30, 15, TimeSpan.Zero);

        // A body comfortably past the 4000-byte inline limit of some engines' string binds.
        var largeHtml = "<p>Xin chào {{username}}</p>" + new string('x', 12_000);

        await store.EnsureSchemaAsync();
        await store.EnsureSchemaAsync(); // idempotent

        await CleanUpAsync(store, key);

        var english = new TemplateDefinition
        {
            TemplateKey = key,
            Culture = "en",
            Channel = TemplateChannel.Email,
            Name = "Welcome e-mail (English)",
            Description = "Sent after the address is confirmed.",
            Subject = "Welcome to XXX",
            TextBody = "Hello {{username}}, this is your email {{EMAIL}}",
            HtmlBody = "<p>Hello {{username}}</p>",
            UpdatedAtUtc = updatedAt,
        };

        var vietnamese = english with
        {
            Culture = "vi",
            Name = "Email chào mừng (Tiếng Việt)",
            Description = "Gửi sau khi xác nhận địa chỉ email.",
            Subject = "Chào mừng tới XXX",
            TextBody = "Xin chào {{username}}, đây là email của bạn {{EMAIL}}",
            HtmlBody = largeHtml,
        };

        var inApp = english with { Channel = TemplateChannel.InApp, Subject = "Welcome!", HtmlBody = null };

        var sms = english with
        {
            Channel = TemplateChannel.Other,
            Name = null,
            Description = null,
            Subject = null,
            TextBody = "XXX: code {{CODE}}",
            HtmlBody = null,
        };

        var inactive = english with { Culture = "fr", Subject = "Bienvenue", IsActive = false };

        foreach (var template in new[] { english, vietnamese, inApp, sms, inactive })
            await store.UpsertAsync(template);

        var set = await store.GetTemplateSetAsync(key);
        Assert.Equal(5, set.Count);

        var storedVietnamese = set.Single(t => t.Culture == "vi" && t.Channel == TemplateChannel.Email);
        Assert.Equal("Chào mừng tới XXX", storedVietnamese.Subject);
        Assert.Equal("Email chào mừng (Tiếng Việt)", storedVietnamese.Name);
        Assert.Equal("Gửi sau khi xác nhận địa chỉ email.", storedVietnamese.Description);
        Assert.Equal(largeHtml, storedVietnamese.HtmlBody);
        Assert.Equal(updatedAt, storedVietnamese.UpdatedAtUtc);
        Assert.True(storedVietnamese.IsActive);

        Assert.Null(set.Single(t => t.Channel == TemplateChannel.InApp).HtmlBody);
        Assert.False(set.Single(t => t.Culture == "fr").IsActive);

        var storedSms = set.Single(t => t.Channel == TemplateChannel.Other);
        Assert.Equal("XXX: code {{CODE}}", storedSms.TextBody);
        Assert.Null(storedSms.Name);
        Assert.Null(storedSms.Description);
        Assert.Null(storedSms.Subject);

        // Second upsert of the same natural key must update, not duplicate.
        await store.UpsertAsync(english with { Subject = "Welcome back" });
        var updated = await store.GetTemplateSetAsync(key);
        Assert.Equal(5, updated.Count);

        var storedEnglish = updated.Single(t => t.Culture == "en" && t.Channel == TemplateChannel.Email);
        Assert.Equal("Welcome back", storedEnglish.Subject);

        Assert.Contains(key, await store.ListTemplateKeysAsync());

        var all = await store.GetAllTemplatesAsync();
        Assert.Equal(5, all.Count(t => t.TemplateKey == key));

        // The store plugs into the services unchanged: vi-VN resolves to the vi row.
        var options = Options.Create(new TemplateOptions { DefaultCulture = "en" });
        var queries = new TemplateQueryService(store, new MemoryTemplateCache(options), options);
        var render = new TemplateRenderService(
            queries,
            new ScribanTemplateCompiler(options.Value),
            new ScribanTemplateRenderer(options.Value),
            options);

        var rendered = await render.RenderAsync(new TemplateRenderRequest
        {
            TemplateKey = key,
            Culture = "vi-VN",
            Values = TemplateValues.Create().Set("username", "Nguyễn").Set("email", "huy@example.com"),
            Parts = TemplateParts.Subject | TemplateParts.Text,
        });

        Assert.Equal("vi", rendered.Culture);
        Assert.Equal("Xin chào Nguyễn, đây là email của bạn huy@example.com", rendered.Text);

        Assert.True(await store.DeleteAsync(key, "vi", TemplateChannel.Email));
        Assert.False(await store.DeleteAsync(key, "vi", TemplateChannel.Email));

        await CleanUpAsync(store, key);
        Assert.Empty(await store.GetTemplateSetAsync(key));
    }

    private static async Task CleanUpAsync(ITemplateWriteStore store, string key)
    {
        foreach (var template in await store.GetTemplateSetAsync(key))
            await store.DeleteAsync(template.TemplateKey, template.Culture, template.Channel);
    }
}
