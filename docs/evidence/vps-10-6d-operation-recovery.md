# VPS-10.6D — Native operation recovery evidence

Base: `fdd04fea9089706a5394dcd71d1ecf1bc3a10565` (merged PR #43). Branch: `vps-10.6d-native-operations`.

## Source changes for review

- Seven native MVC endpoints: v1 context/status, v2 context/resolution, v3 resend context/receipt/eligibility. Actual final-reviewed inventory: 57 native, zero bridge; transitional source links and auxiliary fallback remain until VPS-10.7.
- Strict raw path/header parsing; admitted actor/session, original flag precedence, repository authorization and transaction retained. Response is typed and serialized before committing read transaction. Status discloses validated stored outcome rather than querying the business document again.
- Native V1OperationResultCodec implements the same size, shape, decimal/identity/terminal validation and uses native Sales/Receipt projections. Original codec retained for differential comparison. Factory for native writes switches atomically. Six frozen Sales/Receipt outcomes and refusal/replay paths are included in the focused gate. WEB6 receipt signing/validation and authorization implementation retained.
- No Desktop source, SQL migration, SDK, package or automatic resend policy change. Existing Desktop pending/restart, restricted principal, real HTTPS/SQL and protected audit gates remain necessary.
- Frozen 293-case differential unchanged; new focused 52-case gate (37 HTTP, 15 codec). Actual endpoint inventory and Development OpenAPI path assertions updated.

## Gate ledger

| Gate | Status |
| --- | --- |
| G1 Linux SDK 10.0.404 locked build, 57 routes, focused 52 cases, all Host/Application regressions | PASS in PR CI: 366/366 Application; 1243/1243 Host; 52/52 Operations; 57 native / 0 bridge |
| G2 frozen differential 293/293 and OpenAPI | PASS in PR CI: 293/293, native OpenAPI and coverage checks |
| G3 Windows net48 Desktop pending/replay and upgrade regression | CI portion PASS: 276/276 Desktop.Session, 38/38 typed wire, Desktop x86 publish and discovery; paired upgrade U01–U06 NOT RUN / NOT VERIFIED, Owner-accepted deferral in #45 |
| G4 disposable real HTTPS/SQL including enabled WEB6 and protected audit restart | PASS in disposable CI scope: 16/16 HTTPS/SQL, enabled WEB6/audit 3/3, Sales restart 2/2, Receipt recovery 3/3, Journal Bridge 58/58 |
| Independent review, exact final HEAD CI, merge | F01–F04 RESOLVED; 11/11 PR workflows SUCCESS on `4207e9bccaaa2ef45b04bf905b6c10bf3f1bcdd5`; SOURCE_ONLY MERGED at `922bdc8e7fda24cd53570cccbac7809d305088ed` |

Implementation and source review were performed through the GitHub connector; no independent local SDK, SQL or attended Desktop session. Executed CI receipts below are separate from local and physical acceptance. Pending operation store is unchanged. VPS-08/09 protected staging and release remain NOT VERIFIED. Owner approval permits this source merge and subsequent source development only; it does not waive future increment gates.

## Independent review — initial findings and correction

- Initial implementation HEAD `9d38664632762464aa1f90d611da3c7fe21366ab`: [Linux route workflow](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45788270680) failed with Host 1224/1226 (zero skipped): characterization still expected bridge ownership, and method-miss test used a bridge-only raw template spelling for the migrated WEB6 route. Application 366/366. Corrected in `279f47d8cf18c467264f963d4e0bcfa3e0b5e19d`; no route/order/owner/count invariant removed.
- Coverage finding: the new focused suite lacked explicit v1/v2 terminal, expired/unavailable, corrupt stored outcome and document-ID mismatch cases, and did not assert WEB6 refusal for all known terminal/committed outcomes. Added 17 named HTTP cases using new opt-in repository oracle states, preserving the original 293 frozen cases and existing expectations. Required focused count raised from 35 to 52 (37 HTTP + 15 codec).
- Final corrected HEAD CI and actual paired Desktop/Host upgrade with unresolved pending state remain to be verified; no exception, no merge approval or physical acceptance asserted here.

## F04 — compiled route verification regression

- Reviewed HEAD `d5153d9028eb60b59684a43f9fe4113a4bf40bf9`: [Linux workflow 45789139060](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45789139060) executed Application 366/366, Host 1243/1243, focused Operations 52/52, frozen differential 293/293, all zero failed/skipped. Compiled inventory export succeeded, but `scripts/verify-vps-04a.py` still asserted exactly 50 native owners (VPS-10.6C). The following ledger/optional SQL compile steps did not run.
- Correct the independent phase expectation to require all 57 manifest AND compiled route owners native; retain the frozen 57 controller/action pairs and all per-route order, active-owner, actual-controller and actual-action comparisons. No frozen expectation changed. The workflow must rerun on the new reviewed HEAD.
- [Disposable HTTPS/SQL 45789139449](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45789139449) succeeded at the superseded HEAD: real HTTPS/SQL 16/16, generated Mediator oracle 3/3, Catalog 1/1, Security 2/2, enabled WEB6/audit 3/3, Sales restart 2/2, Receipt recovery 3/3. 10/11 automated workflows successful; no paired upgrade receipt or Owner exception. Historical results are not final-HEAD acceptance.

## Final source-only merge receipt — 2026-11-26

- Reviewed HEAD `4207e9bccaaa2ef45b04bf905b6c10bf3f1bcdd5`, base `fdd04fea9089706a5394dcd71d1ecf1bc3a10565`; [final exact-HEAD CI receipt](https://github.com/chung6a8m/MyDmsVn-Vps/pull/44#issuecomment-7188225725): 11/11 workflows completed SUCCESS, VPS-01 attempt 2 (Desktop discovery 813/813); other ten attempt 1. See linked receipts for commands, test counts and environment. Required executed suites reported zero failures/skips. Conditional protected gates remain NOT VERIFIED.
- [Owner approval](https://github.com/chung6a8m/MyDmsVn-Vps/pull/44#issuecomment-7188352813) on 2026-11-26: explicit acceptance of SOURCE_ONLY merge and deferral of paired Desktop/Host upgrade to technical debt. [PR #44](https://github.com/chung6a8m/MyDmsVn-Vps/pull/44) merged into main at `922bdc8e7fda24cd53570cccbac7809d305088ed`. No full VPS-10.6D acceptance, no production or deployment approval, no future gate waived.
- [Issue #45](https://github.com/chung6a8m/MyDmsVn-Vps/issues/45) remains OPEN: U01–U06 actual paired upgrade with unresolved pending operations NOT RUN / NOT VERIFIED. Maintain stable origin, epoch, SQL and Windows owner/SID; validate identity, no duplicate business effect or unsafe resend, signed receipt and protected audit, preserve evidence and cleanup owned resources. Source-only merge is not a passing result for this obligation.

This closeout-only documentation follow-up records already-observed PR #44 facts, not new runtime evidence; its own CI must be evaluated independently before merging.
