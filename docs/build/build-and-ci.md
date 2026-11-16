# Build foundation — commands and CI scopes

## Local prerequisites

- Windows: Visual Studio 2022 or Build Tools with **.NET Framework 4.8 reference assemblies** and MSBuild, plus .NET SDK from root `global.json` (8.0.400/latestFeature). A dotnet 10 runtime is neither necessary nor proof of VPS-04.
- Linux: Python 3 and Git for **static validation only** in VPS-00B. The server remains `net48` and cannot be treated as ASP.NET Core Linux executable.
- NuGet restored from root `NuGet.config` with pinned lock files. No local SQL Server or admin credentials are assumed for compile.

## Static guard (both local and CI)

```bash
python3 scripts/verify-vps-foundation.py --report foundation-report.json
```

The checker validates 44 tracked C# projects, solution project targets, resolved in-tree ProjectReferences, required docs, 13 SQL scripts and absence of git submodules/gitlinks. A green static check is **not** a build or unit test.

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
- `windows-smoke` — Windows 2022, SQL-free xUnit v2 smoke tests from named projects, TRX artifacts and nonempty executed-count guard. It does not run SQL integration or attended GUI tests.

No job may hide failure through `continue-on-error`. CI must be evaluated on the **last** PR HEAD after fixes. If a baseline restore or compilation problem exists, preserve red status and document exact error, owner and follow-up; do not label it PASS. Results are recorded in [VPS-00B evidence](../evidence/vps-00b-baseline.md). Later increments introduce Linux net10 build/test lanes only once real net10 project targets exist.

## Boundaries of automated safety

SQL integrations require explicit `MYDMSVN_TEST_SQLSERVER_CONNECTION_STRING` for a non-production instance; absent environment means NOT RUN. GUI native focus/125%-scaling needs attended Windows and is NOT RUN by headless CI. Runtime Linux hosting, security, migration and recovery gates are all future work.
