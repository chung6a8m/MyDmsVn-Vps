# VPS-10.6D — Native operation recovery evidence

Base: `fdd04fea9089706a5394dcd71d1ecf1bc3a10565` (merged PR #43). Branch: `vps-10.6d-native-operations`.

## Source changes for review

- Seven native MVC endpoints: v1 context/status, v2 context/resolution, v3 resend context/receipt/eligibility. Expected 57 native, zero bridge; transitional source links and auxiliary fallback remain until VPS-10.7.
- Strict raw path/header parsing; admitted actor/session, original flag precedence, repository authorization and transaction retained. Response is typed and serialized before committing read transaction. Status discloses validated stored outcome rather than querying the business document again.
- Native V1OperationResultCodec implements the same size, shape, decimal/identity/terminal validation and uses native Sales/Receipt projections. Original codec retained for differential comparison. Factory for native writes switches atomically. Six frozen Sales/Receipt outcomes and refusal/replay paths are included in the focused gate. WEB6 receipt signing/validation and authorization implementation retained.
- No Desktop source, SQL migration, SDK, package or automatic resend policy change. Existing Desktop pending/restart, restricted principal, real HTTPS/SQL and protected audit gates remain necessary.
- Frozen 293-case differential unchanged; new focused 52-case gate (37 HTTP, 15 codec). Actual endpoint inventory and Development OpenAPI path assertions updated.

## Gate ledger

| Gate | Status |
| --- | --- |
| G1 Linux SDK 10.0.404 locked build, 57 routes, focused 52 cases, all Host/Application regressions | NOT RUN locally |
| G2 frozen differential 293/293 and OpenAPI | NOT RUN locally |
| G3 Windows net48 Desktop pending/replay and upgrade regression | NOT RUN locally |
| G4 disposable real HTTPS/SQL including enabled WEB6 and protected audit restart | NOT RUN locally |
| Independent review, exact final HEAD CI, merge | PENDING |

GitHub connector-only implementation; no checkout, SDK, SQL or attended Desktop session in this environment. No runtime PASS or physical upgrade evidence claimed. Pending operation store is not changed. VPS-08/09 protected staging and release remain NOT VERIFIED. The final reviewed commit and mandatory CI receipts must be recorded in the PR; this document does not authorize production or VPS-10.7.

## Independent review — initial findings and correction

- Initial implementation HEAD `9d38664632762464aa1f90d611da3c7fe21366ab`: [Linux route workflow](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45788270680) failed with Host 1224/1226 (zero skipped): characterization still expected bridge ownership, and method-miss test used a bridge-only raw template spelling for the migrated WEB6 route. Application 366/366. Corrected in `279f47d8cf18c467264f963d4e0bcfa3e0b5e19d`; no route/order/owner/count invariant removed.
- Coverage finding: the new focused suite lacked explicit v1/v2 terminal, expired/unavailable, corrupt stored outcome and document-ID mismatch cases, and did not assert WEB6 refusal for all known terminal/committed outcomes. Added 17 named HTTP cases using new opt-in repository oracle states, preserving the original 293 frozen cases and existing expectations. Required focused count raised from 35 to 52 (37 HTTP + 15 codec).
- Final corrected HEAD CI and actual paired Desktop/Host upgrade with unresolved pending state remain to be verified; no exception, no merge approval or physical acceptance asserted here.
