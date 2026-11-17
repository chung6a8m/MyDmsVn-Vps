# VPS-04A — ASP.NET Core 10 replatform (implementation ledger)

Baseline: MyDmsVn-Vps `main` at `2685f87fcc52f62e839d4c3abe771ae6e5a680dc` (VPS-03 PR #6 merged).
Work branch: `codex/vps-04a-net10-aspnetcore`. **Status: IN PROGRESS; no acceptance claimed.**

## Implemented in the branch

- A separately runnable ASP.NET Core `net10.0` host under `src/MyDmsVn.Server.AspNetCoreHost`, with nested `global.json` pin `10.0.404`.
- Server Contracts, SharedKernel, Domain, Application, Infrastructure use `-p:Vps04Server=true` for `net10.0` while their ordinary Windows/legacy path remains unchanged `net48`. This is a **transition mechanism**, not the final TFMs.
- Source-links the existing WEB2-WEB6 controllers, token validation, strict wire parsing and DTO codecs. `Newtonsoft.Json`, `MediatR` and DbUp remain unchanged. Native ASP.NET Core endpoint registration emits exactly 57 source-frozen route signatures, with a fail-closed count/duplicate check.
- Kestrel request-scoped DI; TLS origin and forwarded-header boundary checks, strict Origin/CSRF checks, 1 MiB bounded read, correlation, Bearer JWT plus SQL session admission, JSON error envelope and HttpResponseMessage write-back. Startup performs the same SQL schema, principal and superuser validation before listening.
- New Linux CI workflow for `net10.0` locked restore/Release build, 363 Server.Application xUnit v3 regressions, initially 17 native ASP.NET Core HTTP boundary regressions (review rounds add at least 7 more), and compiled 57-route contract inventory. Existing net48 test/build workflows remain independent.

## Deliberate temporary boundaries

- `System.Web.Http` and `Microsoft.Owin` in the **new host source** are locally defined compile-time controller/request facades. The net10 project does **not** depend on those legacy runtime packages. They are removed in VPS-04B when routing, middleware, audit and hosting boundaries are finalized.
- Protected WEB6 audit storage currently enforces Windows file ACL semantics. The new host **refuses startup** when that path is configured: WEB6 mode is not admitted on Linux. Portable durable audit is a separate VPS-04B gate.
- No Linux reverse proxy trust, SPA static hosting, systemd, deployment, production readiness or live SQL runtime acceptance is claimed.
- The net10 graph now uses eight committed `packages.vps04a.lock.json` snapshots, separately from immutable `packages.lock.json` for net48. Linux CI executes **`--locked-mode`** for Host and both net10 test projects, with no downgrade of the legacy graph.
- Existing SQL integration suites are `net48` and cannot be counted as Linux net10 test execution. The compiled 57-route check is structural parity, **not** a proof of response/session/wire behavior.

## Reproduce the current SQL-free gate

```bash
cd src/MyDmsVn.Server.AspNetCoreHost
dotnet --version                 # exactly 10.0.404
dotnet restore MyDmsVn.Server.AspNetCoreHost.csproj -p:Vps04Server=true --locked-mode
dotnet build MyDmsVn.Server.AspNetCoreHost.csproj -p:Vps04Server=true -c Release -f net10.0 --no-restore -warnaserror
dotnet restore ../../tests/MyDmsVn.Server.Application.Tests/MyDmsVn.Server.Application.Tests.csproj -p:Vps04Server=true --locked-mode
dotnet test ../../tests/MyDmsVn.Server.Application.Tests/MyDmsVn.Server.Application.Tests.csproj -p:Vps04Server=true -c Release -f net10.0 --no-restore
dotnet restore ../../tests/MyDmsVn.Server.AspNetCoreHost.Tests/MyDmsVn.Server.AspNetCoreHost.Tests.csproj -p:Vps04Server=true --locked-mode
dotnet test ../../tests/MyDmsVn.Server.AspNetCoreHost.Tests/MyDmsVn.Server.AspNetCoreHost.Tests.csproj -p:Vps04Server=true -c Release -f net10.0 --no-restore
dotnet run --project MyDmsVn.Server.AspNetCoreHost.csproj -p:Vps04Server=true -c Release --no-build -- --list-routes > ../../vps04a-routes.json
cd ../..
python3 scripts/verify-vps-04a.py vps04a-routes.json
```

## CI findings and fixes

- Initial Linux run #44038427435 failed restore (`NU1005` because an existing net48 lock conflicted with `RestorePackagesWithLockFile=false`), NuGet downgrade (`NU1109` for ProtectedData) and the audited PKCS vulnerability (`NU1902`, `NU1903`). The subsequent commit segregates generated lock paths by graph and conditionally pins patched PKCS `10.0.13` and ProtectedData `10.0.13` for the net10 graph only. Root legacy net48 pins remain unchanged. These blockers are resolved in [Linux run #44040119428](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44040119428), commit `67b7f5db`: SDK 10.0.404 build, `--locked-mode`, 363/363 Server.Application, 17/17 HTTP boundary tests and 57/57 compiled routes, all zero failed/skipped. Subsequent commits must rerun exact-HEAD CI.

## Independent review findings (2026-11-17)

- **R1 / P1 — unknown API path with a dot:** The default ASP.NET Core `MapFallback` uses `{*path:nonfile}` and skips paths like `/api/v1/unknown.json`, yielding a bare 404 instead of the frozen JSON `NotFound` envelope. The review-fix commit explicitly maps `{**path}`; two SQL-free regressions verify catch-all registration and fallback JSON content.
- **R2 / P1 — escaped path delimiter corruption:** Building a legacy `HttpRequestMessage.RequestUri` by concatenating decoded `Request.Path.Value` can reinterpret `%3F` and `%23` as query/fragment separators. The review-fix commit uses ASP.NET Core `GetEncodedUrl()` and tests escaped path delimiters with an independent query string.
- **R3 / P1 — Production DI scope validation regression:** The legacy OWIN host explicitly enforced `ValidateScopes=true` in all environments. ASP.NET Core defaults this check off in Production, potentially allowing a singleton to capture a request-scoped identity/correlation dependency. The review-fix sets `ValidateScopes=true` explicitly for the new host and adds a Production-host regression that rejects the invalid lifetime graph.
- **R4 / P2 — outdated evidence ledger:** Four workflows completed successfully on the pre-review candidate `a1e1e415`: Linux net10, Windows legacy foundation, VPS-01 discovery, and VPS-03 publish. Earlier narrative still requested those checks at `9006843c`. The new review-fix HEAD requires a separate exact-SHA rerun.
- These are **code/structural gates** only; they do not supply the missing live Linux HTTPS/SQL, 178 JSON corpus, JWT/session, or idempotency acceptance. No merge or production approval is inferred.

## Independent review — round 2 (2026-11-17)

- **R5 / P1 — wrong HTTP method returned a 404:** The catch-all API fallback matches unknown verbs even when the URL belongs to a registered route, whereas the original Web API 2 error boundary returns JSON 405 `Request.Invalid`. The new ASP.NET Core host registers a lower-priority method-miss handler for each known route template, with `Allow` header and legacy JSON envelope. Tests check endpoint registration, 405 serialization, and live loopback Kestrel routing for 200/404/405 without SQL.
- **R6 / P2 — ignored `Route.Order` and weak parity inventory:** The bridge recorded Web API 2 route priority but dropped it during `MapMethods`, and the 57-route CI comparison checked only verb/path, not controller/action identity. Registered endpoint order now matches the source attribute; the compiler inventory gate compares exact verb/path/controller/action tuples.
- These additions remain SQL-free routing and source-identity checks. They do **not** discharge the separately required live Linux HTTPS/SQL, session/authorization, 178 frozen payload and idempotency/concurrency acceptance.

## Round-2 CI test compilation correction

- Initial round-2 Linux run ([#44044860855](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44044860855), `e5fa09bc`) compiled the ASP.NET Core host and passed 363 Application tests, but native tests did not compile because `HttpResponseHeaders.Allow` does not exist.
- The next run ([#44045089585](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44045089585), `fa5de225`) compiled and executed 24 native tests: 23 passed, and the real Kestrel method-miss test reached the expected HTTP 405 before its `GetValues("Allow")` assertion failed because .NET `HttpClient` classifies `Allow` under `HttpContentHeaders`. The test now uses `wrongVerb.Content.Headers.Allow` as documented. These were test-harness findings; neither run proves live HTTPS/SQL application parity. Latest-HEAD rerun is required.

## Evidence and remaining acceptance gates

- Source/graph build and Linux locked-restore PASS at commit `67b7f5db` ([run #44040119428](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44040119428)), including exact compiler and xUnit TRX counts.
- Windows net48 baseline history: an early architecture smoke failure was corrected at `9006843c`. Four workflows then passed on the round-1 review HEAD `c3055e65`: [Linux net10](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44043184676), [Windows foundation 8/8](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44043184610), [xUnit v3 discovery 10/10](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44043184707), [Desktop publish](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44043184614). Any later code change requires its own HEAD-specific CI.
- The new HTTP boundary tests are SQL-free unit-level requests; no real Linux Kestrel/API/SQL or payload/idempotency oracle is claimed.


| Gate | Status | Required evidence |
| --- | --- | --- |
| Independent net48 CI + xUnit v3 smoke/discovery | PASS at round-1 `c3055e65`; round-2 HEAD must be checked separately | Full Windows CI and discovery all PASS on latest HEAD |
| Linux SDK 10.0.404 server graph / ASP.NET Core build | PASS at round-1 `c3055e65`; round-2 HEAD must be checked separately | [Linux run #44043184676](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44043184676) |
| Native compiled 57/57 route map | PASS at round-1 `c3055e65`; round-2 adds controller/action parity | Runner-generated JSON + immutable 29+28 manifest comparison |
| Net10 packages locked | PASS at round-1 `c3055e65`; round-2 HEAD must be checked separately | Eight versioned net10 locks, `--locked-mode`, no NuGet audit downgrade |
| Net10 xUnit v3 regressions / HTTP perimeter | PASS at round-1 `c3055e65`; round-2 HEAD must be checked separately | Server.Application 363/363; native boundary floor raised to 24 with SQL-free loopback HTTP, no failed/skipped |
| Live Linux Kestrel HTTPS with approved SQL schema | NOT RUN | Real SQL and HTTP tests, exact revision |
| JWT/login/refresh/logout/authorization/idempotency & 178 frozen JSON corpora | NOT RUN on net10 | Independent Linux HTTP/SQL oracle |
| VPS-04B portable audit and deployment security | OUT OF SCOPE | Separate increment |

Do not mark `VPS-04A COMPLETE` before its required build, CI, parity and test-coverage gates are supported by exact final HEAD evidence. Do not merge or enable production without independent review/acceptance.
