# VPS-03 — Desktop HTTP Cutover: implementation and independent gates

**Status: IN PROGRESS; NOT production accepted.** Repo: `chung6a8m/MyDmsVn-Vps`. Base main: `f266c68958c7d3b512e1f5e036122ab6341bed5e`. Verified implementation commits: `c56d2750e95c4b33700dcad6414ae908b432cf3c` (HTTP tests), `8156d02d701b13a910b84b6daac2c48fe877b277` (published binary gate), `acb28d5925ba934ef2e0e0bfdae55152ee16a111` (WEB5 v2 admission and final executable changes), 16 November 2026. The final documentation-only PR HEAD requires CI rerun. Results below are **specific to this repository and these SHAs**.

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

Source graph: `Desktop.App/packages.lock.json` resolves 23 net48 entries and contains no `SqlClient`, `MediatR`, `linq2db`, `Server.Application`, `Server.Infrastructure`, or `Infrastructure.Local`. On Windows, a real Desktop.App `net48` x86 `dotnet publish` produced 35 files; CI rejected forbidden SQL/Local/Server/migrator binary names, credential markers and SQL connection-string markers and found **zero violations**. This is a **published artifact static audit, not an attended Windows GUI loader/release rehearsal**.

## Open acceptance / review findings (not accepted exceptions)

1. **P0 — No live Desktop → HTTPS HttpHost → SQL E2E executed.** Requires operator-approved isolated SQL Server and real Windows client, strict feature/permission/transaction/stock/COGS comparison, authentication cookie rotation and external audit. VPS-02 live HTTP/SQL acceptance is not the VPS-03 Desktop E2E gate.
2. **P1 — [Issue #7: restart-safe recovery and UI reconciliation](https://github.com/chung6a8m/MyDmsVn-Vps/issues/7).** Source now has protected owner-scoped persistence and an attended recovery dialog, but real process-crash, SQL, operator-session and GUI evidence is outstanding. WEB4/WEB5 journal reads continue to require the original actor's current authorized session and original Write/Post grant; no cross-actor recovery is enabled.
3. **P1 — [Issue #7](https://github.com/chung6a8m/MyDmsVn-Vps/issues/7).** Catalog/Security writes now share the durable pending fence and expose advisory authorized readback, but still have no server idempotency journal or audited terminal-release contract. Live readback/reconciliation acceptance is not established; an UNKNOWN may remain protected indefinitely.
4. **P1 — [Issue #8: real Windows HTTPS/SQL and attended GUI](https://github.com/chung6a8m/MyDmsVn-Vps/issues/8).** No attended GUI/session acceptance. Verify Desktop login/logout, idle timeout, refresh race, restricted permissions, workspace disposal and actual UI error mapping.
5. **PARTIALLY VERIFIED — published net48 artifact** passes automated name/config exclusion (35 files and 23 graph entries), but an independent Windows loader/release run and actual GUI process inspection still remain under [Issue #8](https://github.com/chung6a8m/MyDmsVn-Vps/issues/8).
6. **P2 — Independent review and final documentation-HEAD CI.** Code CI passed at `4a16c3a` after fixing three compile findings. A documentation-only evidence update requires a final-HEAD rerun and review; **do not mark VPS-03 COMPLETE before P0/P1 gates are proved or explicitly accepted by Owner.**

No hidden SQL skip or production readiness is inferred. Current task status authority: [roadmap](../roadmap/README.md).

## Issue #7 candidate — durable UNKNOWN write recovery (pending CI/independent acceptance)

A follow-up on draft PR #6 adds a Windows-owner-scoped, DPAPI-encrypted, ACL-restricted, bounded pending intent journal with a write-through temp file and atomic no-overwrite move before HTTP dispatch; on restart a matching exact intent is loaded and all writes are fenced until independently verified. It adds WEB5 v2 resolution requests, current actor + envelope checks, attended shell recovery modal and an explicitly confirmed WEB5 negative audit certificate path. Unjournalled Catalog/Security writes receive the same intent fence, authorized advisory readback and **no automatic release**. No request body, password, JWT or refresh cookie is persisted. See [operator runbook](../runbooks/issue-7-unknown-write-recovery.md).

**Code HEAD CI VERIFIED, 17 November 2026 (UTC+07), `4a16c3a634eeed7a813155914942f8b127eee0fd`:**
- [Foundation run #43962379871](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/43962379871): **8/8 jobs success**. Windows net48 actual SQL-free execution: Architecture **15/15**, Desktop.Session **235/235**, Server.Application **363/363**, HttpHost **683/683** = **1,296/1,296 passed**, **0 failed, 0 skipped/notExecuted**. The 235 Desktop.Session tests include Issue #7 loss/restart/actor/terminal/readback mocks and conditional Windows DPAPI/ACL owner-store roundtrip; record is a mock persistence double in most tests, not a true process-crash/SQL oracle.
- [xUnit v3 discovery run #43962379860](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/43962379860): **9/9 jobs success** (compilation/discovery of other suites; NOT test execution).
- [Desktop x86 publish run #43962379853](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/43962379853): **1/1 job success**, **23 lock graph entries**, **35 published files**, **0 forbidden Local/SQL/Server/runtime markers**.
- **18/18 jobs success at the exact code commit `4a16c3a`**. The earlier candidate `55bba9778` failed compilation with three errors (nullable dereferences, missing System.IO); all three were fixed before this successful final code HEAD. A later documentation-only commit must separately receive its own final-HEAD CI to count as verified.

**OPEN — independent acceptance, not covered by CI:** Windows process-kill/crash at file-save boundaries and restart; real Desktop → HTTPS Host → SQL session/epoch/permission and operator-journal race tests; physical ledger/stock/COGS single-post oracle; actual attended Windows UI session/reconciliation; Catalog/Security terminal release authority. No production approval or Owner-dated exception. Keep [Issue #7](https://github.com/chung6a8m/MyDmsVn-Vps/issues/7), [Issue #8](https://github.com/chung6a8m/MyDmsVn-Vps/issues/8) and VPS-03 **OPEN / IN PROGRESS**.
