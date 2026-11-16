# MyDmsVn-Vps

Independent Windows Desktop + Linux VPS modernization of MyDmsVn, starting from a one-time, immutable source snapshot. This repository has its own history, governance, roadmap, CI and acceptance evidence; it is **not** a fork or a synchronized upstream mirror.

## Approved product direction (2026-11-16)

- **OWN-VPS-01:** Windows Desktop `net48` speaks HTTPS/JSON to a future Linux VPS HTTP host on ASP.NET Core 10 (Kestrel, systemd and reverse proxy).
- **OWN-VPS-02:** SQL Server remains the data store. Future FluentMigrator conversion must support both fresh databases and verified existing databases.
- **OWN-VPS-03:** Only the Desktop `net48` client is retained; Desktop.AppCore is excluded.

See [Owner decisions](docs/decisions/README.md), [target architecture](docs/architecture/overview.md), and the [single authoritative roadmap status](docs/roadmap/README.md).

## Current baseline, not the target

VPS-00A imported source and vendor assets without changing behavior. **At VPS-00B, the Server Domain, Application, Infrastructure, HttpHost, DbMigrator, Desktop and main test projects still target `net48`**. The HttpHost still uses OWIN/Web API 2 and Newtonsoft.Json; DbMigrator still uses DbUp; Desktop still has a Local adapter and references Server assemblies. No Linux runtime, HTTP parity, deployment or production-readiness claim is made.

The SDK selected by the root `global.json` is `8.0.400` with `latestFeature` roll-forward, a build tool only. The intended .NET 10 SDK `10.0.404` and Server `net10.0` arrive in VPS-04 after Desktop HTTP cutover. Do not prematurely retarget this baseline.

## Build and test on Windows

Requires Windows with Visual Studio 2022/.NET Framework 4.8 targeting pack, .NET SDK selected by `global.json`, and NuGet access. From repository root:

```powershell
dotnet --info
dotnet restore src/MyDmsVn.Desktop.App/MyDmsVn.Desktop.App.csproj --locked-mode
dotnet build src/MyDmsVn.Desktop.App/MyDmsVn.Desktop.App.csproj -c Release -f net48 --no-restore
dotnet restore src/MyDmsVn.Server.HttpHost/MyDmsVn.Server.HttpHost.csproj --locked-mode
dotnet build src/MyDmsVn.Server.HttpHost/MyDmsVn.Server.HttpHost.csproj -c Release -f net48 --no-restore
dotnet restore src/MyDmsVn.Server.DbMigrator/MyDmsVn.Server.DbMigrator.csproj --locked-mode
dotnet build src/MyDmsVn.Server.DbMigrator/MyDmsVn.Server.DbMigrator.csproj -c Release -f net48 --no-restore
```

The .NET Framework runtime and GUI are Windows-only. Linux CI in VPS-00B executes **static repository validation, not a Server runtime build**. See [build/CI guide](docs/build/build-and-ci.md) and [test policy](docs/testing/test-policy.md) for the exact supported gates. No SQL Server integration test is implicitly satisfied by a build or skipped test.

## Repository map

- `src/` — production snapshot; `src/MyDmsVn.Desktop.Infrastructure.Local` is transitional.
- `tests/` — legacy tests; [current test entry point](tests/README.md).
- `vendor/MyDmsVn.WinFormsKit/` — local source snapshot including SourceGrid, not submodules.
- `docs/import/` — immutable snapshot provenance and source-integrity evidence.
- `docs/decisions/`, `docs/architecture/`, `docs/roadmap/`, `docs/testing/`, `docs/evidence/` — independent project governance.
- `.github/workflows/vps-foundation.yml` — VPS-00B static and Windows baseline gates.
- `scripts/verify-vps-foundation.py` — cross-platform static path, project and document sanity checker.

Do not use historic source-repository PASS results or exceptions to authorize a release of MyDmsVn-Vps.
