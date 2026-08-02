# Templar reference

The detail behind [the README](../README.md): every option, the data model, caching semantics and the
sample application's API.

## The three services

`AddTemplar()` registers three interfaces so a caller depends only on what it does.

| Service | Members |
| --- | --- |
| `ITemplateRenderService` | `RenderAsync`, `TryRenderAsync` — resolve a variant and substitute values |
| `ITemplateQueryService` | `ListAsync`, `ListKeysAsync`, `GetVariantsAsync`, `FindAsync` (exact), `ResolveAsync` (with fallback) |
| `ITemplateCommandService` | `SaveAsync`, `DeleteAsync`, `InvalidateAsync` |

A fourth, `ITemplateChannelService`, is metadata rather than data: `GetAll()` returns every
`TemplateChannel` as a `TemplateChannelInfo(int Value, string Label)`, ordered by value, so an admin
screen can fill a channel picker without hard-coding the enum — and a channel added to the enum
appears there on its own. It reads neither store nor cache, so it is a singleton and works even
before a database provider is attached.

```csharp
// [{ "value": 0, "label": "Email" }, { "value": 1, "label": "InApp" }, … ]
IReadOnlyList<TemplateChannelInfo> channels = channelService.GetAll();
```

`Label` is exactly what the `channel` column stores, so it round-trips back to
`Enum.Parse<TemplateChannel>(label)`.

`RenderAsync` throws `TemplateNotFoundException` when nothing matches; `TryRenderAsync` returns
`null`.

`FindAsync` matches the exact culture and returns inactive rows, which is what an editing screen
needs; `ResolveAsync` applies fallback and skips inactive rows, which is what a render needs.

## Managing templates (CRUD)

Commands write, queries read. There is no separate "create" and "update" — `SaveAsync` is keyed by
(key, culture, channel) — and every command drops the affected key from the read cache, which is the
step that is easy to forget when writing to `ITemplateWriteStore` directly.

```csharp
using Templar;
using Templar.Abstractions;

public sealed class TemplateAdmin(ITemplateCommandService commands, ITemplateQueryService queries)
{
    // CREATE / UPDATE — one template, or a whole set of languages at once
    public Task SaveAsync(TemplateDefinition template, CancellationToken ct)
        => commands.SaveAsync(template, ct);

    public Task SaveAllAsync(IEnumerable<TemplateDefinition> templates, CancellationToken ct)
        => commands.SaveAsync(templates, ct);

    // READ — everything, every key, every variant of one key, or one exact variant
    public Task<IReadOnlyList<TemplateDefinition>> ListAsync(CancellationToken ct)
        => queries.ListAsync(ct);

    public Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken ct)
        => queries.ListKeysAsync(ct);

    public Task<IReadOnlyList<TemplateDefinition>> GetAsync(string key, CancellationToken ct)
        => queries.GetVariantsAsync(key, ct);

    public Task<TemplateDefinition?> GetAsync(string key, string culture, TemplateChannel channel, CancellationToken ct)
        => queries.FindAsync(key, culture, channel, ct);

    // DELETE — one variant
    public Task<bool> DeleteAsync(string key, string culture, TemplateChannel channel, CancellationToken ct)
        => commands.DeleteAsync(key, culture, channel, ct);
}
```

`TemplateDefinition` is a record, so adding a language or editing a variant is a `with` away:

```csharp
var vietnamese = new TemplateDefinition
{
    TemplateKey  = "welcome-user",
    Culture      = "vi",
    Channel      = TemplateChannel.Email,
    Name         = "Email chào mừng",                  // metadata for admin screens, never rendered
    Description  = "Gửi sau khi người dùng xác nhận email.",
    Subject      = "Chào mừng tới XXX",
    TextBody     = "Xin chào {{username}}, đây là email của bạn {{EMAIL}}",
    HtmlBody     = "<p>Xin chào <strong>{{username}}</strong></p>",
    UpdatedAtUtc = DateTimeOffset.UtcNow,
};

var english = vietnamese with { Culture = "en", Subject = "Welcome to XXX", Name = "Welcome e-mail" };
var edited  = english with { Subject = "Welcome!", UpdatedAtUtc = DateTimeOffset.UtcNow };
```

