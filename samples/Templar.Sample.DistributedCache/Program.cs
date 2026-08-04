using System.Text.Json;
using System.Text.Json.Serialization;
using Templar;
using Templar.Abstractions;
using Templar.Stores;

// DistributedTemplateCache over any IDistributedCache. With Templates:Redis set, two instances share
// one cache: render on one and the other's next read is a cache hit, and clearing on either makes the
// other's next read a store miss within DistributedTemplateCache.GenerationRefresh (2s). Watch both
// through /api/cache/stats.
//
//   make up SAMPLE=DistributedCache
//   dotnet run --Templates:Redis=localhost:6379 --urls http://localhost:5002
//   dotnet run --Templates:Redis=localhost:6379 --urls http://localhost:5003
//
// The store here is per-process and in-memory, so it is the *cache* that is shared, not the data: a
// template saved on one node is not readable from the other. Pair this with a database sample for
// that. Everything this sample does is in this one file, top to bottom.

var builder = WebApplication.CreateBuilder(args);
var settings = builder.Configuration.GetSection("Templates");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.WriteIndented = true;
});

builder.Services.AddOpenApi();

// The store the cache sits in front of. Registered by hand rather than through UseInMemoryStore so
// the counter can wrap it; singleton because this store *is* the data.
var store = new CountingTemplateStore(new InMemoryTemplateStore());

// Redis when configured, otherwise an in-process IDistributedCache so the sample runs with no server
// — that one is not actually shared between instances, which the swagger title says out loud.
var redis = settings["Redis"];
if (string.IsNullOrWhiteSpace(redis))
    builder.Services.AddDistributedMemoryCache();
else
    builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redis);

builder.Services
    .AddTemplar(options =>
    {
        options.DefaultCulture = settings["DefaultCulture"] ?? "en";
        options.EnableCultureFallback = settings.GetValue("EnableCultureFallback", true);
        options.EnableCache = settings.GetValue("EnableCache", true);
        options.CacheDuration = TimeSpan.FromSeconds(settings.GetValue("CacheSeconds", 30));
        options.CacheKeyPrefix = settings["CacheKeyPrefix"] ?? "templar:";
    })
    .UseDistributedCache();

builder.Services.AddSingleton<ITemplateStore>(store);
builder.Services.AddSingleton<ITemplateWriteStore>(store);

var app = builder.Build();

var which = string.IsNullOrWhiteSpace(redis) ? "in-process, not shared" : $"Redis {redis}";

app.MapOpenApi();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", $"Templar · distributed cache ({which})"));
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

// Template failures are ordinary request errors, not server faults.
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (TemplateNotFoundException exception)
    {
        await Results
            .Problem(exception.Message, statusCode: StatusCodes.Status404NotFound, title: "Template not found")
            .ExecuteAsync(context);
    }
    catch (TemplateCompilationException exception)
    {
        await Results
            .Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Template does not parse")
            .ExecuteAsync(context);
    }
    catch (TemplateRenderException exception)
    {
        await Results
            .Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Template could not be rendered")
            .ExecuteAsync(context);
    }
});

