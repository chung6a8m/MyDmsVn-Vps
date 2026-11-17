# VPS-03 — Desktop HTTP Cutover: implementation and independent gates

**Status: ISSUES #7/#8 LOCALLY ACCEPTED; VPS-03 IN PROGRESS; NOT production accepted.** Repo: `chung6a8m/MyDmsVn-Vps`. Base main: `f266c68958c7d3b512e1f5e036122ab6341bed5e`. Issue #8 acceptance HEAD is `4c0b1bea9668c20301e2ab69fe68126602e7adf2`; Issue #7 exact package/acceptance HEAD is `40b7ebc00d703a169566b27004825f376418ab8c`, 17 November 2026. The definitive final documentation SHA and CI run URLs are recorded in PR #6 to avoid a self-referential evidence commit. Results below are **specific to this repository and these SHAs**.

## Code implemented — VPS-03A / VPS-03B

- New `src/MyDmsVn.Desktop.Infrastructure.Http`, target `net48`; all existing 46 Desktop feature method slots have typed HTTP implementations. Production `Desktop.App` references Http + WinForms, no Local/Server project, no SQL startup. Historical Local adapter and test references stay in the repository, not the production Desktop dependency graph.
- Fail-closed `MYDMSVN_DESKTOP_API_ORIGIN` (canonical HTTPS) and `MYDMSVN_DESKTOP_OPERATION_MODE` (`legacy-web11`, `web4`, `web5`); no automatic fallback or certificate-validation override. Host additive routes continue to require `MYDMSVN_WEB_DESKTOP_API=enabled`.
- In-memory Bearer token and `HttpClientHandler` refresh cookie; same-origin `Origin` on unsafe requests; `X-Requested-With` for login/refresh/logout, single-flight refresh, generation fence, bounded synchronous logout, explicit cancellation/timeout errors and no automatic HTTP replay.
- Strict Sales/Goods v1 wire projection (calendar dates, int64 strings, decimal strings, version), inventory receipt/line ID normalization without converting date strings, `JObject` reader with `DateParseHandling.None`.
- WEB4/WEB5 epoch + GUIDv4 idempotency key + frozen WEB4 SHA256 for six Sales/Goods commands; ambiguous ACK retains one in-process pending intent; only a fully matching `COMMITTED` status can clear the fence. Cancellation during refresh invalidates local credentials instead of replaying refresh. Exact WEB5 configuration also requires a valid authenticated WEB5 v2 context and `sameKeyResendEnabled=false`; WEB4-only deployments are rejected before mutation.
- Vendored SourceGrid `net48` excludes its obsolete SQL command helper, `Microsoft.Data.SqlClient` and `Microsoft.Windows.Compatibility` package refs. The vendor `net8.0-windows` target is preserved. Windows .NET Framework references `System.Net.Http`, `System.Security`, and `System.Configuration` are explicit.

## Verified CI — exact implementation SHA acb28d5