`ITemplateSchemaInitializer.EnsureSchemaAsync()` creates the table or index if it is missing — handy
for tests and small deployments; production schemas usually come from real migrations.

## Values and formatting

```csharp
var values = TemplateValues.Create()
    .Set("username", "Nguyễn")                                   // HTML-encoded in an HTML body
    .Set("AMOUNT", 1234.5m)                                      // "1.234,5" for vi, "1,234.5" for en
    .Set("EXPIRES_AT", DateTimeOffset.UtcNow.AddMinutes(15))
    .Set("CTA", TemplateRaw.Html("<a href=\"/go\">Confirm</a>")); // trusted markup, not encoded
```

Values substituted into an HTML body are HTML-encoded (`<script>` arrives as `&lt;script&gt;`) while
non-ASCII letters stay intact, so Vietnamese text remains readable. Text bodies and subjects are
never encoded. Numbers and dates format in the *template's* culture, not the request's.

`TemplateValues.FromObject(new { … })` builds the same thing from an anonymous object. Placeholder
names match case-insensitively and ignore `_`, `-`, `.` and space, so one value named `username`
satisfies `{{username}}`, `{{UserName}}` and `{{USER_NAME}}`. Pass `StringComparer.Ordinal` to
`TemplateValues.Create(…)` when exact matching is required.

## Channels and parts

One key holds one row per language **×** channel.

| Channel | Typical shape |
| --- | --- |
| `TemplateChannel.Email` | Subject plus a text and/or HTML body |
| `TemplateChannel.InApp` | Title plus a short body |
| `TemplateChannel.Sms` | A short plain-text body; no subject, no HTML |
| `TemplateChannel.WhatsApp` | A WhatsApp message |
| `TemplateChannel.Zalo` | A Zalo message |
| `TemplateChannel.Facebook` | A Facebook message — Messenger or a page notification |
| `TemplateChannel.Other` | Anything else — push payload, webhook body, PDF fragment |

Templar renders every one of them the same way and delivers none of them: sending is yours. `Other`
is deliberately the last member, so a channel added later slots in before it and the "none of the
above" bucket stays at the end.

`Parts` skips rendering work you do not need:

```csharp
var sms = await templates.RenderAsync(new TemplateRenderRequest
{
    TemplateKey = "reset-password",
    Culture     = "vi",
    Channel     = TemplateChannel.Sms,
    Values      = values,
    Parts       = TemplateParts.Text,
});
```

## Options

| Option | Default | Effect |
| --- | --- | --- |
| `DefaultCulture` | `en` | Used when a request omits a culture; ends the fallback chain |
| `EnableCultureFallback` | `true` | `vi-VN` → `vi` → default; off means exact match only |
| `EnableCache` | `true` | Cache templates read from the database |
| `CacheDuration` | 5 min | Lifetime of a cached template set |
| `CacheKeyPrefix` | `templar:` | Key prefix for the distributed cache; ignored in process |
| `MissingVariableBehavior` | `Throw` | Or `Empty` (blank it) / `Keep` (leave `{{name}}` in place) |
| `HtmlEncodeValues` | `true` | Encode substituted values in HTML bodies |
| `CompiledTemplateCacheSize` | 1024 | Parsed templates kept in memory |

`MissingVariableBehavior` can be overridden per request.

## Service lifetimes

Anything that touches the database is **scoped**, the lifetime a connection expects; the cache and the
rendering engine are **singletons**.

