# VPS-05C — System.Text.Json production-runtime isolation and 57-route live wire gate

**Repository:** `chung6a8m/MyDmsVn-Vps` · **Issue:** [#15](https://github.com/chung6a8m/MyDmsVn-Vps/issues/15) · **Implementation:** [PR #17](https://github.com/chung6a8m/MyDmsVn-Vps/pull/17)
**Baseline:** VPS-05B merged `main` `f2eeb62f19a5cb8177eb769d01dba4003a2621d8`.
**Status:** **COMPLETE / MERGED within VPS-05C scope.** Independent final review and 5/5 mandatory workflows passed on exact final PR HEAD `1230efdf06a60412da077ce9d85f0d44cb0b5913`; PR #17 merged to `main` as `b4f7cb64e5474203fe0f132c39286b1d7c822e61` on 2026-11-18 03:06:44 UTC. VPS-08/09 release gates remain NOT VERIFIED.

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

## Independent final review, final-HEAD CI and merge (2026-11-18)

The earlier `8e257eb` results above are historical implementation-checkpoint evidence, **not** the authoritative final review gate. PR #17 was reviewed on exact final HEAD `1230efdf06a60412da077ce9d85f0d44cb0b5913` (documentation-only correction after `8e257eb`). Review established that blockers B1 (live 57-route parity), B2 (evidence linkage) and F-01 (stale 279-case count and pending-CI wording) were resolved; [PR #17 final review](https://github.com/chung6a8m/MyDmsVn-Vps/pull/17) recorded **ACCEPT for VPS-05C merge scope only**, not release approval.

| Required workflow, exact PR #17 final HEAD | Evidence | Verified conclusion |
| --- | --- | --- |
| Linux SDK 10.0.404, warning-as-error net10, runtime dependency and route gates | [run #44227546692](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44227546692) | **SUCCESS:** Application **366/366**; Host **660/660**; live SQL-free net48 OWIN vs net10 Kestrel **293/293** across 57 routes; published net10 `.deps.json` **0 Newtonsoft.Json**; no required test failed/skipped |
| Disposable Linux SQL Server 2022, Kestrel HTTPS/TLS | [run #44227546705](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44227546705) | **SUCCESS:** **15/15** bounded real HTTPS→SQL13 tests, 24 selected WEB2–WEB6 SQL oracle scenarios and 3 separately enabled WEB6 security/recovery facts; **not protected staging**, **not all-route real-SQL** |
| Windows Desktop net48 publish/dependency boundary | [run #44227546706](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44227546706) | **SUCCESS:** publish and dependency audit; **not attended GUI/SQL E2E** |
| Windows xUnit v3 compile/discovery topology | [run #44227546729](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44227546729) | **SUCCESS:** 10/10 discovery jobs; discovery-only suites are not SQL or GUI execution |
| Windows legacy net48 foundation, SQL-free smoke and independent OWIN oracle | [run #44227546680](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44227546680) | **SUCCESS:** 8/8 jobs; legacy HTTP Host **684/684**; net48 OWIN oracle **1/1**, asserting 293 observations / 57 routes |

GitHub reports **five workflows SUCCESS on exact SHA `1230efdf`**; the optional protected `approved-linux-sql` job in the SQL-free workflow is **SKIPPED / NOT PASS**. No required acceptance job is treated as successful from an earlier commit. The **merge is separately verified**: [PR #17](https://github.com/chung6a8m/MyDmsVn-Vps/pull/17) closed as merged at `b4f7cb64e5474203fe0f132c39286b1d7c822e61` (2026-11-18 03:06:44 UTC). These are PR final-HEAD CI results, not an assertion of fresh CI on the merge commit or on this later documentation-only closeout commit.

## Remaining release obligations — outside VPS-05

- **VPS-06 / VPS-07 NOT STARTED:** Mediator 3.x and FluentMigrator conversions remain separate increments with their own independent review, CI and SQL/behavior oracles.
- **VPS-08 NOT VERIFIED** ([Issue #11](https://github.com/chung6a8m/MyDmsVn-Vps/issues/11)): independently approved protected staging, systemd, reverse proxy/TLS/secrets, real VPS deployment and restart/rollback rehearsals. A skipped `approved-linux-sql` job is never a staging PASS.
- **VPS-09 NOT VERIFIED** ([Issue #12](https://github.com/chung6a8m/MyDmsVn-Vps/issues/12)): exhaustive *real-SQL* dynamic responses on all 57 routes, fault/race/stock/COGS recovery and attended Windows Desktop→deployed Linux Host→SQL integrated release. The **293/293 live differential is SQL-free**; only the **15/15** bounded tests use disposable SQL, and neither establishes production readiness.
- No production deployment, release approval or acceptance exception is inferred. The only live workstream status authority remains [`docs/roadmap/README.md`](../roadmap/README.md); this file is immutable-scope evidence for PR #17 and the subsequent documentation closeout.
