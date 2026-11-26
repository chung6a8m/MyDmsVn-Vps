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
