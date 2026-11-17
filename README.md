# MyDmsVn-Vps

Independent Windows Desktop + Linux VPS modernization of MyDmsVn, starting from a one-time, immutable source snapshot. This repository has its own history, governance, roadmap, CI and acceptance evidence; it is **not** a fork or a synchronized upstream mirror.

## Approved product direction (2026-11-16)

- **OWN-VPS-01:** Windows Desktop `net48` speaks HTTPS/JSON to a future Linux VPS HTTP host on ASP.NET Core 10 (Kestrel, systemd and reverse proxy).
- **OWN-VPS-02:** SQL Server remains the data store. Future FluentMigrator conversion must support both fresh databases and verified existing databases.
- **OWN-VPS-03:** Only the Desktop `net48` client is retained; Desktop.AppCore is excluded.

See [Owner decisions](docs/decisions/README.md), [target architecture](docs/architecture/overview.md), and the [single authoritative roadmap status](docs/roadmap/README.md).

## Current implementation stage: VPS-04A — ASP.NET Core 10 Linux host (PR #9 review)

VPS-00A/B established the independent baseline, VPS-01 executable xUnit v3, and VPS-02 SQL-backed HTTP parity. [VPS-03 PR #6](https://github.com/chung6a8m/MyDmsVn-Vps/pull/6) **merged** to `main` at `2685f87` on 2026-11-17. Desktop remains Windows `net48`, using `Desktop.Infrastructure.Http` via HTTPS/JSON rather than direct SQL; the Local adapter is retained for legacy tests.

[VPS-04A PR #9](https://github.com/chung6a8m/MyDmsVn-Vps/pull/9) introduces the separate `src/MyDmsVn.Server.AspNetCoreHost` **ASP.NET Core `net10.0`** host and conditional `net10.0` Server graph (`-p:Vps04Server=true`). Its nested `global.json` pins SDK **10.0.404**, whereas the root `global.json` remains the Windows/legacy SDK 8.0.400 (latestFeature). The legacy `net48` HttpHost and DbMigrator still build independently. Newtonsoft.Json, MediatR and DbUp are intentionally retained pending VPS-05/06/07.

PR #9 has bounded, disposable Linux HTTPS→SQL13 acceptance: **15/15 real SQL-backed tests**, **24 selected WEB2–WEB6 expected/actual/diff scenarios**, and **57/57 compiled routes**, plus Windows net48 and xUnit CI. The 178 frozen corpus files are source pins, **not 178 live HTTP response tests**. Protected staging is **NOT RUN**, and positive WEB6 portable audit, exhaustive route/value parity, VPS deployment and release approval are **NOT VERIFIED**.

Subsequent increments: [VPS-04B protected audit/security](https://github.com/chung6a8m/MyDmsVn-Vps/issues/10), [VPS-08 staging/deployment](https://github.com/chung6a8m/MyDmsVn-Vps/issues/11), and [VPS-09 integrated acceptance](https://github.com/chung6a8m/MyDmsVn-Vps/issues/12). Review exact HEAD and CI in [PR #9](https://github.com/chung6a8m/MyDmsVn-Vps/pull/9); [roadmap](docs/roadmap/README.md) is the sole live status authority. No production-readiness claim.

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

The .NET Framework runtime and GUI are Windows-only. VPS-00B's Linux foundation job is static; VPS-04A runs a separate real Linux net10 build and disposable HTTPS→SQL CI. No build, discovery-only test or skipped protected staging job implies production acceptance. See the [build/CI guide](docs/build/build-and-ci.md) and [test policy](docs/testing/test-policy.md).

## VPS-04A Linux net10 SQL-free developer gate

From `src/MyDmsVn.Server.AspNetCoreHost` with SDK **10.0.404**:

```bash
dotnet --version
dotnet restore MyDmsVn.Server.AspNetCoreHost.csproj -p:Vps04Server=true --locked-mode
dotnet build MyDmsVn.Server.AspNetCoreHost.csproj -p:Vps04Server=true -c Release -f net10.0 --no-restore -warnaserror
dotnet restore ../../tests/MyDmsVn.Server.Application.Tests/MyDmsVn.Server.Application.Tests.csproj -p:Vps04Server=true --locked-mode
dotnet test ../../tests/MyDmsVn.Server.Application.Tests/MyDmsVn.Server.Application.Tests.csproj -p:Vps04Server=true -c Release -f net10.0 --no-restore
dotnet restore ../../tests/MyDmsVn.Server.AspNetCoreHost.Tests/MyDmsVn.Server.AspNetCoreHost.Tests.csproj -p:Vps04Server=true --locked-mode
dotnet test ../../tests/MyDmsVn.Server.AspNetCoreHost.Tests/MyDmsVn.Server.AspNetCoreHost.Tests.csproj -p:Vps04Server=true -c Release -f net10.0 --no-restore
```

Real SQL bootstrap, TLS and independent SQL Oracle are CI-only, described in the [VPS-04A runbook](docs/runbooks/vps-04a-selfcontained-ci.md). Do not target staging or production SQL.


## Repository map

- `src/` — production source; `Server.AspNetCoreHost` is the transitional Linux net10 entry point, `Desktop.Infrastructure.Http` is the active Windows net48 client adapter, and `Infrastructure.Local` is retained for legacy tests.
- `tests/` — net48 suites plus net10 native host tests and CI-only SQL bootstrap; [test entry point](tests/README.md).
- `vendor/MyDmsVn.WinFormsKit/` — local source snapshot including SourceGrid, not submodules.
- `docs/import/` — immutable snapshot provenance and source-integrity evidence.
- `docs/decisions/`, `docs/architecture/`, `docs/roadmap/`, `docs/testing/`, `docs/evidence/` — independent project governance.
- `.github/workflows/vps-foundation.yml` — static/Windows net48; `vps-03-desktop-artifact.yml` — Desktop publish; `vps-04a-linux.yml` and `vps-04a-ci-sql.yml` — Linux net10 and disposable HTTPS→SQL13 gates.
- `scripts/verify-vps-foundation.py` — cross-platform static path, project and document sanity checker.

Do not use historic source-repository PASS results or exceptions to authorize a release of MyDmsVn-Vps.