| Registration | Lifetime |
| --- | --- |
| `ITemplateQueryService`, `ITemplateCommandService`, `ITemplateRenderService` | Scoped |
| `ITemplateStore`, `ITemplateWriteStore`, `ITemplateSchemaInitializer` | Scoped |
| `ITemplateCache` | Singleton — a per-request cache would not be a cache |
| `ITemplateCompiler`, `ITemplateRenderer` | Singleton — stateless apart from their own caches |
| `ITemplateChannelService` | Singleton — a fixed list read off the enum |
| `IMongoCollection<MongoTemplateDocument>` | Singleton — `MongoClient` owns the connection pool |
| `InMemoryTemplateStore` | Singleton — it *is* the data, so a scoped one would start empty |

Inside a request this is automatic. Outside one — startup seeding, a background service, a console
app — create a scope first:

```csharp
await using var scope = app.Services.CreateAsyncScope();
await scope.ServiceProvider.GetRequiredService<ITemplateCommandService>().SaveAsync(templates);
```

## Caching

By default templates are cached in process for `CacheDuration`, one entry per template key, and
commands evict the key they wrote. To share one copy between instances, register any
`IDistributedCache` and add `UseDistributedCache()`:

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379");

builder.Services
    .AddTemplar(o => o.CacheKeyPrefix = "myapp:templates:")   // several apps, one Redis
    .UsePostgreSql(connectionString)
    .UseDistributedCache();
