# VPS-04A — WEB2–WEB6 Linux HTTPS response coverage inventory (PR #9)

**Scope:** frozen source route inventories `docs/contracts/vps-01-route-inventory.json` (29) and `docs/contracts/vps-02-route-additions.json` (28), **57 compiled signatures**. Updated for code checkpoint `62544462326e413474f7d402f851a4bcf8fd4c2f` (2026-11-17); exact final documentation HEAD CI evidence will be recorded in PR #9. This is a **coverage ledger**, not a claim of exhaustive live response parity. The canonical live roadmap status remains `docs/roadmap/README.md`.

## Evidence definitions

- `GROUP_EAD_SQL`: endpoint reached in new disposable Linux Kestrel HTTPS SQL-backed WEB2–WEB5 group tests; **expected/actual/diff are bounded selected response fields/statuses and independent SQL rows**, not every JSON property or every query/body variant. Source-frozen fixture files identify the expected contract for selected cases; dynamic synthetic SQL values are independent.
- `PRIOR_LIVE_BOUNDED`: reached in existing 10/10 disposable HTTPS/SQL acceptance (6 keyed WEB4 Sales/Receipt mutations) or auth/health prerequisites. **Does not have new per-route expected/actual/diff artifacts**. Do not count as exhaustive parity.
- `DISABLED_404`: WEB6 route reached over real HTTPS in WEB5 mode and verified to fail closed with JSON 404, plus unchanged operation journal. **Not a WEB6-enabled positive response acceptance**: protected Linux WEB6 audit remains blocked.
- `ROUTE_ONLY`: source signature compiled 57/57 but **new group live HTTP coverage not established**. No live JSON response parity claim.

## Count by source group

| Group | Frozen routes | New live expected/actual/diff | Previous bounded live | Disabled 404 (WEB6) | Route-only / still open |
| --- | ---: | ---: | ---: | ---: | ---: |
| WEB2 | 15 | 8 | 7 | 0 | 0 |
| WEB3 | 7 | 4 | 3 | 0 | 0 |
| WEB4 | 2 | 2 | 0 | 0 | 0 |
| WEB5 | 2 | 2 | 0 | 0 | 0 |
| WEB6 | 3 | 0 | 0 | 3 | 0 |
| VPS-02 | 28 | 0 | 0 | 0 | 28 |
| **TOTAL** | **57** | **16** | **10** | **3** | **28** |

**Scenario evidence:** the new suite executes **24 scenarios (WEB2 9, WEB3 5, WEB4 4, WEB5 3, WEB6 3)** across 16 enabled-response routes and 3 deliberately disabled WEB6 routes; the SQL CI runs 5 new xUnit facts plus 10 previously proven cases (**15/15**). Multiple scenarios may share a route. Do not equate 24 scenarios, 57 compiled routes or 178 frozen corpus files with 178 runnable payload replays.

**Safe artifacts:** `vps04a-ci-group-parity` contains `WEB2.json` through `WEB6.json`, each with `group`, `scope`, `mode`, `cases[].{id,route,fixture,sqlOracle,expected,actual,diff}`. The validator rejects any missing group, scenario, field or nonempty diff. These are **allowlisted projections** only, not raw HTTP responses, JWTs or cookies.

## Complete source route inventory

