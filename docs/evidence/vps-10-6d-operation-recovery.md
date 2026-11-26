# VPS-10.6D — Native operation recovery evidence

Base: `fdd04fea9089706a5394dcd71d1ecf1bc3a10565` (merged PR #43). Branch: `vps-10.6d-native-operations`.

## Source changes for review

- Seven native MVC endpoints: v1 context/status, v2 context/resolution, v3 resend context/receipt/eligibility. Expected 57 native, zero bridge; transitional source links and auxiliary fallback remain until VPS-10.7.
- Strict raw path/header parsing; admitted actor/session, original flag precedence, repository authorization and transaction retained. Response is typed and serialized before committing read transaction. Status discloses validated stored outcome rather than querying the business document again.
- Native V1OperationResultCodec implements the same size, shape, decimal/identity/terminal validation and uses native Sales/Receipt projections. Original codec retained for differential comparison. Factory for native writes switches atomically. WEB6 receipt signing/validation and authorization implementation retained.
- No Desktop source, SQL migration, SDK, package or automatic resend policy change. Existing Desktop pending/restart, restricted principal, real HTTPS/SQL and protected audit gates remain necessary.
- Frozen 293-case differential unchanged; new focused 24-case gate (12 HTTP, 12 codec). Actual endpoint inventory and Development OpenAPI path assertions updated.

## Gate ledger

| Gate | Status |
| --- | --- |
| G1 Linux SDK 10.0.404 locked build, 57 routes, focused 24 cases, all Host/Application regressions | NOT RUN locally |
| G2 frozen differential 293/293 and OpenAPI | NOT RUN locally |
| G3 Windows net48 Desktop pending/replay and upgrade regression | NOT RUN locally |
| G4 disposable real HTTPS/SQL including enabled WEB6 and protected audit restart | NOT RUN locally |
| Independent review, exact final HEAD CI, merge | PENDING |

GitHub connector-only implementation; no checkout, SDK, SQL or attended Desktop session in this environment. No runtime PASS or physical upgrade evidence claimed. Pending operation store is not changed. VPS-08/09 protected staging and release remain NOT VERIFIED. The final reviewed commit and mandatory CI receipts must be recorded in the PR; this document does not authorize production or VPS-10.7.
