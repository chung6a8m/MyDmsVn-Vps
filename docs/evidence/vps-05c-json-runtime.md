# VPS-05C — System.Text.Json production-runtime isolation and 57-route live wire gate

**Repository:** `chung6a8m/MyDmsVn-Vps` · **Issue:** [#15](https://github.com/chung6a8m/MyDmsVn-Vps/issues/15) · **Implementation:** [PR #17](https://github.com/chung6a8m/MyDmsVn-Vps/pull/17)
**Baseline:** VPS-05B merged `main` `f2eeb62f19a5cb8177eb769d01dba4003a2621d8`.
**Status:** CODE IMPLEMENTED / 293/293 LIVE DIFFERENTIAL PASS and all five CI workflows PASS at exact code-checkpoint HEAD `8e257eb5cd045c8f2360c7ae7f6bbda74f429f16`; new documentation-HEAD CI, independent final review and merge still required. **VPS-05 overall NOT COMPLETE until merge.**

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

The following results were independently checked against GitHub Actions logs/check-runs at exact HEAD `8e257eb5cd045c8f2360c7ae7f6bbda74f429f16`. They are a **code checkpoint**: a subsequent documentation-only commit must receive its own final-HEAD CI before merge.

| Required gate | Verified result | GitHub workflow |
| --- | --- | --- |
| Ubuntu SDK 10.0.404, warnings-as-errors, locked production restore/build, runtime `.deps.json`, 57-route inventory | **PASS:** Server.Application **366/366**, ASP.NET Core Host **660/660**, dedicated net48→net10 live route differential **293/293**, zero failed/skipped/not-executed; published Host contains **0** Newtonsoft libraries/targets | [Linux #44225748658](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44225748658) |
| Disposable Linux SQL2022 + native HTTPS/TLS and bounded business groups | **PASS:** **15/15** real HTTPS→SQL facts, **24** selected WEB2–WEB6 expected/actual/diff cases in fail-closed WEB6 mode; **3/3** separately enabled WEB6 audit/restart checks. Not protected staging, not 57-route real-SQL parity | [SQL13 #44225748452](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44225748452) |
| Windows Desktop net48 published x86 artifact | **PASS:** build/publish and dependency boundary only; attended Desktop GUI/SQL E2E not run | [Desktop #44225748387](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44225748387) |
| Windows xUnit v3 discovery | **PASS:** topology and all discovery jobs; build/discovery is not full SQL/GUI execution | [xUnit #44225748364](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44225748364) |
| Windows legacy net48 Host, SQL-free regression and independent OWIN observation regeneration | **PASS:** legacy Host **684/684**, dedicated frozen OWIN oracle **1/1** (internally asserts 293 unique observations, 57 IDs), zero failed/skipped/not-executed | [legacy #44225748418](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44225748418) |

The separate `approved-linux-sql` job is intentionally **SKIPPED**, **not PASS**, and is not a required gate for this PR's automatic CI. Protected Linux staging/systemd/reverse proxy/secrets and integrated Windows Desktop → Linux Host → real SQL release remain `NOT VERIFIED` (VPS-08/09).

## Remaining acceptance boundaries

1. **Fresh CI on the final documentation HEAD and independent review remain required before merge.** The code-checkpoint results above do not automatically transfer to a later SHA; cite that final SHA and all five successful workflows in the PR.
2. **VPS-05C and VPS-05 become complete only after PR #17 is merged and that merge is recorded in the roadmap.**
3. **Protected VPS staging/systemd/proxy/secrets and integrated Windows Desktop → Linux Host → real SQL release** remain `NOT VERIFIED` (VPS-08/09). Disposable CI SQL is not production or staging.

The canonical status is [`docs/roadmap/README.md`](../roadmap/README.md). Re-evaluate this ledger on the final code/documentation SHA rather than claiming inherited PASS.