| Family | Method + route | Frozen controller.action | Verification |
| --- | --- | --- | --- |
| WEB2 | `GET /api/v1/health` | `HealthController.Get` | `PRIOR_LIVE_BOUNDED` |
| WEB2 | `POST /api/v1/auth/login` | `AuthController.Login` | `PRIOR_LIVE_BOUNDED` |
| WEB2 | `POST /api/v1/auth/refresh` | `AuthController.Refresh` | `PRIOR_LIVE_BOUNDED` |
| WEB2 | `POST /api/v1/auth/logout` | `AuthController.Logout` | `PRIOR_LIVE_BOUNDED` |
| WEB2 | `GET /api/v1/auth/me` | `AuthController.Me` | `GROUP_EAD_SQL` |
| WEB2 | `GET /api/v1/auth/capabilities` | `AuthController.Capabilities` | `GROUP_EAD_SQL` |
| WEB2 | `GET /api/v1/sales-receipts` | `SalesReceiptsController.List` | `GROUP_EAD_SQL` |
| WEB2 | `GET /api/v1/sales-receipts/{saleId}` | `SalesReceiptsController.Get` | `GROUP_EAD_SQL` |
| WEB2 | `POST /api/v1/sales-receipts` | `SalesReceiptsController.Create` | `PRIOR_LIVE_BOUNDED` |
| WEB2 | `PUT /api/v1/sales-receipts/{saleId}/draft` | `SalesReceiptsController.Update` | `PRIOR_LIVE_BOUNDED` |
| WEB2 | `POST /api/v1/sales-receipts/{saleId}/post` | `SalesReceiptsController.Post` | `PRIOR_LIVE_BOUNDED` |
| WEB2 | `GET /api/v1/catalog/products/lookup` | `CatalogLookupController.Products` | `GROUP_EAD_SQL` |
| WEB2 | `GET /api/v1/catalog/customers/lookup` | `CatalogLookupController.Customers` | `GROUP_EAD_SQL` |
| WEB2 | `GET /api/v1/catalog/warehouses/lookup` | `CatalogLookupController.Warehouses` | `GROUP_EAD_SQL` |
| WEB2 | `GET /api/v1/catalog/employees/lookup` | `CatalogLookupController.Employees` | `GROUP_EAD_SQL` |
| WEB3 | `GET /api/v1/goods-receipts` | `GoodsReceiptsController.List` | `GROUP_EAD_SQL` |
| WEB3 | `GET /api/v1/goods-receipts/{receiptId}` | `GoodsReceiptsController.Get` | `GROUP_EAD_SQL` |
| WEB3 | `POST /api/v1/goods-receipts` | `GoodsReceiptsController.Create` | `PRIOR_LIVE_BOUNDED` |
| WEB3 | `PUT /api/v1/goods-receipts/{receiptId}/draft` | `GoodsReceiptsController.Update` | `PRIOR_LIVE_BOUNDED` |
| WEB3 | `POST /api/v1/goods-receipts/{receiptId}/post` | `GoodsReceiptsController.Post` | `PRIOR_LIVE_BOUNDED` |
| WEB3 | `GET /api/v1/inventory/balances` | `InventoryController.Balances` | `GROUP_EAD_SQL` |
| WEB3 | `GET /api/v1/inventory/stock-card` | `InventoryController.StockCard` | `GROUP_EAD_SQL` |
| WEB4 | `GET /api/v1/operations/context` | `OperationsController.Context` | `GROUP_EAD_SQL` |
| WEB4 | `GET /api/v1/operations/{operationId}` | `OperationsController.Status` | `GROUP_EAD_SQL` |
| WEB5 | `GET /api/v2/operations/context` | `OperationResolutionController.Context` | `GROUP_EAD_SQL` |
| WEB5 | `GET /api/v2/operations/{operationId}/resolution` | `OperationResolutionController.Resolution` | `GROUP_EAD_SQL` |
| WEB6 | `GET /api/v3/operations/resend-context` | `Web6ResendEligibilityController.Context` | `DISABLED_404` |
| WEB6 | `GET /api/v3/operations/{operationId}/resend-receipt` | `Web6ResendEligibilityController.Receipt` | `DISABLED_404` |
| WEB6 | `GET /api/v3/operations/{operationId}/resend-eligibility` | `Web6ResendEligibilityController.Eligibility` | `DISABLED_404` |
| VPS-02 | `GET /api/v1/catalog/products` | `ProductsController.List` | `ROUTE_ONLY` |
| VPS-02 | `GET /api/v1/catalog/products/{id:int}` | `ProductsController.Get` | `ROUTE_ONLY` |
| VPS-02 | `POST /api/v1/catalog/products` | `ProductsController.Create` | `ROUTE_ONLY` |
| VPS-02 | `PUT /api/v1/catalog/products/{id:int}` | `ProductsController.Update` | `ROUTE_ONLY` |
| VPS-02 | `PUT /api/v1/catalog/products/{id:int}/active` | `ProductsController.SetActive` | `ROUTE_ONLY` |
| VPS-02 | `GET /api/v1/catalog/warehouses` | `WarehousesController.List` | `ROUTE_ONLY` |
| VPS-02 | `GET /api/v1/catalog/warehouses/{id:int}` | `WarehousesController.Get` | `ROUTE_ONLY` |
| VPS-02 | `POST /api/v1/catalog/warehouses` | `WarehousesController.Create` | `ROUTE_ONLY` |
| VPS-02 | `PUT /api/v1/catalog/warehouses/{id:int}` | `WarehousesController.Update` | `ROUTE_ONLY` |
| VPS-02 | `PUT /api/v1/catalog/warehouses/{id:int}/active` | `WarehousesController.SetActive` | `ROUTE_ONLY` |
| VPS-02 | `GET /api/v1/catalog/employees` | `EmployeesController.List` | `ROUTE_ONLY` |
| VPS-02 | `GET /api/v1/catalog/employees/{id:int}` | `EmployeesController.Get` | `ROUTE_ONLY` |
| VPS-02 | `POST /api/v1/catalog/employees` | `EmployeesController.Create` | `ROUTE_ONLY` |
| VPS-02 | `PUT /api/v1/catalog/employees/{id:int}` | `EmployeesController.Update` | `ROUTE_ONLY` |
| VPS-02 | `PUT /api/v1/catalog/employees/{id:int}/active` | `EmployeesController.SetActive` | `ROUTE_ONLY` |
| VPS-02 | `GET /api/v1/catalog/customers` | `CustomersController.List` | `ROUTE_ONLY` |
| VPS-02 | `GET /api/v1/catalog/customers/{id:int}` | `CustomersController.Get` | `ROUTE_ONLY` |
| VPS-02 | `POST /api/v1/catalog/customers` | `CustomersController.Create` | `ROUTE_ONLY` |
| VPS-02 | `PUT /api/v1/catalog/customers/{id:int}` | `CustomersController.Update` | `ROUTE_ONLY` |
| VPS-02 | `PUT /api/v1/catalog/customers/{id:int}/active` | `CustomersController.SetActive` | `ROUTE_ONLY` |
| VPS-02 | `GET /api/v1/security/permissions/check` | `SecurityParityController.CheckPermission` | `ROUTE_ONLY` |
| VPS-02 | `GET /api/v1/security/permissions/catalog` | `SecurityParityController.PermissionCatalog` | `ROUTE_ONLY` |
| VPS-02 | `GET /api/v1/security/users` | `SecurityParityController.ListUsers` | `ROUTE_ONLY` |
| VPS-02 | `GET /api/v1/security/roles` | `SecurityParityController.ListRoles` | `ROUTE_ONLY` |
| VPS-02 | `POST /api/v1/security/mutations` | `SecurityParityController.Mutate` | `ROUTE_ONLY` |
| VPS-02 | `POST /api/v1/security/import/preview` | `SecurityParityController.PreviewImport` | `ROUTE_ONLY` |
| VPS-02 | `POST /api/v1/security/import/apply` | `SecurityParityController.ApplyImport` | `ROUTE_ONLY` |
| VPS-02 | `GET /api/v1/foundation/status` | `FoundationParityController.Status` | `ROUTE_ONLY` |

## Unverified acceptance boundaries

Full response parity for all 57 routes requires dedicated positive and negative runtime cases for `ROUTE_ONLY`, remaining variants of `GROUP_EAD_SQL` and `PRIOR_LIVE_BOUNDED`, authorization/strict JSON/cursor/rowversion cases and independent JSON/wire reconciliation. WEB6 enabled mode **cannot** run under the current net10 host with protected audit paths: startup intentionally refuses Windows-only protected audit until VPS-04B portable durable audit is implemented and approved. This CI runner does not provision or approve the independently protected `vps04a-sql-nonproduction` GitHub Environment, nor provide Linux systemd/reverse-proxy/VPS production acceptance.

**PR disposition:** DRAFT / NOT MERGE APPROVED; no Owner exception, staging approval or production readiness implied.
