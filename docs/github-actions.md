# GitHub Actions setup

One workflow — [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) — covers both pipelines:

| Trigger | `build-test` | `publish` |
| --- | --- | --- |
| PR opened / updated against `main` | ✅ restore, build, test, pack | ⛔ skipped |
| Push to `main` (including a merged PR) | ✅ | ✅ push to nuget.org + GitHub release |
| Manual **Run workflow** on `main` | ✅ | ✅ |

`publish` has `needs: build-test`, so nothing reaches nuget.org unless the build and the full test
suite are green first. The packages `publish` uploads are the exact `.nupkg` files `build-test`
produced — they are passed as an artifact, not rebuilt.

## One-time configuration

### 1. Create a nuget.org API key

1. Sign in at [nuget.org](https://www.nuget.org) → avatar → **API Keys** → **Create**.
2. Key name: `templar-github-actions`.
3. Scopes: **Push** → *Push new packages and package versions*.
4. Glob pattern: `Templar.*` — this covers all seven packages and nothing else.
5. Expiry: 365 days (the maximum). Note the date; the key must be rotated before it lapses.
6. **Create**, then **Copy** — the value is shown only once.

> The first version of a package ID cannot be pushed by a glob-scoped key on some accounts. If the
> initial push is rejected with *"The package does not exist"*, either reserve the `Templar.`
> prefix ([ID prefix reservation](https://learn.microsoft.com/en-us/nuget/nuget-org/id-prefix-reservation))
> or push each package once manually with `dotnet nuget push`, then let CI handle every version
> after that.

### 2. Store the key as a secret

The workflow reads `secrets.NUGET_API_KEY` from an environment named `nuget`:

- Repo → **Settings** → **Environments** → **New environment** → name it `nuget`.
- **Add environment secret** → name `NUGET_API_KEY`, value = the key from step 1.

Using an environment rather than a plain repository secret means you can also tick **Required
reviewers** there, which pauses every release for a manual approval click. Leave it unticked for
fully automatic publishing.

If you would rather not use an environment at all, add `NUGET_API_KEY` under **Settings** →
**Secrets and variables** → **Actions** and delete the `environment: nuget` line from the `publish`
job.

### 3. Allow the workflow to create releases

**Settings** → **Actions** → **General** → **Workflow permissions** → select
**Read and write permissions**. The `publish` job needs this to push the `v<version>` tag and the
GitHub release. (The job already requests `contents: write`, but the repo-level setting must permit
it.)

### 4. Protect `main`

**Settings** → **Branches** → **Add branch ruleset** for `main`:

- **Require a pull request before merging**
- **Require status checks to pass** → add `Build & test`

That check name is the `name:` of the `build-test` job. It only appears in the picker after the
workflow has run at least once, so push the workflow to `main` first, then add the rule.

## How a release happens

Publishing is driven by `<Version>` in [`Directory.Build.props`](../Directory.Build.props) — the
single place the version lives for all seven packages:

```xml
<PropertyGroup>
  <Version>1.0.0</Version>
```

The push step uses `--skip-duplicate`, so:

- **Version unchanged** → nuget.org already has it, the push is a silent no-op, no tag, no release.
  Every ordinary merge to `main` therefore lands safely without a release.
- **Version bumped** → all seven packages publish, tag `v<version>` is created, and a GitHub
  release is cut with generated notes and the `.nupkg` files attached.

So the release flow is: bump `<Version>` in your PR, merge it, done. Nothing else to trigger.

Pre-release versions work the same way — set `<Version>1.1.0-beta.1</Version>` and nuget.org lists
it as a pre-release automatically.

## What the workflow does step by step

```
restore  →  build -c Release  →  test  →  pack  →  upload nupkg artifact
                                                          ↓  (main only)
                                            download  →  push to nuget.org  →  tag + release
```

Details worth knowing:

- **.NET version** — `dotnet-version: 10.0.x`, matching `<TargetFramework>net10.0</TargetFramework>`.
  There is no `global.json`, so the runner takes the latest 10.0 SDK.
- **NuGet cache** — keyed on `Directory.Packages.props` + every `*.csproj`. Because versions are
  managed centrally, the key only changes when a dependency actually changes.
- **Test results** — a `.trx` file is uploaded as the `test-results` artifact on every run,
  including failures, so you can inspect a red build without re-running it.
- **Symbols** — `pack` also produces `.snupkg` files. `dotnet nuget push` uploads the matching
  symbol package alongside each `.nupkg` automatically.
- **Concurrency** — a new commit on a PR cancels that PR's in-flight run; runs on `main` are never
  cancelled, so a release is never interrupted halfway.

## Integration tests in CI

The five `DatabaseIntegrationTests` skip themselves unless a connection string is present, so CI
currently runs the 74 unit tests and skips those 5. To exercise real servers, give the `Test` step
a connection string for each engine you want covered — the tests pick up whatever is set and leave
the rest skipped:

```yaml
      - name: Test
        env:
          TEMPLAR_POSTGRES: ${{ secrets.TEMPLAR_POSTGRES }}
          TEMPLAR_MYSQL: ${{ secrets.TEMPLAR_MYSQL }}
          TEMPLAR_SQLSERVER: ${{ secrets.TEMPLAR_SQLSERVER }}
          TEMPLAR_ORACLE: ${{ secrets.TEMPLAR_ORACLE }}
          TEMPLAR_MONGO: ${{ secrets.TEMPLAR_MONGO }}
        run: dotnet test Templar.slnx --configuration Release --no-build
```

Store each one as a repository secret pointing at a server the runner can reach — a scratch
database on your own infrastructure, a managed test instance, or a self-hosted runner that already
has the engines installed. The tests create and drop their own `notification_templates_it` table, so
the account needs DDL permission but no pre-existing schema.

Secrets are not exposed to workflow runs triggered by forked pull requests, so those runs simply
keep skipping the integration tests. If you want the coverage to gate merges, run it in a separate
job on `main` (or on `pull_request_target` with the usual care) rather than in the main build.

## Testing changes to the workflow

- **Lint before pushing** — `brew install act` then `act --list`, or the *GitHub Actions* VS Code
  extension for schema validation.
- **Dry-run the publish path** — trigger it manually from **Actions** → **CI** → **Run workflow**
  on `main`, with `<Version>` left unchanged. Everything runs, and `--skip-duplicate` makes the push
  itself a no-op.
- **Reproduce a run locally** — the exact commands CI runs:

  ```bash
  dotnet restore Templar.slnx
  dotnet build   Templar.slnx --configuration Release --no-restore
  dotnet test    Templar.slnx --configuration Release --no-build
  dotnet pack    Templar.slnx --configuration Release --no-build --output artifacts \
                 -p:ContinuousIntegrationBuild=true -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg
  ```

## Troubleshooting

| Symptom | Cause |
| --- | --- |
| `error: Response status code 403` on push | Key expired, or its glob doesn't match `Templar.*`. Recreate the key and update the secret. |
| Push succeeds but nuget.org lists nothing new | `<Version>` was not bumped — `--skip-duplicate` swallowed it. Expected behaviour. |
| `Tag v1.0.0 already exists` in the log | Same cause; the step exits 0 on purpose so the run stays green. |
| `publish` job never runs | It is skipped on `pull_request` by design. It runs on the push that follows the merge. |
| `Resource not accessible by integration` on the tag step | Workflow permissions are read-only — see step 3. |
| Packages warn `NU5017`/missing licence on nuget.org | Add `PackageLicenseExpression`, `PackageProjectUrl`, `RepositoryUrl` and a `PackageReadmeFile` to `Directory.Build.props`. Not required to publish, but nuget.org shows the listing as incomplete. |
| `NU1902`/`NU1903` warnings during restore | Known advisories on MongoDB.Driver's optional compression dependencies. Documented in `Directory.Packages.props`; they are warnings, and `TreatWarningsAsErrors` is `false`, so the build stays green. |
