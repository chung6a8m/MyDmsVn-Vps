# VPS-05C — System.Text.Json production-runtime isolation and 57-route bounded wire gate

**Repository:** `chung6a8m/MyDmsVn-Vps` · **Issue:** [#15](https://github.com/chung6a8m/MyDmsVn-Vps/issues/15) · **Implementation:** [draft PR #17](https://github.com/chung6a8m/MyDmsVn-Vps/pull/17)  
**Baseline:** VPS-05B merged `main` `f2eeb62f19a5cb8177eb769d01dba4003a2621d8`.  
**Status:** CODE IMPLEMENTED / BOUNDED CI CHECKPOINT PASS at `2a60ed733c8d435ebb4b859d0eab4751dea6cb22` / INDEPENDENT REVIEW, FINAL DOC-HEAD CI, EXHAUSTIVE DIFFERENTIAL AND MERGE PENDING. **VPS-05 overall NOT ACCEPTED.**

## Production dependency change

- The published ASP.NET Core `net10.0` Host, Server.Application and Contracts no longer request Newtonsoft.Json directly. Json.NET remains only for Desktop/legacy net48 and **independent net10 test oracles**. Conditional shared DTO JSON metadata and strict calendar converters use `System.Text.Json` on net10 and original Json.NET implementations on net48.
- `SqlSecurityAdministrationStore` serializes canonical permission imports using native STJ on net10, leaving SQL stored-procedure shape and net48 Json.NET intact. Protected Linux durable audit JSON output similarly uses native STJ, including explicit nullable fields and unchanged ordered audit schema.
- Net10 locked package snapshots were updated without Newtonsoft for the production graph; the Host **test** lock retains explicit Json.NET to run independent differential tests. `scripts/verify-vps04b-runtime-boundary.py` checks actual compiled Host `.deps.json` `libraries` **and** `targets` for Newtonsoft, alongside existing OWIN/System.Web/ACL exclusion. Do not mistake test-only deps for a runtime dependency.

## Bounded compatibility checks

- A compiled, frozen-signature guard checks all **57/57** mapped routes against the independent VPS-01 (29) and VPS-02 (28) inventories.
- Every compiled route is checked under two **SQL-free** admission/error variants: (1) invalid transport fails before dispatch with exact 400 JSON and security/cache headers; (2) unauthenticated protected routes fail before parsing, with exact 401 error JSON and Bearer/cache/security headers; the four anonymous routes reach the dispatch boundary. These are **114 route/variant admission assertions**, NOT 114 real SQL-backed successful business responses or full legacy-vs-new live differential.
- Existing regressions still cover strict STJ ingress, duplicate/malformed UTF-8, null vs missing, four-decimal and long-as-string payloads, six WEB4 fingerprint/canonical vectors, WEB5/WEB6 envelope projections, cursor hashes, calendar/UTC serialization, and operation journal codec. The independent legacy Json.NET oracles now live only in test assemblies; no baseline fixture hashes have been changed.
- The prior disposable SQL acceptance exercises 15 bounded real HTTPS→SQL13 facts, 24 WEB2–WEB6 selected expected/actual/diff scenarios in WEB5 mode (WEB6 disabled/fail-closed), and separately 3 enabled WEB6 durable-audit/restart acceptance facts. **Neither** this subset nor 178 pinned fixture files proves exhaustive per-route success/error/header/query/body parity.

## Exact code-checkpoint CI (2026-11-18)

All links below refer to **code SHA `2a60ed733c8d435ebb4b859d0eab4751dea6cb22`**, before the documentation closeout commit. GitHub PR #17 must independently establish five successful results on the **later exact final HEAD**; a documentation commit invalidates their status as final-HEAD evidence.

| Gate | Code-checkpoint evidence |
| --- | --- |
| Ubuntu SDK 10.0.404, warnings-as-errors, locked net10 production restore/build, generated Host `.deps.json` | **PASS:** 30 runtime libraries and **zero Newtonsoft/OWIN/System.Web/Windows ACL**; **366/366** Server.Application, **364/364** Host tests with no failures/skips; **57/57** compiled routes. [Linux run #44205797263](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44205797263) |
| Disposable Linux SQL2022, HTTPS with real TLS, restricted database principal, SQL Oracle, WEB2–WEB6 | **PASS:** 15/15 real HTTPS→SQL facts, 24 expected/actual/diff scenarios (WEB6 disabled), 3/3 separate enabled WEB6 Linux audit/restart facts; zero failure/skip in required subsets. [SQL13 run #44205797255](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44205797255) |
| Windows Desktop `net48` published x86 artifact | **PASS:** package/dependency boundary only; **NOT** attended GUI/SQL E2E. [Desktop publish run #44205797295](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44205797295) |
| Windows xUnit v3 compile/discovery | **PASS:** all 10 discovery workflow jobs; discovery is not live SQL E2E. [xUnit run #44205797260](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44205797260) |
| Windows legacy `net48` foundation and SQL-free smoke | **CHECK FINAL CONCLUSION IN PR BEFORE ACCEPTANCE:** [legacy run #44205797296](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44205797296). The preceding four rows do not override a pending/failed Windows job. |

## Outstanding blocking acceptance

1. **Exhaustive independent 57-route live legacy-vs-STJ response/header differential remains OPEN.** Source inventory identifies 28 VPS-02 routes as `ROUTE_ONLY` in the previous Linux/SQL response ledger; all per-route success, forbidden, validation, malformed, date/number, and error variants are not proven. The new 57-route admission matrix improves negative coverage but cannot close this gate.
2. **Independent code/contract/security review and exact-final-HEAD CI remain PENDING.** Do not merge a draft or label VPS-05C complete solely from the bounded checks.
3. **Protected VPS staging/systemd/proxy/secrets and integrated Windows Desktop → Linux Host → real SQL release** remain `NOT VERIFIED` (VPS-08/09). Disposable CI SQL is not production or staging.

The canonical status is [`docs/roadmap/README.md`](../roadmap/README.md). Re-evaluate this ledger on the final code/documentation SHA rather than claiming inherited PASS.
