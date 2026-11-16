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
