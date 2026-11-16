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


## VPS-02 — additive endpoints, source parity only (draft PR #3)

The VPS-01 frozen 29 route signatures and 178 WEB2–WEB6 fixtures remain unchanged.
VPS-02 adds [28 route signatures](vps-02-route-additions.json):
20 Catalog CRUD/list/get/active routes and eight Security, Permission and Foundation routes.
Combined source route declarations: **57**. The [Desktop method map](vps-02-desktop-method-matrix.json)
covers **46/46 interface methods** with an explicit source route.
These are SOURCE claims, not live HTTP/SQL acceptance.

| Group | Desktop methods | API rule |
| --- | ---: | --- |
| Product, Warehouse, Employee, Customer | 24 | GET list with search, pageNumber, pageSize, includeInactive; GET /{id}; POST create; PUT /{id}; PUT /{id}/active; frozen GET /lookup |
| Sales | 5 | Frozen WEB2/4/5/6 behavior, no new handlers |
| Goods Receipts | 5 | Frozen WEB3/4 transaction and key semantics |
| Inventory | 2 | Frozen balance and stock card read |
| Identity | 2 | Frozen login and /auth/me. Transport must adapt login's accessToken/expiresAtUtc/user in VPS-03 |
| Permission | 2 | GET /security/permissions/check?key and GET /security/permissions/catalog |
| Security Administration | 5 | GET users/roles, POST mutations and import preview/apply |
| Foundation | 1 | GET /foundation/status?clientName |
| **Total** | **46** | source mapping only |

### Admission and SQL grants

The exact environment flag MYDMSVN_WEB_DESKTOP_API=enabled is required for new Desktop routes.
Absent/disabled returns 404 to an authorized caller; unrecognized values refuse startup.
The existing HTTPS, same-origin, Bearer, session and Application permissions remain mandatory.
The 29 old routes are never gated by this new flag.

The opt-in role MyDmsVnDesktopApiRuntimeRole grants INSERT/UPDATE on four Catalog tables
(Products, Warehouses, Employees, Customers) and EXECUTE on three existing Security
procedures (ApplySecurityAdministration, ReadSecurityPermissionImportTarget,
ApplySecurityPermissionImport); it does not permit direct Security table mutations,
schema DML or database administrator rights. Apply base SQL principal provisioning
before the separate deployment/sql/Provision-MyDmsVnDesktopApiGrants.sql opt-in script
on an approved isolated SQL instance. Startup checks positive and negative rights
and fails closed for missing or unexpected privileges. Roll out host mode, SQL principal
and environment setting together.

Strict JSON, duplicate/unknown field rejection, canonical integer/boolean decoding,
safe generic errors and correlation IDs are preserved. Existing MediatR handler
pipelines authorize/catalog-validate and own all transactions, Security revisions
and auditing. Catalog update methods do not carry optimistic rowversion in the
current Desktop interface: do not falsely advertise Catalog optimistic concurrency.
Never auto-retry an ambiguous non-idempotent write.

**Outstanding:** review and final-HEAD CI, independent HTTP negative tests, real SQL
Catalog and Security transactions/rollback/permissions under the restricted role,
SQL schema mode admission and Desktop-to-HTTP-to-SQL E2E (the latter belongs to VPS-03).
Code or route presence alone cannot mark VPS-02 COMPLETE.

The Desktop interfaces also expose three local ForSession(...) binders (SalesReceipt, PermissionCatalog and SecurityAdministration). They are **not remote HTTP calls**: 49 declared methods consist of 46 server operations and three client-side session-binding helpers, which VPS-03 implements in the adapter. Web API 2 constrains numeric Catalog route parameters with {id:int} so frozen /lookup literal routes remain unambiguous.


Review handoff: the opt-in role provisioning script requires membership in
MyDmsVnWebRuntimeRole **before granting any Desktop object or procedure rights**.
The elevated-permission SQL extension now rejects exact-fragment drift instead
of silently changing the security allowlist. Real SQL execution and live HTTPS
positive/negative cases remain mandatory under [Issue #4](https://github.com/chung6a8m/MyDmsVn-Vps/issues/4).
