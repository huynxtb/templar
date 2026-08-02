# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

**Templar** — a set of seven NuGet packages that render e-mail / in-app notification content from
templates stored in a database. Multi-language, plain-text and HTML, one package per database engine.
Targets `net10.0`. The original brief is in `DESCRIPTION.md` (Vietnamese). User-facing docs are split:
`README.md` is a short install-and-first-render guide, and `docs/reference.md` is the full manual
(every option, lifetimes, caching internals, data model, per-engine column types, sample API, tests) —
read the latter before changing public API.

## Commands

A `Makefile` wraps the common ones — `make help` lists them:

```bash
make build                 # dotnet build Templar.slnx -c Release
make test                  # unit suite (74 pass, the 5 provider round-trips skip)
make test TEST=MySql_upserts_with_on_duplicate_key_update      # single test by name substring
make test-all              # same, but the provider tests run if their env vars are set
make run                   # sample API on http://localhost:5000
make pack                  # 14 files into artifacts/ — see the `pack` skill
make ci                    # restore → build → test → pack, CI's exact sequence
```

The underlying commands, if you need to vary them (`CONFIG=Debug` also works on any target):

```bash
dotnet build Templar.slnx --configuration Release   # solution is .slnx — needs the .NET 10 SDK
dotnet test  Templar.slnx --configuration Release --no-build
dotnet test  tests/Templar.Tests --filter FullyQualifiedName~SqlDialectTests
```

**Do not run `dotnet format` (or `make format`) casually.** There is no `.editorconfig`, so it
applies SDK defaults that disagree with this codebase: `--verify-no-changes` currently reports 21
whitespace diffs in test and sample files, and applying it would rewrite unrelated code. It is not a
CI gate.

Provider round-trip tests in `DatabaseIntegrationTests` are gated by `DatabaseFactAttribute`: each
skips itself unless its environment variable holds a connection string. Export only the engines you
want to exercise — the rest stay skipped:

```bash
export TEMPLAR_POSTGRES="Host=localhost;Port=5432;Database=notifications;Username=postgres;Password=secret"
export TEMPLAR_MYSQL=…  TEMPLAR_SQLSERVER=…  TEMPLAR_ORACLE=…  TEMPLAR_MONGO=…
dotnet test Templar.slnx
```

They create and drop their own `notification_templates_it` table, so a scratch database with DDL
permission is enough. `NU1902`/`NU1903` on restore are known advisories from `MongoDB.Driver`'s
optional compression dependencies and are deliberately not pinned forward — see the comment in
`Directory.Packages.props`.

## Versioning and release

`<Version>` in `Directory.Build.props` is the single version for all seven packages. CI
(`.github/workflows/build.yml`) pushes to nuget.org with `--skip-duplicate` on every push to `main`, so
an unchanged version is a silent no-op and **bumping `<Version>` is what publishes a release** (plus
a `v<version>` tag and GitHub release). Package versions are centralised in
`Directory.Packages.props` — never put a `Version=` on a `PackageReference`. Publishing uses
nuget.org **trusted publishing** (OIDC), not a stored API key: the `publish` job requests
`id-token: write`, exchanges the token via `NuGet/login@v1` (username from `secrets.NUGET_USER`) and
pushes with the one-hour key it returns. The nuget.org policy is pinned to workflow file `build.yml`
plus environment `production`, so renaming either breaks publishing. The `pack` skill
(`.claude/skills/pack/`) covers building and verifying the packages locally.

## Architecture

### Three services, not one

`AddTemplar()` registers three narrow interfaces so a caller depends only on what it does. All live
in `Templar.Abstractions`, implementations in `src/Templar.Core/Services/`:

| Service | Role |
| --- | --- |
| `ITemplateQueryService` | Read: `ListAsync` (whole table), `ListKeysAsync`, `GetVariantsAsync`, `FindAsync` (exact), `ResolveAsync` (fallback) |
| `ITemplateCommandService` | Write: `SaveAsync` (upsert), `DeleteAsync`, `InvalidateAsync` |
| `ITemplateRenderService` | `RenderAsync` (throws `TemplateNotFoundException`) / `TryRenderAsync` (null) |
| `ITemplateChannelService` | Metadata: `GetAll()` → every `TemplateChannel` as `TemplateChannelInfo(Value, Label)` |

The read path is: `RenderService` → `QueryService.ResolveAsync` → `ITemplateCache.GetOrAddAsync` →
`ITemplateStore.GetTemplateSetAsync`. **One store query fetches every row for a template key**; the
cache holds that whole set, and culture fallback plus channel selection then happen in memory. Keep
it that way — resolving a language must not cost another round trip.

