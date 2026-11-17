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

## Independent review — round 3 (2026-11-17)

- **R7 / P1 — stale response headers on exception:** The native boundary writes a JSON 500 if a downstream controller fails before the response starts, but previously retained pre-staged headers such as `Set-Cookie` and `Location`. A failed refresh or login action could therefore emit a credential/redirect header inconsistent with its failed outcome. The catch handler now clears all uncommitted headers and restores safe correlation/no-cache/security headers before serializing the sanitized 500. A regression proves staged cookie/redirect removal and no exception detail leakage.
- This is a SQL-free fail-closed boundary regression, not proof of database transaction or lost-ACK safety. Exact review-fix HEAD CI rerun remains mandatory.

## Evidence and remaining acceptance gates

- Source/graph build and Linux locked-restore PASS at commit `67b7f5db` ([run #44040119428](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44040119428)), including exact compiler and xUnit TRX counts.
- Windows net48 baseline history: an early architecture smoke failure was corrected at `9006843c`. Four workflows then passed on the round-1 review HEAD `c3055e65`: [Linux net10](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44043184676), [Windows foundation 8/8](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44043184610), [xUnit v3 discovery 10/10](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44043184707), [Desktop publish](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44043184614). Any later code change requires its own HEAD-specific CI.
- Round-2 review code HEAD `4bd9ba58`: four Windows/Linux workflows PASS, including [Linux 363/363 + 24/24 + 57/57](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44045329015), [Windows foundation 8/8](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44045329102), [VPS-01 discovery 10/10](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44045329002), and [Desktop publish](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44045329128).
- Round-3 review code HEAD `7770a688`: four Windows/Linux workflows PASS, including [Linux SDK 10.0.404 / 363 Application / 25 HTTP boundary / 57 routes](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44046861655), [Windows foundation 8/8](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44046861676), [VPS-01 discovery 10/10](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44046861703), and [Desktop publish](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44046861625). All test counts have zero failed/skipped. A subsequent documentation-only commit requires its own final-HEAD CI.
- These boundary tests include SQL-free Kestrel loopback, not live Linux HTTPS-to-SQL integration or payload/idempotency acceptance.


| Gate | Status | Required evidence |
| --- | --- | --- |
| Independent net48 CI + xUnit v3 smoke/discovery | VERIFIED at review-code `7770a688` | [Windows foundation 8/8](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44046861676); [discovery 10/10](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44046861703) |
| Linux SDK 10.0.404 server graph / ASP.NET Core build | VERIFIED at review-code `7770a688` | [Linux CI](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44046861655): locked restore, Release `-warnaserror` |
| Native compiled 57/57 route/action map | VERIFIED at review-code `7770a688` | Compiled HTTP verb/path/controller/action equals immutable 29+28 manifests |
| Net10 packages locked | VERIFIED at review-code `7770a688` | Eight versioned net10 locks, `--locked-mode`, no NuGet downgrade |
| Net10 xUnit v3 regressions / HTTP perimeter | VERIFIED at review-code `7770a688` | Server.Application 363/363; HTTP boundary + loopback 25/25, no failed/skipped |
| 178 WEB2–WEB6 source files and NET10 WEB4 canonical/hash vectors | REVIEW FIX APPLIED; FINAL-HEAD CI REQUIRED | New 178 per-file xUnit facts, 2 corpus/vector facts; this verifies source files and hash, not 178 live HTTP responses |
| Live Linux Kestrel HTTPS with approved exact13 SQL | NOT RUN / NOT VERIFIED | Explicit protected [live runner](../runbooks/vps-04a-live-acceptance.md) on final HEAD |
| SQL-backed JWT/login/refresh/logout/authorization, transactions, idempotency and concurrent same-key writes | NOT RUN / NOT VERIFIED | Conditional four-fact NET10 runner with SQL session/journal/business-row oracles, protected credentials |
| Real SQL injected-failure rollback, all applicable live WEB2–WEB6 payloads and WEB6 portable audit | NOT VERIFIED | Additional independent non-production gates; not established by static corpus or four live smoke facts |
| VPS-04B portable audit and deployment security | OUT OF SCOPE | Separate increment |

Do not mark `VPS-04A COMPLETE` before its required build, CI, parity and test-coverage gates are supported by exact final HEAD evidence. Do not merge or enable production without independent review/acceptance.

## VPS-04A SQL acceptance harness addition (2026-11-17; PR #9)

- Added `Vps04aFrozenCorpusTests.cs`: on the actual NET10 host graph, runs **178 separately discovered test cases** bound to immutable `corpus-blobs.sha1`, plus a full-file-count/untracked-file gate and native WEB4 canonical fingerprint comparison. The source data contains **178 tracked files**, not 178 live JSON payloads; it includes contract documentation, control vectors, scripts and manifests. Neither source fixture nor baseline hash manifest is modified.
- The first experimental Linux xUnit run at `cdac1865` was **RED**: 205 executed, 169 passed, 36 failed. The failures exposed an incorrect *test* assumption about root `.gitattributes` checkout CRLF normalization (35 original Git object hashes) and a test vector-reader shape error (`fingerprint-vectors.json` is an object with `vectors`). Review fix `adf21d07` hashes Git's LF canonical text representation and reads `vectors` explicitly. **Do not infer PASS from the fix commit**; final-HEAD CI must execute. See [the red CI run](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44049480955).
- Added conditional NET10-only `Vps04aLiveSqlAcceptanceTests.cs` (not discovered in normal SQL-free CI) and [protected manual GitHub Actions runner](../../.github/workflows/vps-04a-live-acceptance.yml). The normal Linux workflow compiles opt-in live tests with `-p:Vps04ALive=true` but **never executes them**. A live run requires approved isolated exact13 SQL with restricted principals, active seed references, two independent actors, read-only SQL oracle and valid TLS. Four facts cover login/JWT, refresh replay/logout and real SQL session revocation; concurrency/same-key journal and business-row uniqueness; invalid-body/stale-epoch no-write; authorization deny. The workflow must run on final HEAD, give 4/4 with no skip, and upload TRX. **No SQL credentials/environment were supplied in this change; the live workflow has not been dispatched and is NOT VERIFIED.**
- The HTTPS runner creates a temporary localhost TLS certificate and installs it into only the disposable Linux runner trust store. It uses real Kestrel HTTPS with ordinary TLS certificate verification (no `-k` / no trust-all), real external/isolated SQL configured through protected environment secrets, and a separate read-only SQL oracle. No production data modification or automatic DB migration is authorized.
- Limitations persist: the 178-file integrity suite does not prove full live wire replay or System.Text.Json parity; the four live tests do not inject a mid-transaction SQL failure to prove physical rollback, do not validate stock/COGS under all races, and do not test WEB6/Linux portable audit. No production, VPS-04B/VPS-05/VPS-07/VPS-08/VPS-09 acceptance is inferred.

[Operator prerequisites, evidence fields and fail-closed run instructions](../runbooks/vps-04a-live-acceptance.md) are the canonical live gate runbook. PR #9 must remain **DRAFT / NOT ACCEPTED** until independent final-HEAD review and actual Linux HTTPS→SQL acceptance provide dated evidence.

## Independent review — round 4 (2026-11-17; protected live gate)

- **R9 / P1 — protected Environment ref mismatch:** GitHub evaluates deployment policies against `GITHUB_REF=refs/pull/9/merge` for `pull_request.labeled`; the previous helper allowed only the source branch, blocking the intended approved deployment. The helper now allows **only** `refs/pull/9/merge`, migrates its known obsolete policy, rejects unknown/paginated entries and has SQL-free regression coverage. Both workflow entry points check PR #9, merge ref, target `main`, same-repository source and explicit label.
- **R10 / P1 — synthetic merge checkout masked reviewed HEAD:** By default `actions/checkout` on `pull_request` checks out the merge SHA. The protected live workflow pins to `pull_request.head.sha`, disables persisted checkout credentials, and verifies the checked-out/current PR HEAD against that SHA before starting SQL, then verifies again after the live tests. Required GitHub token permissions remain read-only.
- **R11 / P2 — stale CI prose:** pre-review HEAD `5fe36721` has four ordinary source CI workflows PASS: [Linux SDK10/363 Application/205 native/57 routes](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44053367597), [Windows foundation](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44053367778), [xUnit v3 discovery](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44053367635), [Desktop publish](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44053367711). These are **historical PASS**, not verification of this round-4 review-fix HEAD, which requires new CI.
- The SQL-free operator-helper regressions are structural simulations; they do not test GitHub Environment secrets/protection in a real repository, do not trigger SQL and do not replace the four live SQL oracles.

**Round-4 disposition:** Keep PR #9 DRAFT / NOT FOR MERGE; protected GitHub Environment and live HTTPS→SQL are NOT VERIFIED. Final-HEAD ordinary CI rerun and independent protected live execution are separate requirements.
