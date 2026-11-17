# VPS-04A — ASP.NET Core 10 replatform (implementation ledger)

Baseline: MyDmsVn-Vps `main` at `2685f87fcc52f62e839d4c3abe771ae6e5a680dc` (VPS-03 PR #6 merged).
Work branch: `codex/vps-04a-net10-aspnetcore`. **Status: IN PROGRESS; no acceptance claimed.**

## Implemented in the branch

- A separately runnable ASP.NET Core `net10.0` host under `src/MyDmsVn.Server.AspNetCoreHost`, with nested `global.json` pin `10.0.404`.
- Server Contracts, SharedKernel, Domain, Application, Infrastructure use `-p:Vps04Server=true` for `net10.0` while their ordinary Windows/legacy path remains unchanged `net48`. This is a **transition mechanism**, not the final TFMs.
- Source-links the existing WEB2-WEB6 controllers, token validation, strict wire parsing and DTO codecs. `Newtonsoft.Json`, `MediatR` and DbUp remain unchanged. Native ASP.NET Core endpoint registration emits exactly 57 source-frozen route signatures, with a fail-closed count/duplicate check.
- Kestrel request-scoped DI; TLS origin and forwarded-header boundary checks, strict Origin/CSRF checks, 1 MiB bounded read, correlation, Bearer JWT plus SQL session admission, JSON error envelope and HttpResponseMessage write-back. Startup performs the same SQL schema, principal and superuser validation before listening.
- New Linux CI workflow for compiled route inventory and `net10.0` restore/Release build. Existing net48 test/build workflows remain independent.

## Deliberate temporary boundaries

- `System.Web.Http` and `Microsoft.Owin` in the **new host source** are locally defined compile-time controller/request facades. The net10 project does **not** depend on those legacy runtime packages. They are removed in VPS-04B when routing, middleware, audit and hosting boundaries are finalized.
- Protected WEB6 audit storage currently enforces Windows file ACL semantics. The new host **refuses startup** when that path is configured: WEB6 mode is not admitted on Linux. Portable durable audit is a separate VPS-04B gate.
- No Linux reverse proxy trust, SPA static hosting, systemd, deployment, production readiness or live SQL runtime acceptance is claimed.
- The net10 graph uses separately located generated net10 lock files in `obj/vps04a.packages.lock.json` to avoid overwriting immutable net48 locks. Commit dedicated net10 lock snapshots, add a Linux `--locked-mode` gate and validate drift before declaring VPS-04A complete.
- Existing SQL integration suites are `net48` and cannot be counted as Linux net10 test execution. The compiled 57-route check is structural parity, **not** a proof of response/session/wire behavior.

## Reproduce the current SQL-free gate

```bash
cd src/MyDmsVn.Server.AspNetCoreHost
dotnet --version                 # exactly 10.0.404
dotnet restore MyDmsVn.Server.AspNetCoreHost.csproj -p:Vps04Server=true
dotnet build MyDmsVn.Server.AspNetCoreHost.csproj -p:Vps04Server=true -c Release -f net10.0 --no-restore -warnaserror
dotnet run --project MyDmsVn.Server.AspNetCoreHost.csproj -p:Vps04Server=true -c Release --no-build -- --list-routes > ../../vps04a-routes.json
cd ../..
python3 scripts/verify-vps-04a.py vps04a-routes.json
```

## CI findings and fixes

- Initial Linux run #44038427435 failed restore (`NU1005` because an existing net48 lock conflicted with `RestorePackagesWithLockFile=false`), NuGet downgrade (`NU1109` for ProtectedData) and the audited PKCS vulnerability (`NU1902`, `NU1903`). The subsequent commit segregates generated lock paths by graph and conditionally pins patched PKCS `10.0.13` and ProtectedData `10.0.13` for the net10 graph only. Root legacy net48 pins remain unchanged. None of these fixes is counted as PASS until a new run confirms.

## Acceptance ledger (not yet fulfilled)

| Gate | Status | Required evidence |
| --- | --- | --- |
| Independent net48 CI + xUnit v3 smoke/discovery | NOT VERIFIED on final branch HEAD | Windows CI with actual pass/fail/skip counts |
| Linux SDK 10.0.404 server graph / ASP.NET Core build | NOT VERIFIED on final branch HEAD | Linux Actions run/commit SHA |
| Native compiled 57/57 route map | NOT VERIFIED on final branch HEAD | GitHub Actions generated JSON and static comparison |
| Net10 packages locked | OPEN | Linux lockfile(s) and `--locked-mode` |
| Live Linux Kestrel HTTPS with approved SQL schema | NOT RUN | Real SQL and HTTP tests, exact revision |
| JWT/login/refresh/logout/authorization/idempotency & 178 frozen JSON corpora | NOT RUN on net10 | Independent Linux HTTP/SQL oracle |
| VPS-04B portable audit and deployment security | OUT OF SCOPE | Separate increment |

Do not mark `VPS-04A COMPLETE` before its required build, CI, parity and test-coverage gates are supported by exact final HEAD evidence. Do not merge or enable production without independent review/acceptance.