`FindAsync` returns inactive rows and matches the culture exactly (what an admin editor needs);
`ResolveAsync` skips inactive rows and applies fallback (what a render needs). Don't collapse them.

`ListAsync` → `ITemplateStore.GetAllTemplatesAsync` reads the whole table (ordered by key, culture,
channel) and deliberately bypasses the cache — the cache holds one entry per key, so there is nothing
for a whole-table read to hit. It is for admin screens, not the render path.

`TemplateCommandService` exists mainly to pair every write with a cache eviction for that key. Code
that writes via `ITemplateWriteStore` directly will serve stale reads.

### Store contracts and the provider hierarchy

`ITemplateStore` (read) ⊂ `ITemplateWriteStore` (read + upsert + delete); `ITemplateSchemaInitializer`
adds `EnsureSchemaAsync()`. `src/Templar.Relational/RelationalTemplateStore` is an abstract ADO.NET
implementation — no ORM, only the engine's driver — that the four SQL providers inherit. A subclass
supplies just five things:

- `ParameterPrefix` (`@`, or `:` for Oracle)
- `CreateConnection()`
- `QuoteIdentifier()` (Oracle upper-cases first unless `PreserveIdentifierCase`)
- `GetSchemaStatements()` — idempotent DDL
- `BuildUpsertSql()` — `ON DUPLICATE KEY UPDATE` / `MERGE … WITH (HOLDLOCK)` / `ON CONFLICT` / `MERGE`

Optional overrides handle type quirks: `ConvertIsActive` (Oracle `NUMBER(1)`), `TimestampDbType`
(null for Npgsql, which infers `timestamptz`), `PrepareCommand` (Oracle needs `BindByName = true`
because the generated `MERGE` mentions parameters out of order), `AddTemplateParameters` (Oracle binds
bodies as CLOB explicitly). `MongoTemplateStore` is separate — it implements the interfaces directly.

**The shared read path maps by ordinal.** `SelectColumns` and `MapTemplate` in
`RelationalTemplateStore` must stay in the same order; `SqlDialectTests` asserts that column list
verbatim. Those tests reach protected SQL properties by reflection so dialects are verified without a
database — add cases there when you touch a dialect.

Each provider package exposes one `Use…(connectionString, configure)` extension in namespace
`Templar` (not `Templar.<Provider>`), so `AddTemplar().UsePostgreSql(cs)` needs a single `using`.
Registration goes through `UseRelationalStore<TStore>`, which points all three store interfaces at
the one concrete instance.

### Lifetimes (deliberate, and asserted by tests)

Anything touching the database is **scoped**; the cache and rendering engine are **singletons** —
`ITemplateCompiler`, `ITemplateRenderer`, `ITemplateCache`, `ITemplateChannelService` (a fixed list
read off the enum), and `InMemoryTemplateStore` (it *is* the data, so a scoped one would start
empty). Outside a request (startup seeding, background service),
`CreateAsyncScope()` first.

### Rendering

`MustacheTemplateCompiler` parses `{{name}}` / `{{name:format}}` / `{{{{` (literal `{{`) into a
`CompiledTemplate` and caches by source string, bounded by `CompiledTemplateCacheSize` (clear-all at
the limit rather than LRU). Anything that only *looks* like a placeholder — unclosed, empty,
containing whitespace, or spanning a newline — is left as literal text, which is what lets CSS and
JSON survive inside an HTML body. There are **no conditionals or loops**, on purpose; logic belongs
in calling code that passes the result in as a value.

Name matching goes through `TemplateVariableNameComparer`: case-insensitive and blind to `_ - .` and
space, so one value named `username` satisfies `{{USER_NAME}}`. Values in an HTML body are
HTML-encoded unless wrapped in `TemplateRaw.Html(…)`; subjects and text bodies never are. Numbers and
dates format in the *template's* culture, not the request's.

`CultureFallback.GetCandidates` walks requested culture → its parents → `DefaultCulture` → its
parents.

### Caching

Three implementations behind `ITemplateCache`: `MemoryTemplateCache` (default),
`NullTemplateCache` (when `EnableCache = false`), and `DistributedTemplateCache` (opt in with
`UseDistributedCache()` after registering any `IDistributedCache`). Two things to know about the
distributed one: `IDistributedCache` cannot delete by pattern, so `ClearAsync` bumps a generation
counter embedded in the key (`{prefix}{generation}:{key}`) that other nodes pick up within
`GenerationRefresh` (2 s); and **every** cache failure is logged and bypassed rather than propagating
— a read falls through to the store, and a failed `RemoveAsync`/`ClearAsync` leaves the stale entry to
expire instead of failing the save that asked for the eviction (`ClearAsync` deliberately does not
bump the local generation when the shared write failed, or this node would move to a key space the
others never learn about). `DistributedTemplateCacheTests` asserts all four paths — preserve that.

