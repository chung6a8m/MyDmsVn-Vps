# VPS-10.4A — Native Sales reads

Base: main `11e922ede88cfdd86975b88f16476fba7658e122` (PR #36 merged). Prior PR #36 final source HEAD `5918996fa6d2018f995a6ebf9451c0a8140a4cfb` had 11/11 workflows SUCCESS; this is predecessor evidence, not this increment's acceptance.

## Scope

- GET /api/v1/sales-receipts and GET /api/v1/sales-receipts/{saleId} are owned by native MVC; six native / 51 bridge in 57 frozen route pairs. No write route, feature flag, Desktop source, package lock, SQL schema or Application cursor changes.
- Typed Sales query DTO and route Int64 binder retain raw query allowlist, exact calendar, canonical IDs, default 25 and bound 1..200, query/body/ID precedence, status/search/cursor checks. Application still owns permissions, validation and filter-bound keyset pagination.
- Host-owned response DTOs preserve Int64 and represented decimal strings, null omission, calendar date, SQL UTC and repeated product lines. Native OpenAPI partial document includes both Sales reads; frozen legacy oracle unchanged.
- Source adds 44 focused cases, updates actual endpoint ownership and existing native OpenAPI test, preserves frozen 293 variants. Existing disposable SQL group oracle exercises Sales list/get/not-found through real HTTP; no SQL query implementation changed.

## Verification

| Gate | Status at source preparation |
| --- | --- |
| GitHub predecessor merge and exact source SHA | VERIFIED |
| Local .NET build / restore / focused test / G1 / G2 | NOT RUN — editing environment lacks .NET SDK and repository clone access |
| G3 Windows net48 paired Desktop artifact and regressions | NOT RUN locally — no Windows runner |
| Disposable SQL existing workflows | NOT RUN locally |
| Independent review + final-HEAD CI + merge | REQUIRED / pending |
| Protected staging / attended GUI / production | NOT VERIFIED; VPS-08/09 obligations unchanged |

CI must require 44/44 targeted cases, 34 existing pilot/ownership/OpenAPI cases, 37 characterization cases, 293/293 frozen differential, actual 57 routes (6 native/51 bridge), full native/Application regressions, Windows paired build and existing mandatory workflows. Exact HEAD, run links and test receipts must be recorded in this PR; do not promote predecessor results to current evidence.