// The in-memory store has no schema to create. The three services are scoped, so startup work needs
// its own scope — outside a request there is none.
if (settings.GetValue("SeedOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();

    var seed = SeedTemplates.All;

    // Writing through the command service also evicts the read cache for those keys.
    await scope.ServiceProvider.GetRequiredService<ITemplateCommandService>().SaveAsync(seed);

    // Seeding reads the store too; the counter should start at zero for whoever is watching it.
    store.Reset();

    app.Logger.LogInformation("Seeded {Count} templates.", seed.Length);
}

// ---------------------------------------------------------------- read

app.MapGet("/api/templates", async (ITemplateQueryService queries, CancellationToken ct)
        => await queries.ListAsync(ct))
    .WithTags("Templates")
    .WithSummary("Every stored template: all keys, cultures and channels, including inactive rows.");

app.MapGet("/api/keys", async (ITemplateQueryService queries, CancellationToken ct)
        => await queries.ListKeysAsync(ct))
    .WithTags("Templates")
    .WithSummary("Just the template keys, when the whole table is more than the caller needs.");

app.MapGet("/api/channels", (ITemplateChannelService channels) => channels.GetAll())
    .WithTags("Templates")
    .WithSummary("Every channel as a { value, label } pair, for filling a channel picker.");

app.MapGet("/api/templates/{key}", async (
        string key,
        ITemplateQueryService queries,
        CancellationToken ct) =>
    {
        var variants = await queries.GetVariantsAsync(key, ct);
        return variants.Count == 0 ? Results.NotFound() : Results.Ok(variants);
    })
    .WithTags("Templates")
    .WithSummary("Every culture and channel stored under one key, including inactive rows.");

app.MapGet("/api/templates/{key}/{culture}", async (
        string key,
        string culture,
        ITemplateQueryService queries,
        CancellationToken ct,
        TemplateChannel channel = TemplateChannel.Email) =>
    {
        // FindAsync matches the culture exactly and returns inactive rows — what an editor needs.
        var match = await queries.FindAsync(key, culture, channel, ct);
        return match is null ? Results.NotFound() : Results.Ok(match);
    })
    .WithTags("Templates")
    .WithSummary("One exact variant. No culture fallback: vi-VN matches only a row stored as vi-VN.");

app.MapGet("/api/resolve/{key}", async (
        string key,
        string? culture,
        ITemplateQueryService queries,
        CancellationToken ct,
        TemplateChannel channel = TemplateChannel.Email) =>
    {
        // ResolveAsync is the render path: active rows only, with culture fallback applied.
        var resolved = await queries.ResolveAsync(key, culture, channel, ct);
        return resolved is null ? Results.NotFound() : Results.Ok(resolved);
    })
    .WithTags("Templates")
    .WithSummary("Which variant a render would pick, unrendered. Try culture=vi-VN, then culture=ja.");

// ---------------------------------------------------------------- write

const string NoContent = "At least one of subject, textBody or htmlBody must be set.";

static IResult Invalid(string detail)
    => Results.Problem(detail, statusCode: StatusCodes.Status400BadRequest, title: "Invalid template");

app.MapPost("/api/templates", async (
        WriteRequest request,
        ITemplateCommandService commands,
        CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(request.TemplateKey) || string.IsNullOrWhiteSpace(request.Culture))
            return Invalid("templateKey and culture are required.");
        if (!request.HasContent) return Invalid(NoContent);

        var channel = request.Channel ?? TemplateChannel.Email;
        var template = request.ToDefinition(request.TemplateKey, request.Culture, channel);

        await commands.SaveAsync(template, ct);

        return Results.Created($"/api/templates/{template.TemplateKey}/{template.Culture}?channel={channel}", template);
    })
    .WithTags("Templates")
    .WithSummary("Create a template, or replace the row with the same key, culture and channel.");

app.MapPut("/api/templates/{key}/{culture}", async (
        string key,
        string culture,
        WriteRequest request,
        ITemplateCommandService commands,
        CancellationToken ct,
        TemplateChannel channel = TemplateChannel.Email) =>
    {
        if (!request.HasContent) return Invalid(NoContent);

        var template = request.ToDefinition(key, culture, channel);

        await commands.SaveAsync(template, ct);

        return Results.Ok(template);
    })
    .WithTags("Templates")
    .WithSummary("Update one variant.");

app.MapDelete("/api/templates/{key}/{culture}", async (
        string key,
        string culture,
        ITemplateCommandService commands,
        CancellationToken ct,
        TemplateChannel channel = TemplateChannel.Email) =>
    {
        var deleted = await commands.DeleteAsync(key, culture, channel, ct);
        return deleted ? Results.NoContent() : Results.NotFound();
    })
    .WithTags("Templates")
    .WithSummary("Delete one variant.");

// ---------------------------------------------------------------- render

app.MapPost("/api/render", async (
        RenderRequest request,
        ITemplateRenderService renderer,
        CancellationToken ct)
        => await renderer.RenderAsync(request.ToRenderRequest(), ct))
    .WithTags("Render")
    .WithSummary("Render subject, text and HTML. A missing value is a 400 naming the placeholders.");

app.MapPost("/api/render/html", async (
        RenderRequest request,
        ITemplateRenderService renderer,
        CancellationToken ct) =>
    {
        var rendered = await renderer.RenderAsync(request.ToRenderRequest(), ct);

        return rendered.HasHtml
            ? Results.Content(rendered.Html!, "text/html; charset=utf-8")
            : Results.Content(
                string.Join("\n\n", new[] { rendered.Subject, rendered.Text }.Where(p => !string.IsNullOrEmpty(p))),
                "text/plain; charset=utf-8");
    })
    .WithTags("Render")
    .WithSummary("The same render, returned as text/html so a browser shows the finished e-mail.");

app.MapPost("/api/cache/clear", async (
        ITemplateCommandService commands,
        string? key,
        CancellationToken ct) =>
    {
        await commands.InvalidateAsync(key, ct);
        return Results.Ok(new { cleared = key ?? "(every key)" });
    })
    .WithTags("Render")
    .WithSummary("Drop cached templates so the next read hits the store. Omit key to clear all.");


app.MapGet("/api/cache/stats", () => new { storeReads = store.Reads })
    .WithTags("Cache")
    .WithSummary("Reads the cache could not serve. Render the same template twice — it should not move.");
app.Run();

