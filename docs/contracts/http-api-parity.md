# HTTP feature parity register — worklist, not an acceptance result

**Owner increments:** VPS-01 contract capture; VPS-02 endpoint parity; VPS-03 Desktop HTTP cutover. Current HttpHost is net48 OWIN/Web API 2, and its completeness is **not** certified.

| Desktop feature group | Required operation classes | Status / evidence to collect |
| --- | --- | --- |
| Authentication/session | Login, Bearer, refresh rotation, logout, token expiry | PENDING — request/response/status/auth fixtures |
| Catalog | Product, Warehouse, Customer, Employee CRUD, lookups, active/inactive, paging/keysets, filters, validation | PENDING — each Desktop interface + endpoint |
| Sales | Create/update/get/post, duplicate lines, gifts, manual price, permission, stock/COGS | PENDING — transaction/concurrency/rollback oracle |
| Goods Receipt | Receipt, posting and ledger movement | PENDING — source and persisted ledger parity |
| Inventory | Stock read, adjustments and valuation | PENDING — consistent as-of values and permission |
| Security Admin | Users/roles/permissions, membership and grant enforcement | PENDING — unauthorized/forbidden/allowed |
| Operations/maintenance | Reconciliation, operator commands, audit access | PENDING — locked-down auth/error map |

For each interface method append: Desktop caller, HTTP method/route, request DTO, response DTO, codes, auth scope, pagination/filter semantics, error mapping, SQL side effects, concurrency/idempotency expectations, source test, frozen fixture and observed CI run. **PENDING does not equal missing implementation; actual parity must be inventoried from code and verified.**

Do not change legacy wire behavior during VPS-00B. See [JSON compatibility](json-wire-compatibility.md) and [Desktop HTTP plan](../architecture/desktop-http-architecture.md).

## VPS-01 frozen legacy controller route inventory — source-only

The machine-readable [source attribute-route inventory](vps-01-route-inventory.json) records **29** currently declared method/route/action signatures across nine Web API 2 controllers at source baseline `4503ae3`. A reflection test in `Vps01ContractFreezeTests.cs` rejects additions/removals/renames without explicit inventory review. This is **not** a proof that every Desktop API interface is implemented or that every route passes real HTTP authorization, permission and SQL validation.

| HTTP group | Declared routes | Corpus | Further parity work |
| --- | ---: | --- | --- |
| Health | 1 | N/A | Anonymous liveness verified separately |
| Auth | 5 | WEB2 | Login/refresh/logout cookie-flow; Bearer for me/capabilities |
| Sales Receipts | 5 | WEB2 | Live errors, permissions, posting and idempotency |
| Catalog lookup | 4 | WEB2 | CRUD not implied by lookup-only routes; Desktop coverage gap remains |
| Goods Receipts | 5 | WEB3 | Ledger/stock posting and SQL transaction |
| Inventory | 2 | WEB3 | Balance and stock-card semantics |
| Operations v1 | 2 | WEB4 | Idempotency, retry and fingerprint |
| Operations v2 | 2 | WEB5 | Resolution and terminal states |
| Operations v3 | 3 | WEB6 | Resend eligibility, receipt and authorization |
| **Total** | **29** | **178 pinned WEB2–WEB6 files** | **VPS-02/03 acceptance not claimed** |

`authentication` in the inventory is the current pipeline admission classification, **not** the complete per-action authorization matrix. `verification: route-source-only` prevents conflating static discovery with real HTTP endpoint outcomes.
