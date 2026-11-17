# Build foundation — commands and CI scopes

## Local prerequisites

- Windows: Visual Studio 2022 or Build Tools with **.NET Framework 4.8 reference assemblies** and MSBuild, plus .NET SDK from root `global.json` (8.0.400/latestFeature). A dotnet 10 runtime is neither necessary nor proof of VPS-04.
- Linux VPS-00B: Python 3 and Git for its historical **static-only** foundation lane.
- Linux VPS-04A: SDK **10.0.404** from `src/MyDmsVn.Server.AspNetCoreHost/global.json` for a separate `net10.0` Server graph and ASP.NET Core host. Windows Desktop and legacy HttpHost/DbMigrator remain `net48`.
- NuGet restored from root `NuGet.config` with pinned lock files. No local SQL Server or admin credentials are assumed for compile.

## Static guard (both local and CI)

```bash
python3 scripts/verify-vps-foundation.py --report foundation-report.json
```

The checker validates 44 tracked C# projects, solution project targets, resolved in-tree ProjectReferences, literal MSBuild Import/HintPath boundaries, required docs, 13 SQL scripts, 178 frozen corpus Git blob SHAs and absence of git submodules/gitlinks. A green static check is **not** a build or unit test.

## Windows net48 compile commands

```powershell
$ErrorActionPreference = 'Stop'
$projects = @(
  'src/MyDmsVn.Desktop.App/MyDmsVn.Desktop.App.csproj',
  'src/MyDmsVn.Server.HttpHost/MyDmsVn.Server.HttpHost.csproj',
  'src/MyDmsVn.Server.DbMigrator/MyDmsVn.Server.DbMigrator.csproj'
)
foreach ($p in $projects) {
  dotnet restore $p --locked-mode
  if ($LASTEXITCODE -ne 0) { throw "Restore failed: $p" }
  dotnet build $p -c Release -f net48 --no-restore -v minimal
  if ($LASTEXITCODE -ne 0) { throw "Build failed: $p" }
}
```

This compiles Desktop, legacy Server.HttpHost and legacy DbMigrator as **separate** evidence paths. For broader code changes, additionally run `dotnet restore MyDmsVn.sln --locked-mode` followed by `dotnet build MyDmsVn.sln -c Release --no-restore` and appropriate tests.

## GitHub Actions: VPS foundation

`.github/workflows/vps-foundation.yml` runs on PRs and pushes to `main` plus manual dispatch.

- `static-foundation` — Ubuntu runner, Python checker with JSON artifact. Cross-platform repository integrity **only**.
- `windows-net48` — Windows 2022 matrix for Desktop.App, Server.HttpHost, DbMigrator. Pinned SDK and .NET Framework reference pack; locked restore; Release net48 build.
- `windows-smoke` — Windows 2022, SQL-free xUnit v2 smoke tests from named projects, TRX artifacts and nonempty fully passed / no-skipped gate. It does not run SQL integration or attended GUI tests.

No job may hide failure through `continue-on-error`. CI must be evaluated on the **last** PR HEAD after fixes. If a baseline restore or compilation problem exists, preserve red status and document exact error, owner and follow-up; do not label it PASS. Results are recorded in [VPS-00B evidence](../evidence/vps-00b-baseline.md). VPS-04A now has separate [Linux net10 source/route CI](../../.github/workflows/vps-04a-linux.yml) and [self-contained real HTTPS→SQL13 CI](../../.github/workflows/vps-04a-ci-sql.yml), independent from the historical VPS-00B static lane.

## Boundaries of automated safety

SQL integrations require explicit `MYDMSVN_TEST_SQLSERVER_CONNECTION_STRING` for a non-production instance; absent environment means NOT RUN. GUI native focus/125%-scaling needs attended Windows and is NOT RUN by headless CI. VPS-04A disposable Linux HTTPS→SQL13 tests provide bounded runtime/stock/rollback evidence, **not protected staging**. Portable WEB6 audit, VPS reverse proxy/systemd, FluentMigrator and integrated release gates remain future work.

## VPS-04A Linux net10 commands and evidence

From `src/MyDmsVn.Server.AspNetCoreHost` with pinned SDK 10.0.404: `dotnet restore MyDmsVn.Server.AspNetCoreHost.csproj -p:Vps04Server=true --locked-mode`, then `dotnet build MyDmsVn.Server.AspNetCoreHost.csproj -p:Vps04Server=true -c Release -f net10.0 --no-restore -warnaserror`. Restore and execute `tests/MyDmsVn.Server.Application.Tests` and `tests/MyDmsVn.Server.AspNetCoreHost.Tests` with `-p:Vps04Server=true -f net10.0`; the [VPS-04A evidence](../evidence/vps-04a-replatform.md) supplies the full commands. The [self-contained SQL runbook](../runbooks/vps-04a-selfcontained-ci.md) is an explicitly isolated GitHub Actions lane with verified TLS, restricted runtime and independent read-only SQL oracle. The historical protected `approved-linux-sql` job remains **SKIPPED / NOT RUN** and is deferred to [VPS-08](https://github.com/chung6a8m/MyDmsVn-Vps/issues/11).
