# VPS-03 — Desktop HTTP Cutover: implementation and independent gates

**Status: IN PROGRESS; NOT production accepted.** Repo: `chung6a8m/MyDmsVn-Vps`. Base main: `f266c68958c7d3b512e1f5e036122ab6341bed5e`. Verified implementation commit: `c56d2750e95c4b33700dcad6414ae908b432cf3c` (16 November 2026). The subsequent documentation-only PR HEAD must be verified again before merge. Results below are **specific to this repository and SHA**.

## Code implemented — VPS-03A / VPS-03B

- New `src/MyDmsVn.Desktop.Infrastructure.Http`, target `net48`; all existing 46 Desktop feature method slots have typed HTTP implementations. Production `Desktop.App` references Http + WinForms, no Local/Server project, no SQL startup. Historical Local adapter and test references stay in the repository, not the production Desktop dependency graph.
- Fail-closed `MYDMSVN_DESKTOP_API_ORIGIN` (canonical HTTPS) and `MYDMSVN_DESKTOP_OPERATION_MODE` (`legacy-web11`, `web4`, `web5`); no automatic fallback or certificate-validation override. Host additive routes continue to require `MYDMSVN_WEB_DESKTOP_API=enabled`.
- In-memory Bearer token and `HttpClientHandler` refresh cookie; same-origin `Origin` on unsafe requests; `X-Requested-With` for login/refresh/logout, single-flight refresh, generation fence, bounded synchronous logout, explicit cancellation/timeout errors and no automatic HTTP replay.
- Strict Sales/Goods v1 wire projection (calendar dates, int64 strings, decimal strings, version), inventory receipt/line ID normalization without converting date strings, `JObject` reader with `DateParseHandling.None`.
- WEB4/WEB5 epoch + GUIDv4 idempotency key + frozen WEB4 SHA256 for six Sales/Goods commands; ambiguous ACK retains one in-process pending intent; only a fully matching `COMMITTED` status can clear the fence. Cancellation during refresh invalidates local credentials instead of replaying refresh.
- Vendored SourceGrid `net48` excludes its obsolete SQL command helper, `Microsoft.Data.SqlClient` and `Microsoft.Windows.Compatibility` package refs. The vendor `net8.0-windows` target is preserved. Windows .NET Framework references `System.Net.Http`, `System.Security`, and `System.Configuration` are explicit.

## Verified CI — exact implementation SHA c56d275

| Evidence | Environment | Actual outcome |
| --- | --- | --- |
| [Foundation workflow #43899518767](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/43899518767) | Ubuntu static checks + Windows Server 2022 SDK 8.0.400 / net48 locked restore and Release build | **8/8 jobs SUCCESS** |
| [xUnit v3 discovery #43899518821](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/43899518821) | Windows compile/discovery of eight non-smoke suites, Linux static topology | **9/9 jobs SUCCESS** (discovery is not test execution) |
| Architecture smoke | Windows net48 SQL-free | **15/15 passed**, failed 0, skipped/not-executed 0 |
| Desktop.Session (including new HTTP adapter tests) | Windows net48 SQL-free | **224/224 passed**, failed 0, skipped/not-executed 0 |
| Server.Application smoke | Windows net48 SQL-free | **363/363 passed**, failed 0, skipped/not-executed 0 |
| Legacy HttpHost regression | Windows net48 SQL-free | **683/683 passed**, failed 0, skipped/not-executed 0 |
| **SQL-free smoke total** | Windows net48 | **1,285/1,285 passed**, failed 0, skipped/not-executed 0 |

The new HTTP tests include fake transport login/Bearer and session-generation fencing, 401/403 handling, known failure vs unknown/lost ACK, no replay, security/feature DI, provider-free preview, six independent WEB4 corpus SHA256 vectors and exact inventory date/int64 rename. **These are mock transport tests, not HTTPS/SQL E2E.**

Source graph: `Desktop.App/packages.lock.json` resolves 23 net48 entries and, on the verified branch, contains no `SqlClient`, `MediatR`, `linq2db`, `Server.Application`, `Server.Infrastructure`, or `Infrastructure.Local` dependency entries. This is static NuGet lock inspection only; **no published release package / actual Windows loader audit** has been run.

## Open acceptance / review findings (not accepted exceptions)

1. **P0 — No live Desktop → HTTPS HttpHost → SQL E2E executed.** Requires operator-approved isolated SQL Server and real Windows client, strict feature/permission/transaction/stock/COGS comparison, authentication cookie rotation and external audit. VPS-02 live HTTP/SQL acceptance is not the VPS-03 Desktop E2E gate.
2. **P1 — Pending write identity exists in memory only.** Process restart loses the fence. WEB4/Web5 journal reads are session-scoped; a new login after restart does not automatically recover the original session. No durable cross-process recovery or support handoff is established.
3. **P1 — Other write APIs (Catalog, Security) have no idempotency journal.** A timeout after dispatch may be ambiguous. No automatic retry occurs, but authoritative readback/reconciliation and UI fencing are not yet verified.
4. **P1 — No attended GUI/session acceptance.** Verify Desktop login/logout, idle timeout, refresh race, restricted permissions, workspace disposal and actual UI error mapping.
5. **P1 — No real published net48 artifact audit** for zero SQL provider binaries, Server assemblies, Local adapter, MediatR, admin migrator and no secrets. The locked graph/build alone is insufficient.
6. **P2 — No independent review / final-HEAD validation after documentation edits.** Re-run both workflows on the precise final SHA, review all findings, then determine whether code is ready to merge as a gated increment. **Do not mark VPS-03 COMPLETE before P0/P1 gates are proved or explicitly accepted by Owner.**

No hidden SQL skip or production readiness is inferred. Current task status authority: [roadmap](../roadmap/README.md).
