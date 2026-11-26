# VPS-10.4B — Native Sales writes and canonical operation context

Base: main `deae0d104334b6907f6b015170c23066118b6ee8` (PR #37 merged). Prior final HEAD `1d103b2d1ad2028dd197261f64f75d2e7577e6d3` had 11/11 required workflows SUCCESS. Those receipts are predecessor evidence only.

## Scope

- POST collection, PUT draft, POST post are native MVC. Actual endpoint inventory and active manifest now expect 9 native / 48 bridge; 57 total frozen verb/path pairs.
- Host-owned typed Sales requests; route-scoped native MVC System.Text.Json input formatter parses the admitted bounded body once. Explicit field traversal preserves legacy multi-error ordering, missing/false/null distinctions, nested path, Sales version and decimal rules. Request source is registered for disposal; only operation context reads it. Other route families retain their existing formatter/bridge. This is an intentional narrow formatter instead of default STJ property-order error semantics.
- Native canonicalizer implements web4-v1 from validated source (not serialized DTO), retains canonical UTF-8 and SHA-256; factory uses admitted session and exact request path, feature flags, GUID header grammar, existing WebOperationResultCodec. No Application, SQL schema, Desktop target, package lock, frozen corpus or legacy host change.
- Two focused classes add 43 HTTP/typed write cases and 8 canonical/codec cases (20 independent Sales vectors per culture); existing baseline 293 differential and 48 read/34 lookup/37 characterization/26 composition tests remain mandatory. The test fixture extension is opt-in; frozen callers unchanged.
- Existing disposable HTTPS→SQL13 gates exercise actual native Sales create/update/post, invalid no-write, stale epoch/version, same-key concurrent create/replay, conflict, oversell, rollback and same-key recovery. A new 2-phase Sales.Create recovery test runs before/after the actual Kestrel process restart, using a fresh session for the same actor, SQL ResultBytes/fingerprint/hash, and exactly one receipt by unique note. The initial client intentionally does not consume the result (not a transport-level dropped ACK). Temporary state contains no credentials, is owner-only, and is excluded from uploads. No protected staging, production or attended UI acceptance.

## Verification status at source preparation

| Gate | Status |
| --- | --- |
| Base merge and atomic source diff | VERIFIED via GitHub |
| G1/G2 focused and full .NET build/test | NOT RUN locally — no .NET SDK or repository clone access |
| G3 Windows net48 paired artifact/tests | NOT RUN locally |
| G4 disposable SQL13 including existing rollback/recovery/concurrency | NOT RUN locally; require workflow receipts on final HEAD |
| Actual process-restart Sales operation proof | Two separate real HTTPS/SQL test phases added around the existing worker restart; NOT RUN locally. Require exact 1/1 before and 1/1 after TRX. |
| Independent review and required exact-HEAD CI/merge | REQUIRED / pending |

The CI lane must require non-empty exact 43/43 and 8/8 targeted test receipts; keep existing frozen selectors/counts and independent SQL oracles. Review OpenAPI schema/runtime equivalence and replay byte equality before acceptance. All CI status and discovered/executed/passed/failed/skipped counts belong to the implementation PR; no predecessor PASS or source inspection is acceptance. VPS-08/09 remain unchanged.