// ---------------------------------------------------------------- request bodies

/// <summary>Body of a create or update call. Route values win over ids sent in the body.</summary>
internal sealed record WriteRequest(
    string? TemplateKey = null,
    string? Culture = null,
    TemplateChannel? Channel = null,
    string? Name = null,
    string? Description = null,
    string? Subject = null,
    string? TextBody = null,
    string? HtmlBody = null,
    bool? IsActive = null)
{
    public bool HasContent =>
        !string.IsNullOrWhiteSpace(Subject)
        || !string.IsNullOrWhiteSpace(TextBody)
        || !string.IsNullOrWhiteSpace(HtmlBody);

    public TemplateDefinition ToDefinition(string key, string culture, TemplateChannel channel)
        => new()
        {
            TemplateKey = key,
            Culture = culture,
            Channel = channel,
            Name = Name,
            Description = Description,
            Subject = Subject,
            TextBody = TextBody,
            HtmlBody = HtmlBody,
            IsActive = IsActive ?? true,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
}

/// <summary>
/// Body of the render calls. <c>values</c> is free-form JSON:
/// <c>{ "username": "Huy", "MINUTES": 15, "EXPIRES_AT": "2026-08-01T09:30:00Z" }</c>.
/// </summary>
internal sealed record RenderRequest(
    string TemplateKey,
    string? Culture = null,
    TemplateChannel? Channel = null,
    TemplateParts? Parts = null,
    Dictionary<string, JsonElement>? Values = null)
{
    public TemplateRenderRequest ToRenderRequest() => new()
    {
        TemplateKey = TemplateKey,
        Culture = Culture,
        Channel = Channel ?? TemplateChannel.Email,
        Parts = Parts ?? TemplateParts.All,
        Values = Values is null
            ? TemplateValues.Empty
            : TemplateValues.From(Values.Select(v => new KeyValuePair<string, object?>(v.Key, Unwrap(v.Value)))),
    };

    /// <summary>
    /// Format specifiers such as <c>{{ MINUTES | format 'N0' }}</c> and <c>{{ EXPIRES_AT | format 'g' }}</c> only apply to real
    /// numbers and dates, and JSON already carries the type — so <c>15</c> arrives as a number while
    /// a verification code sent as <c>"007193"</c> keeps its leading zero.
    /// </summary>
    private static object? Unwrap(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.TryGetDateTimeOffset(out var timestamp) ? timestamp : value.GetString(),
        JsonValueKind.Number => value.TryGetInt64(out var integer) ? integer : value.GetDecimal(),
        JsonValueKind.True or JsonValueKind.False => value.GetBoolean(),
        JsonValueKind.Array => value.EnumerateArray().Select(Unwrap).ToList(),
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(p => p.Name, p => Unwrap(p.Value)),
        JsonValueKind.Null => null,
        _ => value.ToString(),
    };
}

// ---------------------------------------------------------------- the counter

/// <summary>
/// Wraps the store and counts the reads that reach it, which is the whole point of this sample: with
/// a warm cache the count stays put, and a save, a delete or a clear makes the next read a miss.
/// </summary>
internal sealed class CountingTemplateStore(ITemplateWriteStore inner) : ITemplateWriteStore
{
    private long _reads;

    public long Reads => Interlocked.Read(ref _reads);

    public void Reset() => Interlocked.Exchange(ref _reads, 0);

    public Task<IReadOnlyList<TemplateDefinition>> GetTemplateSetAsync(
        string templateKey,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _reads);
        return inner.GetTemplateSetAsync(templateKey, cancellationToken);
    }

    public Task<IReadOnlyList<string>> ListTemplateKeysAsync(CancellationToken cancellationToken = default)
        => inner.ListTemplateKeysAsync(cancellationToken);

    public Task<IReadOnlyList<TemplateDefinition>> GetAllTemplatesAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _reads);
        return inner.GetAllTemplatesAsync(cancellationToken);
    }

    public Task UpsertAsync(TemplateDefinition template, CancellationToken cancellationToken = default)
        => inner.UpsertAsync(template, cancellationToken);

    public Task<bool> DeleteAsync(
        string templateKey,
        string culture,
        TemplateChannel channel,
        CancellationToken cancellationToken = default)
        => inner.DeleteAsync(templateKey, culture, channel, cancellationToken);
}

// ---------------------------------------------------------------- seed

