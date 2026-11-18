# VPS-05C — System.Text.Json production-runtime isolation and 57-route live wire gate

**Repository:** `chung6a8m/MyDmsVn-Vps` · **Issue:** [#15](https://github.com/chung6a8m/MyDmsVn-Vps/issues/15) · **Implementation:** [PR #17](https://github.com/chung6a8m/MyDmsVn-Vps/pull/17)
**Baseline:** VPS-05B merged `main` `f2eeb62f19a5cb8177eb769d01dba4003a2621d8`.
**Status:** CODE IMPLEMENTED / 293-CASE LIVE DIFFERENTIAL PASS locally at `875ac117c4b7deb177eff1d8c78da94b31cb9fc2`; all five workflows passed on preceding 279-case documentation checkpoint `b3542e05d2d5056effa6449141bc0ae0ae2dea35` / FINAL DOCUMENTATION-HEAD CI, REVIEW DISPOSITION AND MERGE PENDING. **VPS-05 overall NOT COMPLETE until merge.**

## Production dependency change

- The published ASP.NET Core `net10.0` Host, Server.Application and Contracts no longer request Newtonsoft.Json directly. Json.NET remains only for Desktop/legacy net48 and **independent net10 test oracles**. Conditional shared DTO JSON metadata and strict calendar converters use `System.Text.Json` on net10 and original Json.NET implementations on net48.
- `SqlSecurityAdministrationStore` serializes canonical permission imports using native STJ on net10, leaving SQL stored-procedure shape and net48 Json.NET intact. Protected Linux durable audit JSON output similarly uses native STJ, including explicit nullable fields and unchanged ordered audit schema.
- Net10 locked package snapshots were updated without Newtonsoft for the production graph; the Host **test** lock retains explicit Json.NET to run independent differential tests. `scripts/verify-vps04b-runtime-boundary.py` checks actual compiled Host `.deps.json` `libraries` **and** `targets` for Newtonsoft, alongside existing OWIN/System.Web/ACL exclusion. Do not mistake test-only deps for a runtime dependency.

## Live compatibility checks

- A compiled, frozen-signature guard checks all **57/57** mapped routes against the independent VPS-01 (29) and VPS-02 (28) inventories.
- A real net48 OWIN host generates a frozen, independently reviewable oracle; a real net10 Kestrel listener dispatches the same route/variant and compares status, content type, correlation/cache/security/auth headers, `Set-Cookie`, and raw response body. Dynamic JWT text is normalized only after its three-part structure is checked and the issued token successfully calls `/api/v1/auth/me`.
- The exact matrix contains **293 unique `(caseId, variant)` observations across 57 route IDs**. All 28 VPS-02 routes and all 16 sales/lookup/inventory routes cover success, permission, invalid input/query, sanitized handler/repository failure and unauthenticated variants. Auth/control routes add applicable wrong-credential, rate-limit with `Retry-After`, refresh-cookie, valid capability, invalid/unauthenticated/failure cases. WEB4/5 add enabled committed success, strict invalid input, unauthenticated, repository failure, stored-kind permission denial, not-observed and epoch/fingerprint conflict responses; WEB6 covers disabled/enabled, invalid, unauthenticated, permission, repository failure, signed epoch/fingerprint/candidate conflicts, deny and indeterminate responses.
- Success payloads include null/missing values, calendar dates, UTC timestamps, four-decimal values, string-encoded int64 values, revisions above `2^53`, Unicode and escaped text. The Linux workflow requires exactly **293 executed/passed, zero failed/skipped/not-executed**; the net48 generator separately asserts 293 unique observations and 57 unique IDs.
- Existing regressions still cover strict STJ ingress, duplicate/malformed UTF-8, null vs missing, four-decimal and long-as-string payloads, six WEB4 fingerprint/canonical vectors, WEB5/WEB6 envelope projections, cursor hashes, calendar/UTC serialization, and operation journal codec. The independent legacy Json.NET oracles now live only in test assemblies; no baseline fixture hashes have been changed.
- The prior disposable SQL acceptance exercises 15 bounded real HTTPS→SQL13 facts, 24 WEB2–WEB6 selected expected/actual/diff scenarios in WEB5 mode (WEB6 disabled/fail-closed), and separately 3 enabled WEB6 durable-audit/restart acceptance facts. **Neither** this subset nor 178 pinned fixture files proves exhaustive per-route success/error/header/query/body parity.

## Exact code-checkpoint CI (2026-11-18)

All links below refer to the preceding **documentation SHA `b3542e05d2d5056effa6449141bc0ae0ae2dea35`**. GitHub PR #17 must establish the same five successful results, with the expanded 293-case gate, on the later exact final HEAD.

| Gate | Code-checkpoint evidence |
| --- | --- |
| Ubuntu SDK 10.0.404, warnings-as-errors, locked net10 production restore/build, generated Host `.deps.json` | **PASS:** production runtime boundary, SQL-free suites, 57/57 route inventory and exact **279/279** live net48-vs-net10 differential; zero failed/skipped/not-executed. The Linux-only FD-leak test remains bounded and is isolated from unrelated parallel Kestrel socket churn. [Linux run #44224021745](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44224021745) |
| Disposable Linux SQL2022, HTTPS with real TLS, restricted database principal, SQL Oracle, WEB2–WEB6 | **PASS:** 15/15 bounded real HTTPS→SQL facts and the separately enabled WEB6 durable-audit/restart subset; zero failure/skip in required subsets. [SQL13 run #44224021912](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44224021912) |
| Windows Desktop `net48` published x86 artifact | **PASS:** package/dependency boundary only; **NOT** attended GUI/SQL E2E. [Desktop publish run #44224021754](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44224021754) |
| Windows xUnit v3 compile/discovery | **PASS:** all discovery workflow jobs; discovery is not live SQL E2E. [xUnit run #44224021770](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44224021770) |
| Windows legacy `net48` foundation and SQL-free smoke | **PASS:** legacy builds and SQL-free smoke, including live net48 oracle regeneration. [legacy run #44224021903](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44224021903) |

## Remaining acceptance boundaries

1. **Exact final documentation-HEAD CI and final review disposition remain required before merge.** The code-checkpoint results above do not automatically transfer to a later SHA.
2. **VPS-05C and VPS-05 become complete only after PR #17 is merged and that merge is recorded in the roadmap.**
3. **Protected VPS staging/systemd/proxy/secrets and integrated Windows Desktop → Linux Host → real SQL release** remain `NOT VERIFIED` (VPS-08/09). Disposable CI SQL is not production or staging.

The canonical status is [`docs/roadmap/README.md`](../roadmap/README.md). Re-evaluate this ledger on the final code/documentation SHA rather than claiming inherited PASS.
