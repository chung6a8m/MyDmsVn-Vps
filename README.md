# MyDmsVn-Vps

Independent Windows Desktop + Linux VPS modernization of MyDmsVn, starting from a one-time, immutable source snapshot. This repository has its own history, governance, roadmap, CI and acceptance evidence; it is **not** a fork or a synchronized upstream mirror.

## Approved product direction (2026-11-16)

- **OWN-VPS-01:** Windows Desktop `net48` speaks HTTPS/JSON to a future Linux VPS HTTP host on ASP.NET Core 10 (Kestrel, systemd and reverse proxy).
- **OWN-VPS-02:** SQL Server remains the data store. Future FluentMigrator conversion must support both fresh databases and verified existing databases.
- **OWN-VPS-03:** Only the Desktop `net48` client is retained; Desktop.AppCore is excluded.

See [Owner decisions](docs/decisions/README.md), [target architecture](docs/architecture/overview.md), and the [single authoritative roadmap status](docs/roadmap/README.md).

## Current implementation stage: VPS-03 (draft PR, not production acceptance)

VPS-00A/B established the independent source/build baseline. VPS-01 added executable xUnit v3 and frozen contracts; VPS-02 completed separately evidenced SQL-backed HTTP API parity. **Draft [VPS-03 PR #6](https://github.com/chung6a8m/MyDmsVn-Vps/pull/6)** now changes production `Desktop.App` (Windows `net48`) to use `Desktop.Infrastructure.Http` for HTTPS/JSON, not Local/Server or direct SQL. The legacy Local adapter remains in source for old tests. The new [published artifact CI](.github/workflows/vps-03-desktop-artifact.yml) validates net48 x86 output boundaries.

The **Server Domain/Application/Infrastructure/HttpHost/DbMigrator remain net48** in VPS-03; HttpHost is still OWIN/Web API 2 and uses Newtonsoft.Json and MediatR, and DbMigrator still uses DbUp. **Actual Desktop → HTTPS → SQL E2E and restart-safe recovery remain open** in [Issue #8](https://github.com/chung6a8m/MyDmsVn-Vps/issues/8) and [Issue #7](https://github.com/chung6a8m/MyDmsVn-Vps/issues/7). Do not treat implementation CI as VPS-03 COMPLETE or production approval.

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

Running `Desktop.App` (not merely compiling it) requires `MYDMSVN_DESKTOP_API_ORIGIN` set to the deployed canonical HTTPS origin and `MYDMSVN_DESKTOP_OPERATION_MODE` set to the actual host mode (`legacy-web11`, `web4`, or `web5`). The additive host feature API still requires `MYDMSVN_WEB_DESKTOP_API=enabled`; do not enable it on production without the accepted SQL/permission/GUI gates.

The .NET Framework runtime and GUI are Windows-only. Linux CI currently executes **static repository validation, not a Server runtime build**. See [build/CI guide](docs/build/build-and-ci.md) and [test policy](docs/testing/test-policy.md) for the exact supported gates. No SQL Server integration test is implicitly satisfied by a build or skipped test.

## Repository map

- `src/` — production source; `src/MyDmsVn.Desktop.Infrastructure.Http` is the active Desktop adapter, with `Infrastructure.Local` retained solely for legacy tests.
- `tests/` — legacy tests; [current test entry point](tests/README.md).
- `vendor/MyDmsVn.WinFormsKit/` — local source snapshot including SourceGrid, not submodules.
- `docs/import/` — immutable snapshot provenance and source-integrity evidence.
- `docs/decisions/`, `docs/architecture/`, `docs/roadmap/`, `docs/testing/`, `docs/evidence/` — independent project governance.
- `.github/workflows/vps-foundation.yml` — Linux static and Windows net48 checks; `.github/workflows/vps-03-desktop-artifact.yml` — Desktop x86 publish, package and binary boundary audit.
- `scripts/verify-vps-foundation.py` — cross-platform static path, project and document sanity checker.

Do not use historic source-repository PASS results or exceptions to authorize a release of MyDmsVn-Vps.