/// <summary>
/// What the sample starts with: <c>welcome-user</c> in English and Vietnamese as an e-mail and as an
/// in-app notification, and <c>reset-password</c> in both languages plus an SMS on the
/// <c>Other</c> channel. Enough to watch <c>vi-VN</c> fall back to <c>vi</c> and <c>ja</c> fall back
/// to the default culture.
/// </summary>
internal static class SeedTemplates
{
    private static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static TemplateDefinition[] All =>
    [
        new()
        {
            TemplateKey = "welcome-user",
            Culture = "en",
            Channel = TemplateChannel.Email,
            Name = "Welcome e-mail (English)",
            Description = "Sent once, immediately after a user confirms their address.",
            Subject = "Welcome to XXX",
            TextBody = "Hello {{username}}, welcome to XXX, this is your email {{EMAIL}}",
            HtmlBody = """
                <html>
                  <body style="font-family: sans-serif">
                    <h1>Welcome to XXX</h1>
                    <p>Hello <strong>{{username}}</strong>, welcome to XXX.</p>
                    <p>This is your email: <a href="mailto:{{EMAIL}}">{{EMAIL}}</a></p>
                    <p>Registered on {{ DATE | format 'D' }}.</p>
                  </body>
                </html>
                """,
            UpdatedAtUtc = SeededAt,
        },
        new()
        {
            TemplateKey = "welcome-user",
            Culture = "vi",
            Channel = TemplateChannel.Email,
            Name = "Email chào mừng (Tiếng Việt)",
            Description = "Gửi một lần, ngay sau khi người dùng xác nhận địa chỉ email.",
            Subject = "Chào mừng tới XXX",
            TextBody = "Xin chào {{username}}, chào mừng tới XXX, đây là email của bạn {{EMAIL}}",
            HtmlBody = """
                <html>
                  <body style="font-family: sans-serif">
                    <h1>Chào mừng tới XXX</h1>
                    <p>Xin chào <strong>{{username}}</strong>, chào mừng tới XXX.</p>
                    <p>Đây là email của bạn: <a href="mailto:{{EMAIL}}">{{EMAIL}}</a></p>
                    <p>Đăng ký ngày {{ DATE | format 'D' }}.</p>
                  </body>
                </html>
                """,
            UpdatedAtUtc = SeededAt,
        },
        new()
        {
            TemplateKey = "welcome-user",
            Culture = "en",
            Channel = TemplateChannel.InApp,
            Name = "Welcome notification (English)",
            Description = "Shown in the notification centre on first sign-in.",
            Subject = "Welcome!",
            TextBody = "Hi {{username}}, your account is ready.",
            UpdatedAtUtc = SeededAt,
        },
        new()
        {
            TemplateKey = "welcome-user",
            Culture = "vi",
            Channel = TemplateChannel.InApp,
            Name = "Thông báo chào mừng (Tiếng Việt)",
            Description = "Hiển thị trong trung tâm thông báo khi đăng nhập lần đầu.",
            Subject = "Chào mừng!",
            TextBody = "Chào {{username}}, tài khoản của bạn đã sẵn sàng.",
            UpdatedAtUtc = SeededAt,
        },
        new()
        {
            TemplateKey = "reset-password",
            Culture = "en",
            Channel = TemplateChannel.Email,
            Name = "Password reset e-mail (English)",
            Description = "Carries a one-time code; expires with the code.",
            Subject = "Reset your password, {{username}}",
            TextBody = "Use the code {{CODE}} before {{ EXPIRES_AT | format 'g' }}. It is valid for {{MINUTES}} minutes.",
            HtmlBody = "<p>Use the code <code>{{CODE}}</code> before {{ EXPIRES_AT | format 'g' }} ({{MINUTES}} minutes).</p>",
            UpdatedAtUtc = SeededAt,
        },
        new()
        {
            TemplateKey = "reset-password",
            Culture = "vi",
            Channel = TemplateChannel.Email,
            Name = "Email đặt lại mật khẩu (Tiếng Việt)",
            Description = "Chứa mã dùng một lần; hết hạn cùng với mã.",
            Subject = "Đặt lại mật khẩu, {{username}}",
            TextBody = "Dùng mã {{CODE}} trước {{ EXPIRES_AT | format 'g' }}. Mã có hiệu lực {{MINUTES}} phút.",
            HtmlBody = "<p>Dùng mã <code>{{CODE}}</code> trước {{ EXPIRES_AT | format 'g' }} ({{MINUTES}} phút).</p>",
            UpdatedAtUtc = SeededAt,
        },
        new()
        {
            // The Other channel covers everything Templar does not model explicitly — here an SMS,
            // which has no subject and no HTML.
            TemplateKey = "reset-password",
            Culture = "vi",
            Channel = TemplateChannel.Other,
            Name = "SMS đặt lại mật khẩu (Tiếng Việt)",
            Description = "Kênh Other: nội dung SMS, chỉ có text và không có tiêu đề.",
            TextBody = "XXX: ma xac thuc {{CODE}}, het han sau {{MINUTES}} phut.",
            UpdatedAtUtc = SeededAt,
        },
    ];
}
