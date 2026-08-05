using System.Text.Json;
using System.Text.Json.Serialization;
using Templar;
using Templar.Abstractions;

// Templar with no database at all: templates live in the process and disappear with it. The smallest
// way to see the library work. Everything this sample does is in this one file, top to bottom:
// configuration, registration, seeding, then the HTTP API. Browse and call it at /swagger.
//
//   dotnet run                         → http://localhost:5000/swagger

var builder = WebApplication.CreateBuilder(args);
var settings = builder.Configuration.GetSection("Templates");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.WriteIndented = true;
});

builder.Services.AddOpenApi();

// AddTemplar registers the three services, the Scriban engine and the cache; UseInMemoryStore
// supplies the store behind them. That store is a singleton, unlike the database ones — it *is* the
// data, so a scoped instance would start empty on every request.
builder.Services
    .AddTemplar(options =>
    {
        options.DefaultCulture = settings["DefaultCulture"] ?? "en";
        options.EnableCultureFallback = settings.GetValue("EnableCultureFallback", true);
        options.EnableCache = settings.GetValue("EnableCache", true);
        options.CacheDuration = TimeSpan.FromSeconds(settings.GetValue("CacheSeconds", 300));

        // A stored body can loop, so the iterations are capped: one bad row must not be able to
        // hang a request thread.
        options.LoopLimit = settings.GetValue("LoopLimit", 1000);

        // Functions registered here are callable from every stored body: {{ order.total | vnd }}.
        // They run in the template's culture, so this one delegate groups digits as 1.250.000 for the
        // Vietnamese row and 1,250,000 for the English one. Shared across renders, so a function takes
        // what it needs as arguments rather than closing over per-request state.
        options.Functions["vnd"] = (decimal amount) => $"{amount:N0} ₫";
    })
    .UseInMemoryStore();

var app = builder.Build();

app.MapOpenApi();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Templar · in-memory store"));
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
/// Body of the render calls. <c>values</c> is free-form JSON —
/// <c>{ "username": "Huy", "MINUTES": 15, "EXPIRES_AT": "2026-08-01T09:30:00Z" }</c> — and nested
/// shapes are kept: <c>{ "customer": { "isVip": true }, "order": { "lines": [ { "name": "…" } ] } }</c>
/// is what <c>{{ if customer.is_vip }}</c> and <c>{{ for line in order.lines }}</c> read.
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
    /// Format specifiers such as <c>{{ MINUTES | format 'N0' }}</c> only apply to real numbers and
    /// dates, and JSON already carries the type — so <c>15</c> arrives as a number while a
    /// verification code sent as <c>"007193"</c> keeps its leading zero. Arrays and objects become
    /// lists and dictionaries rather than strings; that is what makes them loopable and their members
    /// reachable.
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

// ---------------------------------------------------------------- seed