### Data model

Natural key is `(template_key, culture, channel)`, which is also every dialect's upsert target.
Table is `notification_templates` by default, overridable per provider along with `Schema` and
`CommandTimeoutSeconds`. Renaming does not migrate anything. Per-engine column types are tabulated in
`docs/reference.md` under "Data model". Mongo stores the same fields camelCased with the three key parts inside a
composite `_id`, and matches template keys byte-wise (no collation) — culture matching stays
case-insensitive because it happens in the query service, not the store.

## Conventions

- Nullable and implicit usings are on; `GenerateDocumentationFile` is on, so **public members need
  XML docs** (`CS1591` is suppressed, but the PR template asks for them).
- Guard clauses use `ArgumentNullException.ThrowIfNull` / `ArgumentException.ThrowIfNullOrWhiteSpace`;
  options types have an internal `Validate()` called at construction so bad configuration fails at
  startup rather than on first render.
- **Primary constructors wherever a type can use one** — every service, cache, store and exception
  does. Constructor arguments are guarded in the field initialiser instead
  (`= store ?? throw new ArgumentNullException(nameof(store))`), and options are unwrapped and
  validated in one expression by `TemplateOptions.Validated(options)` /
  `RelationalTemplateStore.Validated(options)`. Field initialisers run in declaration order, which is
  what lets `DistributedTemplateCache` read `options.Value` after `_options` has rejected a null.
  Keep an explicit constructor only where a primary one cannot express it: a non-public constructor
  on a public type (`TemplarBuilder`, `TemplateRaw`, `TemplateValues`, `CompiledTemplate`,
  `TemplateVariableNameComparer`), several public overloads (`TemplateRenderRequest`,
  `InMemoryTemplateStore`), or a body that runs statements (`DatabaseFactAttribute`, the two options
  classes that set an inherited `Schema`). A second constructor may still chain into the primary one,
  as `MongoTemplateStore(options, logger)` does.
- `ILogger` is always an optional constructor parameter defaulting to `NullLogger`.
- Every `await` on a library path uses `.ConfigureAwait(false)`.
- **Keep comments to a minimum.** Default to none. The only comments in this codebase explain *why* a
  non-obvious choice was made (ordinal mapping, Oracle `BindByName`, the cache generation counter) —
  add one only when the reason cannot be read off the code. No comments that restate the code, no
  section banners, no step-by-step narration. XML docs on public members are the exception and stay
  (see the `GenerateDocumentationFile` note above).
- `TemplateDefinition` is a record — new languages and edits are expressed with `with`.
- **The eight samples share no code, on purpose — do not factor them together.** An earlier version
  had a `Templar.Sample.Shared` project and was rejected as hard to follow: each sample is now one
  self-contained `Program.cs` (wiring → seeding → endpoints → request bodies → seed data) plus a
  `.csproj`, `appsettings.json` and `launchSettings.json`. The files are ~90% identical and that is
  the point; a reader opens one file and a copier takes one directory. **A change to the API surface
  or the seed has to be applied to all eight.** The provider-specific part is the `Use…` call and the
  comment above it.
- **The samples have no UI.** `AddOpenApi()` plus `Swashbuckle.AspNetCore.SwaggerUI` serve Swagger at
  `/swagger`, `/` redirects there, and every endpoint carries `WithTags`/`WithSummary` so the page
  documents itself. `Templar.Sample.InMemory` (port 5000) is the smoke test that needs no database;
  `MemoryCache` and `DistributedCache` each declare their own `CountingTemplateStore` and expose
  `GET /api/cache/stats`. Render values are free-form JSON (`Dictionary<string, JsonElement>`), so the
  JSON type is the value's type — no string coercion heuristics.
- `Microsoft.OpenApi` is pinned to 2.11.0 in `Directory.Packages.props` because
  `Microsoft.AspNetCore.OpenApi` asks for 2.0.0, which carries a high-severity advisory. Transitive
  pinning is on, so nothing references it directly and no shipped package is affected.
- `samples/docker-compose.yml` starts each engine with the credentials its sample's `appsettings.json`
  already expects, keyed by profiles that are the lowercased sample names (`make up SAMPLE=SqlServer`).
  The tracked `launchSettings.json` files carry the per-sample ports README documents.
- `docs/reference.md` carries user-facing behaviour in detail; update it in the same change as any
  public API or option change, and README too if the change touches installing or first render.
