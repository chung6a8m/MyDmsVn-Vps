# VPS-10.5A — Native Receipt/Inventory reads

Base: main `ade2f7cb0f67f4cd0945f673d41d56df6c1b2e77` ([PR #38](https://github.com/chung6a8m/MyDmsVn-Vps/pull/38) merged). Predecessor final HEAD `8e491593754f372e295e2b48aad7839d1f6fccb5`: 11 required workflows succeeded per merge receipt. Not current acceptance.

## Scope

- Native GET /api/v1/goods-receipts, GET /api/v1/goods-receipts/{receiptId}, GET /api/v1/inventory/balances, GET /api/v1/inventory/stock-card. Exact endpoint inventory requires 13 native / 44 bridge, 57 total. Receipt writes remain bridge-owned until VPS-10.5B.
- Host-owned typed query DTOs, scoped binder and response records. Retain raw query grammar and precedence, required offset pagination, optional IDs/filter/text, exact UTC bounds, canonical Int64 path, required explicit JSON nulls, WEB3 fixed F4/F8 text and signed movements. Application handlers, permissions, SQL schema, count/items concurrency semantics, and Desktop net48 are unchanged.
- Preserve legacy response invariant checks (receipt number/version/line uniqueness/status, signed movement, zero balance and null average, numeric range/precision, strict text). No legacy DOM mapper invoked by the four native actions.
- 63 focused cases in Vps10InventoryReadTests, including real loopback Kestrel capture with four request cultures and opt-in null/zero/count fixture. Frozen 293 differential variants, fixture defaults and prior focused counts remain mandatory. The existing disposable SQL WEB3 independent Oracle already probes all four routes and missing receipt; CI now asserts all five exact report IDs and existing report diff/SQL checks. The native OpenAPI document remains development-only.
- No package lock, legacy host, Application, persistence, Desktop wire or release policy change. Two new net10 generated Mediator controller paths are excluded only from the frozen historical MediatR lexical scan.

## Verification

| Gate | Status at implementation |
| --- | --- |
| GitHub predecessor merge and source commit | VERIFIED via GitHub; runtime commit `09ecc1c80599ae11a19241e1ee967fcdccbead00` |
| G1/G2 .NET restore/build/focused and full tests | NOT RUN locally — no dotnet SDK; git ls-remote failed DNS in editing container |
| G3 Windows net48 paired Desktop build and tests | NOT RUN locally — no Windows runner |
| Disposable HTTPS-to-SQL13 WEB3 targeted read Oracle and existing G4 regressions | NOT RUN locally — require final-HEAD CI |
| Independent review, exact final-HEAD required CI and merge | REQUIRED / pending |
| Protected staging, attended UI, production | NOT VERIFIED; VPS-08/09 unchanged |

CI must require 63/63 targeted cases, existing 34 lookup/ownership/OpenAPI, 37 characterization, 48 Sales reads, 43 Sales writes, 8 canonical tests and 293 independent live differential variants. Require all existing mandatory workflows and SQL projection receipts; do not treat a previous HEAD, static inspection or skipped test as current acceptance. Record exact HEAD/run URLs and test counts in the implementation PR.

## Second-pass source review (2026-11-26)

Reviewed all 23 initial changed files against base `ade2f7cb0f67f4cd0945f673d41d56df6c1b2e77`; initial source HEAD `887cc9fe2b90799ec8a5e86dbb7d5bec69d81407`. GitHub PR runs checked out merge-test SHA `59809b026d97a02186ce52895ce9fb5e66825b0c` with that exact source and base. All 11 workflows SUCCESS on the initial HEAD only.

- Binding review: body/query/ID and scalar precedence match InventoryWireInput; UTF-8, duplicate keys, required page parameters and canonical UTC are preserved. Mapping review: all InventoryWireOutput guards retained, including count < items.Length, explicit nulls and fixed scale. Application and permissions unchanged; route manifest diff changes only ownership/controller for the four GETs; all source pins and remaining entries intact.
- F01 (P2): partial OpenAPI overview was ambiguous: pagination wording included receipt detail and could imply pageNumber is capped at 200. Clarify collection-only pagination, separate numeric bounds, and detail path/query/body restrictions. Strengthen the existing OpenAPI test to assert those bounds and description, plus the required nullable types and F4/F8 patterns on actual generated schemas. No runtime contract change.
- F02 (P2): live status row lacked a PR reference or merge-conditioned completion; link PR #39 and use the established conditional status, avoiding stale pending status after merge.

Initial-HEAD receipts (not final-HEAD acceptance):

| Gate | Actual receipt |
| --- | --- |
| G1 / G2 Linux SDK 10.0.404 | [run 45765507723](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45765507723): Application 366/366, Host 958/958, local simulation 17/17, 37 characterization, 26 composition, 34 lookup/ownership/OpenAPI, 48 Sales read, 63 Receipt/Inventory read, 43 Sales write, 8 canonical, 293 frozen differential; each zero failed/skipped; 57 compiled routes, 13 native/44 bridge, six frozen pins unchanged |
| G3 Windows net48 | [legacy 45765507576](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45765507576): Desktop.Session 276/276 and typed wire 38/38; [discovery 45765507620](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45765507620): culture/input 37/37 + 17/17; [published Desktop 45765507567](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45765507567): 23 lock entries, 35 files, zero forbidden runtime dependencies |
| Targeted disposable SQL | [run 45765507732](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45765507732): isolated SQL2022 TLS schema13, 16/16 (11 Oracle + 5 grouped), 24 explicit projections including all five required WEB3 IDs, 3/3 generated Mediator SQL, 3/3 WEB6 runtime/recovery, 2/2 Sales restart; zero failed/skipped in executed selectors |
| Other required source workflows | [45765507585](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45765507585), [45765507655](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45765507655), [45765507717](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45765507717), [45765507634](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45765507634), [45765507659](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45765507659), [45765507556](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45765507556): SUCCESS |

Final review-fix HEAD must pass every required workflow again. Final SHA, checkout SHA and receipts belong in PR #39 conversation/merge record to avoid recursive evidence commits. Protected `approved-linux-sql`, VPS-08 staging and VPS-09 physical/attended gates were SKIPPED / NOT VERIFIED, outside the VPS-10.5A increment. AI second-pass review is not independent human review. No local execution or deployment claim.