/// <summary>
/// What the sample starts with. <c>welcome-user</c> and <c>reset-password</c> are the flat ones —
/// English and Vietnamese, e-mail and in-app, plus an SMS on the <c>Other</c> channel — and are
/// enough to watch <c>vi-VN</c> fall back to <c>vi</c> and <c>ja</c> fall back to the default
/// culture. <c>order-confirmation</c> is where the engine earns its keep: a <c>for</c> over the order
/// lines rendered as a table, <c>if</c>/<c>else</c> on VIP status, <c>case</c>/<c>when</c> on the
/// order status, and the <c>vnd</c> function registered in <c>AddTemplar</c>.
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
        new()
        {
            // Everything the engine can do, in one row: case/when on the status, if/else on VIP,
            // a for over the lines as a table, and the vnd function AddTemplar registered.
            TemplateKey = "order-confirmation",
            Culture = "en",
            Channel = TemplateChannel.Email,
            Name = "Order confirmation (English)",
            Description = "Sent when payment clears. Loops the order lines and totals them.",
            Subject = "Order {{ order.reference }} confirmed",
            TextBody = """
                Hello {{ customer.first_name }},

                {{ case order.status ~}}
                {{ when 'paid' ~}}
                Payment received — we are packing your order.
                {{ when 'pending' ~}}
                We are still waiting for your payment.
                {{ else ~}}
                Order status: {{ order.status }}
                {{ end ~}}

                {{ for line in order.lines ~}}
                  {{ line.quantity }} x {{ line.name }} — {{ line.total | format 'N0' }}
                {{ else ~}}
                  (this order has no lines)
                {{ end ~}}

                Total: {{ order.total | vnd }}
                Placed on {{ order.placed_at | format 'D' }}.
                """,
            HtmlBody = """
                <html>
                  <body style="font-family: sans-serif">
                    <h1>Order {{ order.reference }} confirmed</h1>
                    <p>Hello <strong>{{ customer.first_name }}</strong>,</p>
                    {{~ if customer.is_vip ~}}
                    <p style="color: #a67c00">As a VIP member your delivery is free.</p>
                    {{~ else ~}}
                    <p>Delivery is charged at the standard rate.</p>
                    {{~ end ~}}
                    {{~ case order.status ~}}
                    {{~ when 'paid' ~}}
                    <p style="color: #0a7d28">Payment received — we are packing your order.</p>
                    {{~ when 'pending' ~}}
                    <p style="color: #a67c00">We are still waiting for your payment.</p>
                    {{~ else ~}}
                    <p>Order status: {{ order.status }}</p>
                    {{~ end ~}}
                    <table cellpadding="6" style="border-collapse: collapse">
                      <tr><th align="left">Item</th><th align="right">Qty</th><th align="right">Total</th></tr>
                      {{~ for line in order.lines ~}}
                      <tr style="background: {{ if for.even }}#fff{{ else }}#f6f6f6{{ end }}">
                        <td>{{ line.name }}</td>
                        <td align="right">{{ line.quantity }}</td>
                        <td align="right">{{ line.total | format 'N0' }}</td>
                      </tr>
                      {{~ else ~}}
                      <tr><td colspan="3"><em>This order has no lines.</em></td></tr>
                      {{~ end ~}}
                      <tr><td colspan="2" align="right"><strong>Total</strong></td>
                          <td align="right"><strong>{{ order.total | vnd }}</strong></td></tr>
                    </table>
                    <p>Placed on {{ order.placed_at | format 'D' }}.</p>
                  </body>
                </html>
                """,
            UpdatedAtUtc = SeededAt,
        },
        new()
        {
            TemplateKey = "order-confirmation",
            Culture = "vi",
            Channel = TemplateChannel.Email,
            Name = "Email xác nhận đơn hàng (Tiếng Việt)",
            Description = "Gửi khi thanh toán thành công. Lặp qua từng dòng hàng và cộng tổng.",
            Subject = "Đơn hàng {{ order.reference }} đã được xác nhận",
            TextBody = """
                Xin chào {{ customer.first_name }},

                {{ case order.status ~}}
                {{ when 'paid' ~}}
                Đã nhận thanh toán — chúng tôi đang đóng gói đơn hàng.
                {{ when 'pending' ~}}
                Chúng tôi vẫn đang chờ thanh toán của bạn.
                {{ else ~}}
                Trạng thái đơn hàng: {{ order.status }}
                {{ end ~}}

                {{ for line in order.lines ~}}
                  {{ line.quantity }} x {{ line.name }} — {{ line.total | format 'N0' }} đ
                {{ else ~}}
                  (đơn hàng không có sản phẩm nào)
                {{ end ~}}

                Tổng cộng: {{ order.total | vnd }}
                Đặt ngày {{ order.placed_at | format 'D' }}.
                """,
            HtmlBody = """
                <html>
                  <body style="font-family: sans-serif">
                    <h1>Đơn hàng {{ order.reference }} đã được xác nhận</h1>
                    <p>Xin chào <strong>{{ customer.first_name }}</strong>,</p>
                    {{~ if customer.is_vip ~}}
                    <p style="color: #a67c00">Khách hàng VIP được miễn phí vận chuyển.</p>
                    {{~ else ~}}
                    <p>Phí vận chuyển áp dụng theo bảng giá thông thường.</p>
                    {{~ end ~}}
                    {{~ case order.status ~}}
                    {{~ when 'paid' ~}}
                    <p style="color: #0a7d28">Đã nhận thanh toán — chúng tôi đang đóng gói đơn hàng.</p>
                    {{~ when 'pending' ~}}
                    <p style="color: #a67c00">Chúng tôi vẫn đang chờ thanh toán của bạn.</p>
                    {{~ else ~}}
                    <p>Trạng thái đơn hàng: {{ order.status }}</p>
                    {{~ end ~}}
                    <table cellpadding="6" style="border-collapse: collapse">
                      <tr><th align="left">Sản phẩm</th><th align="right">SL</th><th align="right">Thành tiền</th></tr>
                      {{~ for line in order.lines ~}}
                      <tr style="background: {{ if for.even }}#fff{{ else }}#f6f6f6{{ end }}">
                        <td>{{ line.name }}</td>
                        <td align="right">{{ line.quantity }}</td>
                        <td align="right">{{ line.total | format 'N0' }} đ</td>
                      </tr>
                      {{~ else ~}}
                      <tr><td colspan="3"><em>Đơn hàng không có sản phẩm nào.</em></td></tr>
                      {{~ end ~}}
                      <tr><td colspan="2" align="right"><strong>Tổng cộng</strong></td>
                          <td align="right"><strong>{{ order.total | vnd }}</strong></td></tr>
                    </table>
                    <p>Đặt ngày {{ order.placed_at | format 'D' }}.</p>
                  </body>
                </html>
                """,
            UpdatedAtUtc = SeededAt,
        },
        new()
        {
            TemplateKey = "order-confirmation",
            Culture = "en",
            Channel = TemplateChannel.InApp,
            Name = "Order confirmation notification (English)",
            Description = "One line, so it pluralises with an if instead of a table.",
            Subject = "Order confirmed",
            TextBody =
                "{{ order.lines.size }} item{{ if order.lines.size != 1 }}s{{ end }} " +
                "on the way, {{ order.total | vnd }} total.",
            UpdatedAtUtc = SeededAt,
        },
        new()
        {
            TemplateKey = "order-confirmation",
            Culture = "vi",
            Channel = TemplateChannel.InApp,
            Name = "Thông báo xác nhận đơn hàng (Tiếng Việt)",
            Description = "Một dòng: tiếng Việt không đổi số nhiều, nên không cần if.",
            Subject = "Đã xác nhận đơn hàng",
            TextBody = "{{ order.lines.size }} sản phẩm đang được giao, tổng {{ order.total | vnd }}.",
            UpdatedAtUtc = SeededAt,
        },
        new()
        {
            // Whitespace control (~) keeps a loop on one line, which is all an SMS has room for.
            TemplateKey = "order-confirmation",
            Culture = "vi",
            Channel = TemplateChannel.Other,
            Name = "SMS xác nhận đơn hàng (Tiếng Việt)",
            Description = "Kênh Other: chỉ có text, không tiêu đề, không HTML.",
            TextBody = "XXX: don {{ order.reference }} da xac nhan, {{ order.lines.size }} san pham.",
            UpdatedAtUtc = SeededAt,
        },
    ];
}