| Evidence | Environment | Actual outcome |
| --- | --- | --- |
| [Foundation workflow #43903066275](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/43903066275) | Ubuntu static checks + Windows Server 2022 SDK 8.0.400 / net48 locked restore and Release build | **8/8 jobs SUCCESS** |
| [xUnit v3 discovery #43903065827](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/43903065827) | Windows compile/discovery of eight non-smoke suites, Linux static topology | **9/9 jobs SUCCESS** (discovery is not test execution) |
| Architecture smoke | Windows net48 SQL-free | **15/15 passed**, failed 0, skipped/not-executed 0 |
| Desktop.Session (including new HTTP adapter tests) | Windows net48 SQL-free | **225/225 passed**, failed 0, skipped/not-executed 0 |
| Server.Application smoke | Windows net48 SQL-free | **363/363 passed**, failed 0, skipped/not-executed 0 |
| Legacy HttpHost regression | Windows net48 SQL-free | **683/683 passed**, failed 0, skipped/not-executed 0 |
| **SQL-free smoke total** | Windows net48 | **1,286/1,286 passed**, failed 0, skipped/not-executed 0 |
| [Desktop published artifact #43903065526](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/43903065526) | Windows Server 2022 SDK 8.0.400, locked net48 x86 publish and package/output exclusion scan | **1/1 SUCCESS**: 23 package-lock dependencies, 35 published files, zero forbidden binaries/config markers |
| **All three CI workflows** | Windows net48 plus Ubuntu static | **18/18 jobs passed** at acb28d5 |

The new HTTP tests include fake transport login/Bearer and session-generation fencing, 401/403 handling, known failure vs unknown/lost ACK, no replay, security/feature DI, provider-free preview, six independent WEB4 corpus SHA256 vectors, exact inventory date/int64 rename, and WEB5-to-WEB4 mode-mismatch refusal. **These are mock transport tests, not HTTPS/SQL E2E.**

Source graph: `Desktop.App/packages.lock.json` resolves 23 net48 entries and contains no `SqlClient`, `MediatR`, `linq2db`, `Server.Application`, `Server.Infrastructure`, or `Infrastructure.Local`. On Windows, a real Desktop.App `net48` x86 `dotnet publish` produced 35 files; CI rejected forbidden SQL/Local/Server/migrator binary names, credential markers and SQL connection-string markers and found **zero violations**. That CI job remains a static artifact audit; the separate Issue #8 exact-SHA run below supplies the attended Windows GUI loader/release evidence.

## Current acceptance / review findings (not accepted exceptions)

1. **ISSUE #8 PASS — live Desktop → HTTPS HttpHost → SQL E2E.** Exact-SHA Release x86 Desktop and IIS packages ran on Windows 11 against owned restricted SQL schema 11/12/13 fixtures. Production adapter methods, authentication/session/permission behavior, stock/COGS, deterministic lost-ACK recovery and attended Native GUI were exercised with zero required skips; details and counts are below.
2. **ISSUE #7 LOCAL PASS — restart-safe recovery and UI reconciliation.** Exact-SHA process-crash, protected-store, HTTPS/SQL/operator-session and Native GUI evidence passed 14/14 with zero skips; see [Issue #7 closeout evidence](vps-03-issue-7-closeout.md). WEB4/WEB5 journal reads continue to require the original actor's current authorized session and original Write/Post grant; no cross-actor recovery is enabled.
3. **ACCEPTED FAIL-CLOSED LIMITATION.** Catalog/Security writes share the durable pending fence and advisory authorized readback, but have no server idempotency journal or audited terminal-release contract. Readback never releases the fence; an UNKNOWN may remain protected indefinitely.
4. **ISSUE #8 PASS — attended Native GUI/session acceptance.** Login, shell, Catalog, Goods, Sales, Security administration, normalized invalid-credential error and logout were observed on the exact published package; the process then exited cleanly and owned IIS/SQL resources were absent.
5. **VERIFIED — published net48 artifact.** Automated name/config exclusion remains in CI, and the exact x86 package was loaded and exercised by the attended Windows run at `4c0b1be`.
6. **P2 — Independent review.** Issue #8 passed at `4c0b1be` and Issue #7 local package acceptance passed at `40b7ebc`. Final documentation-HEAD CI, independent review and merge remain open, so **do not mark VPS-03 COMPLETE or production-ready.**

No hidden SQL skip or production readiness is inferred. Current task status authority: [roadmap](../roadmap/README.md).

## Issue #7 — durable UNKNOWN write recovery

A follow-up on draft PR #6 adds a Windows-owner-scoped, DPAPI-encrypted, ACL-restricted, bounded pending intent journal with a write-through temp file and atomic no-overwrite move before HTTP dispatch; on restart a matching exact intent is loaded and all writes are fenced until independently verified. It adds WEB5 v2 resolution requests, current actor + envelope checks, attended shell recovery modal and an explicitly confirmed WEB5 negative audit certificate path. Unjournalled Catalog/Security writes receive the same intent fence, authorized advisory readback and **no automatic release**. No request body, password, JWT or refresh cookie is persisted. See [operator runbook](../runbooks/issue-7-unknown-write-recovery.md).

**Code HEAD CI VERIFIED, 17 November 2026 (UTC+07), `4a16c3a634eeed7a813155914942f8b127eee0fd`:**
- [Foundation run #43962379871](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/43962379871): **8/8 jobs success**. Windows net48 actual SQL-free execution: Architecture **15/15**, Desktop.Session **235/235**, Server.Application **363/363**, HttpHost **683/683** = **1,296/1,296 passed**, **0 failed, 0 skipped/notExecuted**. The 235 Desktop.Session tests include Issue #7 loss/restart/actor/terminal/readback mocks and conditional Windows DPAPI/ACL owner-store roundtrip; record is a mock persistence double in most tests, not a true process-crash/SQL oracle.
- [xUnit v3 discovery run #43962379860](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/43962379860): **9/9 jobs success** (compilation/discovery of other suites; NOT test execution).
- [Desktop x86 publish run #43962379853](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/43962379853): **1/1 job success**, **23 lock graph entries**, **35 published files**, **0 forbidden Local/SQL/Server/runtime markers**.
- **18/18 jobs success at the exact code commit `4a16c3a`**. The earlier candidate `55bba9778` failed compilation with three errors (nullable dereferences, missing System.IO); all three were fixed before this successful final code HEAD. A later documentation-only commit must separately receive its own final-HEAD CI to count as verified.

**Independent Issue #7 acceptance PASS at `40b7ebc`:** process termination around the durable save, protected-store failures, restart/session/grant/terminal behavior, operator race, permanent unjournalled fencing, and the Native recovery dialog passed 14/14 with zero skips. Catalog/Security deliberately still have no terminal release authority; an ambiguous write can remain fenced indefinitely. See [closeout evidence](vps-03-issue-7-closeout.md). This is not production approval; VPS-03 remains **IN PROGRESS** pending final-HEAD CI, review and merge.

## Issue #8 native acceptance — 17 November 2026

This is a complete Issue #8 PASS, not production approval. Windows 11, .NET Framework 4.8, installed IIS Express, trusted localhost TLS binding and an operator-supplied SQL Server connection were used. The isolated database/login and IIS process were owned per run and cleanup was verified. The exact Release x86 Desktop/HttpHost package and acceptance harness provenance was `4c0b1bea9668c20301e2ab69fe68126602e7adf2`. Base remains `f266c68958c7d3b512e1f5e036122ab6341bed5e`.

| Harness commit | Native result | Counts |
| --- | --- | --- |
| `e1a7745` plus startup-gap fix in `d58d0e9` | Owned HTTPS/restricted-SQL schema 11/12/13 admission | 3/3 PASS, 0 failed, 0 skipped |
| `d58d0e9` | All 46 public Desktop interface methods through production `AddHttpDesktopAdapter` to actual IIS Express/SQL | 46/46 PASS, 0 failed, 0 skipped |
| `2111d5b` | login/me/logout/re-auth, normalized unauthorized login, independent-session permission grant/revocation | 3/3 PASS, 0 failed, 0 skipped |
| `de6911b` | Goods Post + Sales Post SQL oracle: 2 ledger rows, 2 valuation rows, ending quantity 8, inventory value 40, COGS 10 | 1/1 PASS, 0 failed, 0 skipped |
| `1265c9c` | Fail-closed attended-result model only | 2/2 unit PASS; **GUI walkthrough NOT RUN** |
| `76641c23a83ae440494575250a66d2ba35bb2352` | Exact-SHA Release x86 Desktop/IIS package plus complete native core acceptance project | **85/85 PASS**, 0 failed, 0 skipped, 1m37s; `-CoreOnly`, therefore not full Issue #8 acceptance |
| `42c3db9f417dec9bf1c7ae3226ad63ba8ef760f4` | Deterministic response drop after upstream Sales Post, independent-session replay fence, exact COMMITTED readback and SQL single-post oracle | **86/86 PASS**, 0 failed, 0 skipped, 2m17s; first lost ACK run |
| `4c0b1bea9668c20301e2ab69fe68126602e7adf2` | Exact-SHA full gate: Release x86 Desktop/IIS, all native core/lost-ACK cases, separate attended Native GUI record and fail-closed cleanup | **Core 86/86 PASS** in 1m49s and **attended 1/1 PASS** in 2m19s; 0 failed, 0 skipped |

The live run exposed and fixed a harness lazy-startup gap: setting a protected audit path without its candidate root allowed process admission but failed the first HTTP request. Final fixture startup now performs an authenticated request and independently checks schema compatibility and superuser binding. Two deliberately rejected write probes correctly retained an encrypted UNKNOWN fence; because their owned databases had already been abandoned, only the exact current-run files were removed after exact path/count/date verification. Final acceptance avoids claiming those probes as recoverable evidence.

At clean HEAD `4c0b1bea9668c20301e2ab69fe68126602e7adf2`, `-CoreOnly` built the Release x86 Desktop and IIS host, validated package provenance, and executed all 86 selected non-attended test cases. The deterministic fault proxy forwarded one Sales Post, received success from the upstream host, discarded that response and raised a transport failure. The first client retained UNKNOWN; an independent authenticated session was fenced before replay; exact COMMITTED journal readback cleared the pending intent. SQL remained exactly two ledger rows and two valuation rows across the seeded Goods + Sales pair, with ending quantity 8, value 40 and COGS 10.

The separate `-AttendedOnly` run on the same SHA recorded all eight required checkpoints: `LOGIN`, `SHELL`, `CATALOG`, `GOODS`, `SALES`, `SECURITY`, `ERRORS`, and `LOGOUT`. The invalid login displayed the normalized Vietnamese error, the valid login opened the owner shell, all workspaces were visible, logout returned to the login window, Desktop exited cleanly, and owned host/SQL cleanup passed. The default full command then consumed both exact-SHA result records and reran the 86 non-attended tests, printing `PASS head=4c0b1be... total=86 passed=86 failed=0 skipped=0`.

Final local regression checks on the same code HEAD, before the documentation commit, were: static foundation PASS (2,558 tracked files, 46 projects, 98 ProjectReferences, 13 SQL scripts, 178 frozen fixtures), VPS-01/VPS-02 topology PASS, and the four SQL-free suites **1,296/1,296 PASS** — Architecture 15, Desktop.Session 235, Server.Application 363 and HttpHost 683, zero failed/skipped. Sequential clean build/discovery listed Desktop 813, Desktop.Ui 382, SQL Catalog 19, Foundation 66, Harness 59, Inventory 208, Migrations 175, Security 1,756 and VPS-03 acceptance 40; discovery is not execution. The x86 publish audit found 23 lock dependencies, 35 files and zero forbidden artifacts/configuration. A preliminary parallel smoke attempt shared `bin/obj` with the prior x86 publish and produced mixed-architecture `BadImageFormat` failures; it is not counted as test evidence. Clean sequential rebuilds, matching CI isolation, produced the authoritative passing counts above.

**Issue #8 disposition:** all required native/live and attended gates passed on the same exact package SHA with zero required skips; documentation HEAD `326f65c` then passed 19/19 CI jobs and Issue #8 was closed as completed. Issue #7 later passed its separate exact-SHA gates at `40b7ebc`. PR #6 remains draft and VPS-03 remains IN PROGRESS because final-HEAD CI, independent review and merge are separate gates. The final status-only PR SHA and CI URLs remain recorded in PR #6.
