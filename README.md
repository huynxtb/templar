# Templar

Render e-mail and in-app notification content from templates stored in your database —
multi-language, plain-text and HTML. One NuGet package per database engine. Targets .NET 10.

## Install

Install the package for your database. It brings the rest with it.

```bash
dotnet add package Templar.PostgreSql     # or MySql, SqlServer, Oracle, Mongo
```

| Package | Database | Extension method |
| --- | --- | --- |
| `Templar.PostgreSql` | PostgreSQL | `UsePostgreSql(…)` |
| `Templar.MySql` | MySQL / MariaDB | `UseMySql(…)` |
| `Templar.SqlServer` | SQL Server / Azure SQL | `UseSqlServer(…)` |
| `Templar.Oracle` | Oracle Database | `UseOracle(…)` |
| `Templar.Mongo` | MongoDB | `UseMongo(…)` |

`Templar.Core` (model, the Scriban template engine, caching, in-memory store) and `Templar.Relational`
(shared ADO.NET store) come in as dependencies — install them directly only if you are writing your
own store. There is nothing else to add: [loops and conditionals](#a-table-and-a-conditional) work
out of the box.

## Use it

Two namespaces: `Templar` for registration and the model, `Templar.Abstractions` for the services.

Register once:

```csharp
using Templar;

builder.Services
    .AddTemplar(options => options.DefaultCulture = "en")
    .UsePostgreSql(builder.Configuration.GetConnectionString("Templates")!);
```

Create the table if you do not manage schema yourself:

```csharp
await using var scope = app.Services.CreateAsyncScope();
await scope.ServiceProvider.GetRequiredService<ITemplateSchemaInitializer>().EnsureSchemaAsync();
```

Save a template per language:

```csharp
var en = new TemplateDefinition
{
    TemplateKey  = "welcome-user",
    Culture      = "en",
    Channel      = TemplateChannel.Email,
    Subject      = "Welcome to XXX",
    TextBody     = "Hello {{username}}, this is your email {{EMAIL}}",
    HtmlBody     = "<p>Hello <strong>{{username}}</strong></p>",
    UpdatedAtUtc = DateTimeOffset.UtcNow,
};

var vi = en with
{
    Culture  = "vi",
    Subject  = "Chào mừng tới XXX",
    TextBody = "Xin chào {{username}}, đây là email của bạn {{EMAIL}}",
};

await commands.SaveAsync([en, vi]);        // ITemplateCommandService
```

Then render for a user in their own language:

```csharp
public sealed class WelcomeMailer(ITemplateRenderService templates, IEmailSender sender)
{
    public async Task SendAsync(User user, CancellationToken ct)
    {
        var email = await templates.RenderAsync(new TemplateRenderRequest
        {
            TemplateKey = "welcome-user",
            Culture     = user.Language,          // "vi-VN" → falls back to "vi", then to "en"
            Values      = TemplateValues.Create()
                              .Set("username", user.Name)
                              .Set("EMAIL", user.Email),
        }, ct);

        await sender.SendAsync(user.Email, email.Subject!, email.Text, email.Html, ct);
    }
}
```

`email.Subject` / `.Text` / `.Html` are the finished strings. `RenderAsync` throws
`TemplateNotFoundException` when nothing matches; `TryRenderAsync` returns `null` instead.

Switching database means changing the one `Use…` call — nothing else.

## Template syntax

Templates are [Scriban](https://github.com/scriban/scriban), so a body can insert a value, branch on
it, or loop over it. Nothing to register — `AddTemplar()` already did.

| Syntax | Meaning |
| --- | --- |
| `{{ username }}` | Insert the value named `username` |
| `{{ USER_EMAIL }}` | Same value as `{{ userEmail }}` — matching ignores case and `_ - . ` separators |
| `{{ DATE \| format 'dd/MM/yyyy' }}` | Any .NET format string, applied in the template's culture |
| `{{ if … }}` `{{ else }}` `{{ end }}` | Show a block only when the data says so |
| `{{ for x in list }}` `{{ end }}` | Repeat a block — a table of order lines, say |
| `{{{{` | A literal `{{` |

Values go into an HTML body HTML-encoded; wrap trusted markup in `TemplateRaw.Html(…)` to opt out.

### A table and a conditional

When the *shape* of the output depends on the data — a table of order lines, a paragraph only VIP
customers see — write it in the body:

```
{{~ if customer.is_vip ~}}<p>Your delivery is free.</p>{{~ end ~}}
<table>
  {{~ for line in order.lines ~}}
  <tr><td>{{ line.name }}</td><td>{{ line.total | format 'N0' }}</td></tr>
  {{~ else ~}}
  <tr><td colspan="2">This order has no lines.</td></tr>
  {{~ end ~}}
</table>
```

Pass the nested data straight in — a list of objects stays a list:

```csharp
TemplateValues.Create()
    .Set("customer", new { IsVip = true })
    .Set("order", new { Lines = lines, Total = total });
```

### Your own functions

Register them once where you configure the container, and every stored template can call them:

```csharp
services.AddTemplar()
        .UsePostgreSql(connectionString)
        .UseScriban(options => options.Functions["vnd"] = (decimal amount) => $"{amount:N0} ₫");
```

```
Total: {{ order.total | vnd }}        →  Total: 1.250.000 ₫
```

`{{ vnd total }}` is the same call. Functions run in the template's culture, so that one delegate
groups digits as `1.250.000` for a Vietnamese row and `1,250,000` for an English one.

### Coming from 1.0

1.0 had a placeholder-only engine, which 2.0 removed. `{{username}}` still means the same thing, so
most bodies carry over untouched. Two do not: `{{DATE:dd/MM/yyyy}}` becomes
`{{ DATE | format 'dd/MM/yyyy' }}`, and text that merely *looks* like a placeholder is now a syntax
error rather than literal text.

Scriban renders the old format syntax as an empty string rather than failing, so Templar rejects it
outright at compile time with a message naming the replacement, instead of letting a live table lose
values quietly. Rewriting those rows is the whole migration.

Full details are in
[the reference](https://github.com/huynxtb/templar/blob/main/docs/reference.md#coming-from-templar-10).

## Languages and channels

One template key holds one row per language × channel:

| key | culture | channel | subject |
| --- | --- | --- | --- |
| `welcome-user` | `en` | `Email` | `Welcome to XXX` |
| `welcome-user` | `vi` | `Email` | `Chào mừng tới XXX` |
| `welcome-user` | `vi` | `InApp` | `Chào mừng!` |

Those three columns are the primary key — `PRIMARY KEY (template_key, culture, channel)` on every SQL
engine, and the same three fields inside the composite `_id` on MongoDB. There is no surrogate id, so
a triple exists at most once and `SaveAsync` is an upsert against it: saving `welcome-user` / `vi` /
`Email` again edits that row instead of adding a second one.

A request walks *requested culture → its parents → default culture → its parents* and takes the first
active row, so `vi-VN` resolves to `vi` and an unknown `ja` resolves to the default. Channels are
`Email`, `InApp`, `Sms`, `WhatsApp`, `Zalo`, `Facebook` and `Other` (push, webhook, anything else) —
Templar renders them all the same way, delivery is yours.

## Three services

`AddTemplar()` registers three interfaces, so you depend only on what you use:

| Service | For |
| --- | --- |
| `ITemplateRenderService` | Rendering — `RenderAsync`, `TryRenderAsync` |
| `ITemplateQueryService` | Reading — list keys, get variants, find one, resolve with fallback |
| `ITemplateCommandService` | Writing — `SaveAsync` (upsert), `DeleteAsync`, `InvalidateAsync` |

Write through `ITemplateCommandService` rather than the store: it evicts the cache for you.

A fourth is metadata only: `ITemplateChannelService.GetAll()` lists the channels as
`{ value: 0, label: "Email" }` pairs, for filling a picker without hard-coding the enum.

Templates are cached in process for five minutes by default. Call `UseDistributedCache()` after
registering any `IDistributedCache` to share one copy between instances.

## Try it

One sample per provider. Each is a single `Program.cs` you can read top to bottom — configuration,
registration, seeding, then a Swagger-browsable API — with nothing shared between them, so you can
copy the one you need and delete the rest. Start with the in-memory one; it needs no database:

```bash
dotnet run --project samples/Templar.Sample.InMemory      # → http://localhost:5000/swagger
make samples                                              # every sample and its port
```

| Sample | Shows | Port |
| --- | --- | --- |
| `Templar.Sample.InMemory` | The in-memory store — no database needed | 5000 |
| `Templar.Sample.MemoryCache` | The default in-process cache, with a store-read counter | 5001 |
| `Templar.Sample.DistributedCache` | `UseDistributedCache()` over Redis or memory | 5002 |
| `Templar.Sample.Scriban` | an order e-mail with a line-item table and a VIP conditional | 5003 |
| `Templar.Sample.PostgreSql` | PostgreSQL | 5010 |
| `Templar.Sample.MySql` | MySQL / MariaDB | 5011 |
| `Templar.Sample.SqlServer` | SQL Server / Azure SQL | 5012 |
| `Templar.Sample.Oracle` | Oracle Database | 5013 |
| `Templar.Sample.Mongo` | MongoDB | 5014 |

The database ones create their own table. `samples/docker-compose.yml` starts each engine with the
credentials its sample already expects, so there is nothing to configure:

```bash
make up SAMPLE=PostgreSql       # docker compose, matching appsettings.json
make run SAMPLE=PostgreSql      # → http://localhost:5010/swagger
make down                       # stop the databases
```

Or point one at a server of your own:

```bash
dotnet run --project samples/Templar.Sample.PostgreSql \
  --Templates:ConnectionString="Host=localhost;Database=notifications;Username=postgres;Password=secret"
```

Every sample exposes the same API — full CRUD, `/api/render`, `/api/render/html` and
`/api/cache/clear` — with Swagger UI in front of it, so `/` redirects to `/swagger` and you can call
everything from the browser. Details in
[docs/reference.md](https://github.com/huynxtb/templar/blob/main/docs/reference.md#sample-applications).

## More

- **[docs/reference.md](https://github.com/huynxtb/templar/blob/main/docs/reference.md)** — every
  option, service lifetimes, caching internals,
  the data model and per-engine column types, table/schema configuration, the sample's API, tests.
- `make help` — build, test and packaging commands.
