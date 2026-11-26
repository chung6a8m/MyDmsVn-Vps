# VPS-10.4A — Native Sales reads

Base: main `11e922ede88cfdd86975b88f16476fba7658e122` (PR #36 merged). Prior PR #36 final source HEAD `5918996fa6d2018f995a6ebf9451c0a8140a4cfb` had 11/11 workflows SUCCESS; this is predecessor evidence, not this increment's acceptance.

## Scope

- GET /api/v1/sales-receipts and GET /api/v1/sales-receipts/{saleId} are owned by native MVC; six native / 51 bridge in 57 frozen route pairs. No write route, feature flag, Desktop source, package lock, SQL schema or Application cursor changes.
- Typed Sales query DTO and route Int64 binder retain raw query allowlist, exact calendar, canonical IDs, default 25 and bound 1..200, query/body/ID precedence, status/search/cursor checks. Application still owns permissions, validation and filter-bound keyset pagination.
- Host-owned response DTOs preserve Int64 and represented decimal strings, null omission, calendar date, SQL UTC and repeated product lines. Native OpenAPI partial document includes both Sales reads; frozen legacy oracle unchanged.
- Source adds 48 focused cases, updates actual endpoint ownership and existing native OpenAPI test, preserves frozen 293 variants. Existing disposable SQL group oracle exercises Sales list/get/not-found through real HTTP; no SQL query implementation changed.

## Verification

| Gate | Status at source preparation |
| --- | --- |
| GitHub predecessor merge and exact source SHA | VERIFIED |
| Local .NET build / restore / focused test / G1 / G2 | NOT RUN — editing environment lacks .NET SDK and repository clone access |
| G3 Windows net48 paired Desktop artifact and regressions | NOT RUN locally — no Windows runner |
| Disposable SQL existing workflows | NOT RUN locally |
| Independent review + final-HEAD CI + merge | REQUIRED / pending |
| Protected staging / attended GUI / production | NOT VERIFIED; VPS-08/09 obligations unchanged |

CI must require 48/48 targeted cases, 34 existing pilot/ownership/OpenAPI cases, 37 characterization cases, 293/293 frozen differential, actual 57 routes (6 native/51 bridge), full native/Application regressions, Windows paired build and existing mandatory workflows. Exact HEAD, run links and test receipts must be recorded in this PR; do not promote predecessor results to current evidence.

## Second-pass review (2026-11-26)

Initial HEAD `c71f1ca3f9081378c39a8ebde446d4ec00d706b6`: 9/11 workflows succeeded, 2 failed. Linux Host build and Application regressions succeeded; full Host suite 838/840 passed, zero skipped. [Linux run](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45758783177) failed route-template count (48 vs 47) and OpenAPI nullable generic data schema. [Mediator canary](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45758783051) rejected native IMediator lexical drift (+1 injection/+2 sends). [Disposable SQL](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45758783032) succeeded: 16/16 base+group facts, 24 grouped parity projections, 3/3 generated Mediator oracle and 3/3 enabled/negative WEB6 facts. These are initial-HEAD results only.

| Finding | Review fix |
| --- | --- |
| F01 (P1) Mixed route count compared raw MVC/bridge template spelling | Compare normalized distinct paths to every dedicated method-miss template; test Sales collection/detail HTTP 405 and Allow values. |
| F02 (P1) Generated OpenAPI success data permits null/oneOf and path ID could be interpreted as unsafe JS number | Explicit non-null Sales success data, exact required vs omitted properties, timestamp format, canonical Int64 path text. Extend actual development document test; no frozen expected payload change. |
| F03 (P1) Frozen legacy lexical inventory scans native-only controller | Exclude only this net10 generated-Mediator controller, consistent with lookup pilot. Keep all six historical expected counters unchanged. |
| F04 (P2) Culture vectors did not demonstrate culture inside Kestrel handler; page/cursor coverage gaps | Set and restore request culture in opt-in fixture middleware, observe handler culture; add minimum page/trimmed search and opaque page cursor mapping cases. Preserve frozen 293 variant data and selectors. |

Review fixes require 48/48 focused cases and all existing final-HEAD workflow gates. Local compilation/tests unavailable; AI second-pass review is not independent human acceptance.
