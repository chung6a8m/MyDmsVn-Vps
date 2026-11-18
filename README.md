# MyDmsVn-Vps

Independent Windows Desktop + Linux VPS modernization of MyDmsVn, starting from a one-time, immutable source snapshot. This repository has its own history, governance, roadmap, CI and acceptance evidence; it is **not** a fork or a synchronized upstream mirror.

## Approved product direction (2026-11-16)

- **OWN-VPS-01:** Windows Desktop `net48` speaks HTTPS/JSON to a future Linux VPS HTTP host on ASP.NET Core 10 (Kestrel, systemd and reverse proxy).
- **OWN-VPS-02:** SQL Server remains the data store. Future FluentMigrator conversion must support both fresh databases and verified existing databases.
- **OWN-VPS-03:** Only the Desktop `net48` client is retained; Desktop.AppCore is excluded.

See [Owner decisions](docs/decisions/README.md), [target architecture](docs/architecture/overview.md), and the [single authoritative roadmap status](docs/roadmap/README.md).

## Current implementation stage: VPS-08 source candidate for review; deployment NOT VERIFIED

**VPS-08 Linux deployment & hardening is implemented as [draft PR #27](https://github.com/chung6a8m/MyDmsVn-Vps/pull/27), NOT ACCEPTED.** The source candidate adds opt-in Nginx/TLS and authenticated loopback Kestrel, hardened systemd service and credentials, protected-audit provisioning, SHA-pinned Linux package/rollback, net10 CI, and a separately reviewed protected staging gate. Actual VPS/SQL13 staging, independent Oracle, restart/reboot/failure/rollback evidence, final-HEAD CI and merge are outstanding. See the [single canonical roadmap](docs/roadmap/README.md), [VPS-08 runbook](docs/runbooks/vps-08-linux-deployment.md) and [acceptance ledger](docs/evidence/vps-08-linux-deployment.md). PR #9's historical staging SKIPPED is not PASS. **VPS-09** integrated Windows Desktop→deployed Linux→SQL and production approval remain NOT VERIFIED.

**Earlier VPS-05 and VPS-06A checkpoint evidence (historical):**

**VPS-05 System.Text.Json migration is COMPLETE within its increment scope.** [VPS-05A PR #14](https://github.com/chung6a8m/MyDmsVn-Vps/pull/14), [VPS-05B PR #16](https://github.com/chung6a8m/MyDmsVn-Vps/pull/16) and [VPS-05C PR #17](https://github.com/chung6a8m/MyDmsVn-Vps/pull/17) are **merged**. PR #17 merged on 2026-11-18 03:06:44 UTC at `b4f7cb64e5474203fe0f132c39286b1d7c822e61`, after independent review and five SUCCESS workflows on its exact final reviewed HEAD `1230efdf06a60412da077ce9d85f0d44cb0b5913`.

The frozen source-independent **live net48 OWIN → net10 Kestrel SQL-free differential passes 293/293 route/variant observations covering all 57 routes**; ASP.NET Core Host **660/660**, legacy net48 Host **684/684**, and bounded disposable real HTTPS→SQL13 **15/15** passed at the same reviewed HEAD. The published production net10 Host `.deps.json` has **zero Newtonsoft.Json** runtime libraries/targets; Desktop and legacy net48 retain Json.NET. Final-HEAD workflows: [Linux net10 #44227546692](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44227546692), [disposable SQL #44227546705](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44227546705), [Windows Desktop publish #44227546706](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44227546706), [xUnit v3 discovery #44227546729](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44227546729), [legacy net48 #44227546680](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44227546680). The protected `approved-linux-sql` job was **SKIPPED by design, NOT PASS**.

**VPS-06A generator/pipeline foundation is COMPLETE within its bounded scope.** [PR #20](https://github.com/chung6a8m/MyDmsVn-Vps/pull/20) merged to `main` at `1a688c7698a7e7e98f4913771d4c967f6fe64b33` on 2026-11-18. The final independently reviewed PR HEAD `dfd8150110275898a8f60af99c2c2306f3a1b506` passed **six/six mandatory workflows**: isolated .NET 10 Mediator 3.0.2 Source Generator with committed NuGet lock and scoped internal handler, **22/22** pipeline/identity/validation/cancellation/disposal canary cases, **MSG0005/MSG0001** negative diagnostics, net10 Application **366/366**, Host **660/660**, source-independent **293/293** SQL-free live 57-route differential, disposable HTTPS→SQL13 **15/15**, Windows net48/discovery and Desktop publish. [VPS-06A evidence](docs/evidence/vps-06a-generator-foundation.md). The protected `approved-linux-sql` was **SKIPPED / NOT PASS**.

**VPS-06B production generated Mediator cutover is merged** in [PR #23](https://github.com/chung6a8m/MyDmsVn-Vps/pull/23); **VPS-07A/07B FluentMigrator foundation and journal bridge are merged** in [PR #24](https://github.com/chung6a8m/MyDmsVn-Vps/pull/24) and [PR #26](https://github.com/chung6a8m/MyDmsVn-Vps/pull/26). VPS-07B isolated SQL2022 verified-TLS acceptance passed 58/58 and 9/9 workflows at the reviewed code checkpoint; it is **not real business SQL cutover**. Consult the [canonical roadmap](docs/roadmap/README.md) and the PR timelines for exact final-HEAD evidence; prior VPS-05/06A paragraphs above are historical snapshots, not current dependency inventories.

VPS-00..03 established the independent baseline, xUnit v3 contracts, HTTP parity and Windows Desktop `net48` production HTTP cutover. [VPS-04A PR #9](https://github.com/chung6a8m/MyDmsVn-Vps/pull/9) **merged** to `main` at `b53e5ee` on 2026-11-17. The separately runnable [ASP.NET Core `net10.0` host](src/MyDmsVn.Server.AspNetCoreHost) uses SDK **10.0.404** (`-p:Vps04Server=true`). The original Windows `net48` HttpHost/DbMigrator remain separate. The net48-only Newtonsoft.Json code remains for Desktop compatibility; MediatR and DbUp transition later in VPS-06/07. VPS-05 STJ runtime isolation and wire parity have since been merged in PR #17.

[VPS-04B PR #13](https://github.com/chung6a8m/MyDmsVn-Vps/pull/13) implements protected Linux audit storage for enabled WEB6 (pre-provisioned `0700` directory, `0600` file, no-follow descriptor checks, owner/inode/mode/link checks, cross-process lock, `fsync`); ASP.NET Core request admission middleware, DI scopes and degraded-audit JSON HTTP 503 readiness. Legacy Net48 audit ACL behavior remains unchanged. [Runbook](docs/runbooks/vps-04b-protected-audit.md).

At VPS-04B code SHA `b41eb7c`, [Linux SQL-free CI](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44111184190) passed **366/366** Server.Application, **216/216** native host/audit/corpus tests and **57/57** frozen compiled routes. [Disposable real Linux HTTPS→SQL13 CI](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44111184115) passed **15/15** prior SQL tests and **3/3** new enabled WEB6 receipt/eligibility/security, durable audit and restart tests, with zero failures/skips. These are historical code-checkpoint results. **For authoritative final-HEAD CI, findings resolution, review and merge status, consult PR #13.** See [evidence ledger](docs/evidence/vps-04b-linux-security.md).

**Explicitly NOT VERIFIED:** deployment on a real VPS, trusted reverse proxy/systemd/secrets, protected staging, full 57-route dynamic **real-SQL** response parity, Windows Desktop→Linux Host→SQL release E2E, and production readiness. These remain [VPS-08 Issue #11](https://github.com/chung6a8m/MyDmsVn-Vps/issues/11) and [VPS-09 Issue #12](https://github.com/chung6a8m/MyDmsVn-Vps/issues/12). [Roadmap](docs/roadmap/README.md) remains the only live status authority.

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