```

Entries are JSON. `IDistributedCache` cannot delete by pattern, so `InvalidateAsync(key)` removes
that key exactly, while `InvalidateAsync()` bumps a shared generation counter that makes every
existing key unreachable — other instances pick that up within two seconds. Set `EnableCache = false`
to turn caching off entirely while authoring templates.

A cache that cannot be reached never propagates the failure: it is logged as a warning and bypassed.
A read falls through to the store, and a failed eviction or clear leaves the stale entry alone rather
than failing the save that triggered it — so after a cache outage a template can be served from a
stale entry until `CacheDuration` expires it. The warning names the key this happened to, which is
what to watch for if you need writes to be visible everywhere immediately.

## Data model

The natural key is `(template_key, culture, channel)`. Every row for a key is fetched in one query
and cached; the language is then chosen in memory.

```mermaid
erDiagram
    TEMPLATE_KEY ||--|{ notification_templates : "welcome-user has en, vi and InApp rows"

    TEMPLATE_KEY {
        varchar_200 template_key PK "welcome-user, reset-password"
    }
    notification_templates {
        varchar_200 template_key PK "logical name, shared by all languages"
        varchar_20 culture PK "BCP-47: en, vi, vi-VN"
        varchar_20 channel PK "Email, InApp, Sms, WhatsApp, Zalo, Facebook, Other"
        varchar_200 name "nullable - label for admin screens, never rendered"
        varchar_1000 description "nullable - what it is for, never rendered"
        varchar_1000 subject "nullable - subject (e-mail) or title (in-app)"
        text text_body "nullable - plain-text body"
        text html_body "nullable - HTML body"
        boolean is_active "default true, inactive rows are never served"
        timestamp updated_at "UTC"
    }
```

Per-engine column types, all created by `EnsureSchemaAsync()`:

| Column | MySQL | SQL Server | PostgreSQL | Oracle |
| --- | --- | --- | --- | --- |
| `template_key` / `name` | `VARCHAR(200)` | `NVARCHAR(200)` | `varchar(200)` | `VARCHAR2(200 CHAR)` |
| `culture`, `channel` | `VARCHAR(20)` | `NVARCHAR(20)` | `varchar(20)` | `VARCHAR2(20 CHAR)` |
| `description`, `subject` | `VARCHAR(1000)` | `NVARCHAR(1000)` | `text` | `VARCHAR2(1000 CHAR)` |
| `text_body`, `html_body` | `LONGTEXT` | `NVARCHAR(MAX)` | `text` | `CLOB` |
| `is_active` | `TINYINT(1)` | `BIT` | `boolean` | `NUMBER(1)` |
| `updated_at` | `DATETIME(6)` | `DATETIME2(3)` | `timestamptz` | `TIMESTAMP(3)` |

MongoDB stores the same fields camelCased, with the three key parts inside a composite `_id`.

### Primary key

`(template_key, culture, channel)` is the primary key itself, not just a unique constraint, and there
is no surrogate id column. The triple *is* the row's identity: one logical template, in one language,
for one channel.

| Engine | How `EnsureSchemaAsync()` declares it |
| --- | --- |
| MySQL | `PRIMARY KEY (template_key, culture, channel)` |
| PostgreSQL | `PRIMARY KEY (template_key, culture, channel)` |
| SQL Server | `CONSTRAINT PK_<table> PRIMARY KEY CLUSTERED (template_key, culture, channel)` |
| Oracle | `CONSTRAINT PK_<table> PRIMARY KEY (template_key, culture, channel)` |
| MongoDB | `_id` is a document — `{ templateKey, culture, channel }` — so uniqueness comes free |

Three things follow from it:

- **Writes are upserts.** The same triple is every dialect's conflict target: `ON DUPLICATE KEY
  UPDATE` (MySQL), `MERGE … WITH (HOLDLOCK)` (SQL Server), `ON CONFLICT … DO UPDATE` (PostgreSQL),
  `MERGE` (Oracle), `_id` replace (MongoDB). `SaveAsync` therefore has no separate create and update:
  saving `welcome-user` / `vi` / `Email` twice edits one row rather than adding a second.
- **Reads use its index.** `template_key` leads the key, so `SELECT … WHERE template_key = @key` —
  the one query behind every render — is served by the primary key index with no extra index to
  maintain. MongoDB cannot do that (`_id` is a whole document), which is why `EnsureSchemaAsync()`
  creates `ix_template_key` there and nowhere else.
- **The constraint name follows `TableName`.** SQL Server and Oracle name it `PK_<TableName>`, so
  pointing the store at `email_templates` produces `PK_email_templates` — upper-cased on Oracle,
  like every other identifier, unless `PreserveIdentifierCase` is set.

Case sensitivity of the key columns is the database's, not the library's: MySQL's
`utf8mb4_unicode_ci` matches keys case-insensitively, MongoDB compares byte-wise. Culture matching is
case-insensitive everywhere regardless, because it happens in `TemplateQueryService` rather than in
the store.

### Table name and schema

The table is called `notification_templates` by default. Point the library at your own name:

```csharp
.UsePostgreSql(connectionString, o =>
{
    o.TableName = "email_templates";
    o.Schema    = "notify";              // null leaves the table unqualified
    o.CommandTimeoutSeconds = 30;
})

.UseMongo(connectionString, o => { o.DatabaseName = "notifications"; o.CollectionName = "email_templates"; })
```

Renaming does not migrate anything — the new table is created empty, and an existing table needs the
columns above.

## Sample applications

There is one sample per provider, plus one for each cache. **Each is a single self-contained
`Program.cs` and shares no code with the others** — configuration, registration, seeding, the API and
the seed data are all in that one file, in that order, so you can read it start to finish and copy
the one you need without untangling anything. The files are near-identical on purpose; the provider
differences are the `Use…` call and the comment above it.

Each seeds `welcome-user` (`en`/`vi`, e-mail and in-app), `reset-password` (`en`/`vi` plus an SMS on
the `Other` channel) and, where the store supports it, creates its own table on startup.

| Project | Shows | Port |
| --- | --- | --- |
| `Templar.Sample.InMemory` | `UseInMemoryStore()` — needs no database | 5000 |
| `Templar.Sample.MemoryCache` | The default `MemoryTemplateCache`, with a store-read counter | 5001 |
| `Templar.Sample.DistributedCache` | `UseDistributedCache()` over Redis or memory | 5002 |
| `Templar.Sample.PostgreSql` | PostgreSQL | 5010 |
| `Templar.Sample.MySql` | MySQL / MariaDB | 5011 |
| `Templar.Sample.SqlServer` | SQL Server / Azure SQL | 5012 |
| `Templar.Sample.Oracle` | Oracle Database | 5013 |
| `Templar.Sample.Mongo` | MongoDB | 5014 |

```bash
make samples                                          # the list above
make run                                              # InMemory, http://localhost:5000/swagger
make up SAMPLE=PostgreSql && make run SAMPLE=PostgreSql
dotnet run --project samples/Templar.Sample.Mongo     # the underlying command
```

The whole of a sample's registration is this:

```csharp
var builder = WebApplication.CreateBuilder(args);
var settings = builder.Configuration.GetSection("Templates");

builder.Services.AddOpenApi();
builder.Services
    .AddTemplar(options => options.DefaultCulture = settings["DefaultCulture"] ?? "en")
    .UsePostgreSql(settings["ConnectionString"]!, store => store.Schema = settings["Schema"]);
```

### Swagger

`AddOpenApi()` produces the document at `/openapi/v1.json`; `Swashbuckle.AspNetCore.SwaggerUI` puts
Swagger UI in front of it at `/swagger`, and `/` redirects there. Endpoints carry `WithTags` and
`WithSummary`, so the page groups them under **Templates**, **Render** and **Cache** and explains each
one. Everything below is callable from that page — there is no separate UI.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/` | Redirects to `/swagger` |
| `GET` | `/api/templates` | Every stored template, including inactive rows |
| `GET` | `/api/keys` | Just the template keys |
| `GET` | `/api/channels` | Every channel as `{ value, label }` |
| `GET` | `/api/templates/{key}` | Every language and channel of a key |
| `GET` | `/api/templates/{key}/{culture}?channel=` | One exact variant |
| `GET` | `/api/resolve/{key}?culture=&channel=` | Which variant fallback picks, unrendered |
| `POST` | `/api/templates` | Create |
| `PUT` | `/api/templates/{key}/{culture}?channel=` | Update one variant |
| `DELETE` | `/api/templates/{key}/{culture}?channel=` | Delete |
| `POST` | `/api/render` | Render subject, text and HTML |
| `POST` | `/api/render/html` | The same render as `text/html`, for looking at in a browser |
| `POST` | `/api/cache/clear?key=` | `InvalidateAsync` — omit `key` to clear every key |
| `GET` | `/api/cache/stats` | Store reads so far. **Cache samples only** |

`values` in a render body is free-form JSON, and its types are used as they arrive:

```json
{ "templateKey": "reset-password", "culture": "en",
  "values": { "username": "Huy", "CODE": "007193", "MINUTES": 15,
              "EXPIRES_AT": "2026-08-01T09:30:00Z" } }
```

`15` stays a number and formats per culture in `{{MINUTES:N0}}`, the ISO string becomes a
`DateTimeOffset` for `{{EXPIRES_AT:g}}`, and `"007193"` stays a string so a verification code keeps
its leading zero.

```bash
J="content-type: application/json"

curl -s localhost:5000/api/templates -H "$J" -d '{
  "templateKey": "invoice-paid", "culture": "vi", "channel": "Email",
  "name": "Email hoá đơn", "description": "Gửi khi thanh toán thành công.",
  "subject": "Hoá đơn {{invoiceNo}} đã thanh toán",
  "textBody": "Xin chào {{username}}, hoá đơn {{invoiceNo}} ({{AMOUNT:N0}} đ) đã thanh toán." }'

curl -s localhost:5000/api/render -H "$J" -d '{ "templateKey": "invoice-paid", "culture": "vi",
  "values": { "username": "Huy", "invoiceNo": "INV-204", "AMOUNT": 1990000 } }'
# → "Xin chào Huy, hoá đơn INV-204 (1.990.000 đ) đã thanh toán."

curl -s -X PUT 'localhost:5000/api/templates/invoice-paid/vi' -H "$J" \
     -d '{ "subject": "Đã nhận thanh toán", "textBody": "Cảm ơn {{username}}." }'
curl -s -X DELETE 'localhost:5000/api/templates/invoice-paid/vi'
```

A missing value returns `400` naming the placeholders; an unknown key or variant returns `404`.

### The two cache samples

Both wrap their store in a `CountingTemplateStore` — declared at the bottom of the same file — so
`GET /api/cache/stats` is a live cache-hit meter: render the same template twice and `storeReads` does
not move; save, delete or clear, and the next render is a miss.

`Templar.Sample.MemoryCache` is the default — `MemoryTemplateCache`, one entry per template key, at a
deliberately short 30 second `CacheSeconds` so expiry is watchable.

`Templar.Sample.DistributedCache` calls `UseDistributedCache()`. Without `Templates:Redis` it
registers `AddDistributedMemoryCache()`, which runs anywhere but is per-process. Point it at Redis and
two instances genuinely share one cache:

```bash
dotnet run --project samples/Templar.Sample.DistributedCache --Templates:Redis=localhost:6379 --urls http://localhost:5002
dotnet run --project samples/Templar.Sample.DistributedCache --Templates:Redis=localhost:6379 --urls http://localhost:5003
```

Render on 5003 and its `/api/cache/stats` goes to 1; render the same template on 5002 and its stays at
**0**, because it read the entry the other node wrote. Clear the cache on either and the other's next
read is a miss again within `DistributedTemplateCache.GenerationRefresh` (2 s) — that is the
generation counter in `{prefix}{generation}:{key}` doing its work.

The store in this sample is still per-process and in-memory, so it is the *cache* that is shared, not
the data: a template saved on one node is not readable from the other. Combine it with one of the
database samples for that.

### Backing services

`samples/docker-compose.yml` runs each database with the credentials, port and database name the
matching sample already has in its `appsettings.json`, so nothing needs configuring:

```bash
make up SAMPLE=PostgreSql        # just PostgreSQL
make up SAMPLE=all               # every engine plus Redis
make down                        # stop, keep the data
make down-clean                  # stop and delete the volumes
```

The compose profiles are the lowercased sample names, which is why `up` takes the same `SAMPLE=` as
`run`; `InMemory` and `MemoryCache` match no service, so `up` is a harmless no-op for them.

- Every host port is overridable when one is already taken:
  `TEMPLAR_REDIS_PORT=6399 make up SAMPLE=DistributedCache`. The variables are
  `TEMPLAR_{POSTGRES,MYSQL,SQLSERVER,ORACLE,MONGO,REDIS}_PORT`. Remember to point the sample at the
  new port too — `--Templates:ConnectionString=…`.
- SQL Server starts with no application database, so `make up SAMPLE=SqlServer` also runs a one-shot
  `sqlserver-init` container that creates `notifications`. Everything else creates its database from
  image environment variables.
- Oracle takes a few minutes on first start while it builds its data files; the healthcheck covers it,
  so `make up` simply waits. On Apple Silicon the SQL Server and Oracle images are amd64 and run
  emulated — slower, and Docker prints a platform warning.
- Each engine keeps a named volume, so data survives `make down`. Only `make down-clean` discards it.
- The samples create their own table on startup, so a scratch database with DDL permission is all any
  of them needs — which is also what the compose services provide.

### Configuration

Every sample reads the same `Templates` section, and has no `Provider` key — the project *is* the
choice of provider. A database sample's `appsettings.json` carries a placeholder connection string:

```json
"Templates": {
  "ConnectionString": "Host=localhost;Port=5432;Database=notifications;Username=postgres;Password=secret",
  "TableName": "notification_templates", "Schema": "public", "CommandTimeoutSeconds": 30,
  "DefaultCulture": "en", "EnableCultureFallback": true,
  "EnableCache": true, "CacheSeconds": 300,
  "SeedOnStartup": true
}
```

- `ConnectionStrings:Templates` is the fallback when `Templates:ConnectionString` is empty. A sample
  that finds neither fails at startup saying so.
- `Schema: ""` leaves the table unqualified; omitting the key keeps the provider default (`dbo`,
  `public`). MongoDB uses `Database` plus `TableName` as the collection name instead.
- `Oracle` additionally reads `PreserveIdentifierCase`; `DistributedCache` additionally reads `Redis`
  and `CacheKeyPrefix`.
- `SeedOnStartup: false` skips both `EnsureSchemaAsync` and the seed, leaving whatever is already
  stored.
- Everything is overridable on the command line:
  `dotnet run --Templates:TableName=email_templates --Templates:CacheSeconds=5`.
- Real credentials belong in user secrets or environment variables, not in this file.

## Tests

```bash
make test         # or: dotnet test Templar.slnx
```

74 unit tests cover the parser, renderer, the three services, culture fallback, channels, parts, the
in-process and distributed caches, the in-memory store, DI lifetimes and the SQL each dialect
generates — none need a database.

The five provider round-trip tests are skipped unless a connection string is exported. Each covers
DDL, both upsert paths, Unicode, a 12 KB body, UTC round-tripping, the `Other` channel, delete and a
render through the services. They create and drop their own `notification_templates_it` table, so a
scratch database with DDL permission is enough.

```bash
export TEMPLAR_POSTGRES="Host=localhost;Port=5432;Database=notifications;Username=postgres;Password=secret"
export TEMPLAR_MYSQL="Server=localhost;Port=3306;Database=notifications;User ID=root;Password=secret"
export TEMPLAR_SQLSERVER="Server=localhost,1433;Database=master;User ID=sa;Password=Secret_123;TrustServerCertificate=true"
export TEMPLAR_ORACLE="User Id=system;Password=secret;Data Source=localhost:1521/FREEPDB1"
export TEMPLAR_MONGO="mongodb://localhost:27017"

make test-all
```

## Project structure

```
src/Templar.Core          Abstractions/ (query, command, render + the 3 store contracts),
                          Services/ (their implementations), Rendering/ (compiler, renderer),
                          Caching/ (memory, distributed, null), Stores/ (in-memory),
                          and at the root the model (TemplateDefinition, TemplateValues, …)
                          plus registration (AddTemplar, UseDistributedCache, TemplarBuilder)
src/Templar.Relational    RelationalTemplateStore, shared by the four SQL providers
src/Templar.{MySql,SqlServer,PostgreSql,Oracle,Mongo}
                          one store + one Use… extension each
samples/Templar.Sample.{InMemory,MemoryCache,DistributedCache,PostgreSql,MySql,SqlServer,Oracle,Mongo}
                          four files each and no shared code: Program.cs (wiring, seeding, the API,
                          the request bodies and the seed data, in that order), a .csproj,
                          appsettings.json and Properties/launchSettings.json
samples/docker-compose.yml
                          each engine with the credentials its sample expects
tests/Templar.Tests       unit tests per area + opt-in provider round-trips
```

A SQL provider supplies only its connection object, identifier quoting, DDL and upsert statement;
everything else is inherited. Namespaces follow the folders, except that registration extensions
live in `Templar` so `AddTemplar().UseMySql(cs)` needs one `using` — injecting any of the three
services also needs `using Templar.Abstractions;`.

## Notes

- **Oracle identifiers** are upper-cased before quoting, matching conventional DDL. Set
  `PreserveIdentifierCase = true` for a table created with quoted lower-case names.
- **MongoDB matches template keys exactly** — byte-wise, without a collation. Culture matching stays
  case-insensitive because it happens in the query service.
- **`NU1902` / `NU1903` on restore** come from `SharpCompress` and `Snappier`, pulled in by
  `MongoDB.Driver` for optional wire compression. No released version clears the advisory, so they
  stay at the versions the driver ships with.
- The engine substitutes values — no conditionals, no loops. Logic belongs in the calling code,
  which passes the result in as a value.
