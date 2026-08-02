---
name: pack
description: Build the seven Templar NuGet packages locally, exactly the way CI does. Use when asked to pack, build the NuGet packages, produce .nupkg/.snupkg files, verify packaging before a release, or bump the release version.
---

# Pack Templar for NuGet

Reproduces the `build-test` job of `.github/workflows/build.yml` locally. Output lands in `artifacts/`
(git-ignored).

## Steps

From the repository root:

```bash
make ci          # restore → build → test → pack, in CI's order
```

Or the raw commands, if you need to vary them — each step depends on the previous one's output:

```bash
dotnet restore Templar.slnx
dotnet build   Templar.slnx --configuration Release --no-restore
dotnet test    Templar.slnx --configuration Release --no-build
dotnet pack    Templar.slnx --configuration Release --no-build --output artifacts \
               -p:ContinuousIntegrationBuild=true -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg
```

Do not skip the test step (`make pack` alone does not run it): CI gates publishing on it (`publish`
has `needs: build-test`), so a pack that has not been tested is not a release candidate.

`NU1902`/`NU1903` warnings during restore are expected — known advisories on `MongoDB.Driver`'s
optional compression dependencies, documented in `Directory.Packages.props`. They are warnings only;
`TreatWarningsAsErrors` is `false`.

## Verify the output

There must be **fourteen** files — a `.nupkg` and a `.snupkg` for each of the seven packages
(`Templar.Core`, `Templar.Relational`, `Templar.MySql`, `Templar.SqlServer`, `Templar.PostgreSql`,
`Templar.Oracle`, `Templar.Mongo`). Nothing named `Templar.Sample.*` and no `Templar.Tests` may
appear — the nine sample projects and the test project all set `IsPackable=false`.

```bash
ls -1 artifacts/
```

Every file carries the same version — the `<Version>` from `Directory.Build.props`. If a version
looks wrong, that file is where to fix it; nothing else sets a package version.

To inspect what a package actually ships:

```bash
unzip -l artifacts/Templar.Core.<version>.nupkg     # expect lib/net10.0/*.dll and *.xml docs
```

The `.xml` doc file must be present — `GenerateDocumentationFile` is on and the public API is
documented.

## Releasing

Publishing is **not** a local step. CI pushes to nuget.org on every push to `main` with
`--skip-duplicate`, so:

- Version unchanged → the push is a silent no-op, no tag, no release. Every ordinary merge is safe.
- Version bumped → all seven packages publish, tag `v<version>` is created, GitHub release is cut.

So to release: bump `<Version>` in `Directory.Build.props` in the PR, merge to `main`, done. Never
run `dotnet nuget push` by hand for a normal release. Pre-release versions work the same way
(`1.1.0-beta.1` is listed as a pre-release automatically).

Ask the user before bumping `<Version>` unless they explicitly asked for a release — a bump is what
triggers a publish.
