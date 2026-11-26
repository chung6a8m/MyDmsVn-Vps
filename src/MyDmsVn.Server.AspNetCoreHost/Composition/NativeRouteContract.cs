using System.Collections.Generic;

namespace MyDmsVn.Server.HttpHost;

// Compatibility identity, pinned to the independently frozen v1/v2/v3 inventory.
// This table contains no legacy controller types or dispatch delegates. The
// actual MVC inventory is compared against it at startup and against the
// original manifests by scripts/verify-vps-04a.py in CI.
internal static class NativeRouteContract
{
    internal sealed record Entry(string Verb, string Path, string BaselineController,
        string BaselineAction, int Order, string NativeController, string NativeAction, bool DesktopApiRequired);

    internal static IReadOnlyList<Entry> Routes { get; } = new Entry[]
    {
        new("GET", "/api/v1/auth/capabilities", "AuthController", "Capabilities", 0, "MyDmsVn.Server.HttpHost.Features.Auth.AuthController", "Capabilities", false),
        new("GET", "/api/v1/auth/me", "AuthController", "Me", 0, "MyDmsVn.Server.HttpHost.Features.Auth.AuthController", "Me", false),
        new("GET", "/api/v1/catalog/customers", "CustomersController", "List", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.CustomersController", "List", true),
        new("GET", "/api/v1/catalog/customers/lookup", "CatalogLookupController", "Customers", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.CatalogLookupController", "Customers", false),
        new("GET", "/api/v1/catalog/customers/{id:int}", "CustomersController", "Get", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.CustomersController", "Get", true),
        new("GET", "/api/v1/catalog/employees", "EmployeesController", "List", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.EmployeesController", "List", true),
        new("GET", "/api/v1/catalog/employees/lookup", "CatalogLookupController", "Employees", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.CatalogLookupController", "Employees", false),
        new("GET", "/api/v1/catalog/employees/{id:int}", "EmployeesController", "Get", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.EmployeesController", "Get", true),
        new("GET", "/api/v1/catalog/products", "ProductsController", "List", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.ProductsController", "List", true),
        new("GET", "/api/v1/catalog/products/lookup", "CatalogLookupController", "Products", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.CatalogLookupController", "Products", false),
        new("GET", "/api/v1/catalog/products/{id:int}", "ProductsController", "Get", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.ProductsController", "Get", true),
        new("GET", "/api/v1/catalog/warehouses", "WarehousesController", "List", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.WarehousesController", "List", true),
        new("GET", "/api/v1/catalog/warehouses/lookup", "CatalogLookupController", "Warehouses", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.CatalogLookupController", "Warehouses", false),
        new("GET", "/api/v1/catalog/warehouses/{id:int}", "WarehousesController", "Get", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.WarehousesController", "Get", true),
        new("GET", "/api/v1/foundation/status", "FoundationParityController", "Status", 0, "MyDmsVn.Server.HttpHost.Features.Foundation.FoundationController", "Status", true),
        new("GET", "/api/v1/goods-receipts", "GoodsReceiptsController", "List", 0, "MyDmsVn.Server.HttpHost.Features.Receipts.GoodsReceiptsController", "List", false),
        new("GET", "/api/v1/goods-receipts/{receiptId}", "GoodsReceiptsController", "Get", 0, "MyDmsVn.Server.HttpHost.Features.Receipts.GoodsReceiptsController", "Get", false),
        new("GET", "/api/v1/health", "HealthController", "Get", 0, "MyDmsVn.Server.HttpHost.Features.Auth.HealthController", "Get", false),
        new("GET", "/api/v1/inventory/balances", "InventoryController", "Balances", 0, "MyDmsVn.Server.HttpHost.Features.Inventory.InventoryController", "Balances", false),
        new("GET", "/api/v1/inventory/stock-card", "InventoryController", "StockCard", 0, "MyDmsVn.Server.HttpHost.Features.Inventory.InventoryController", "StockCard", false),
        new("GET", "/api/v1/operations/context", "OperationsController", "Context", 0, "MyDmsVn.Server.HttpHost.Features.Operations.OperationsController", "Context", false),
        new("GET", "/api/v1/operations/{operationId}", "OperationsController", "Status", 1, "MyDmsVn.Server.HttpHost.Features.Operations.OperationsController", "Status", false),
        new("GET", "/api/v1/sales-receipts", "SalesReceiptsController", "List", 0, "MyDmsVn.Server.HttpHost.Features.Sales.SalesReceiptsController", "List", false),
        new("GET", "/api/v1/sales-receipts/{saleId}", "SalesReceiptsController", "Get", 0, "MyDmsVn.Server.HttpHost.Features.Sales.SalesReceiptsController", "Get", false),
        new("GET", "/api/v1/security/permissions/catalog", "SecurityParityController", "PermissionCatalog", 0, "MyDmsVn.Server.HttpHost.Features.Security.SecurityController", "PermissionCatalog", true),
        new("GET", "/api/v1/security/permissions/check", "SecurityParityController", "CheckPermission", 0, "MyDmsVn.Server.HttpHost.Features.Security.SecurityController", "CheckPermission", true),
        new("GET", "/api/v1/security/roles", "SecurityParityController", "ListRoles", 0, "MyDmsVn.Server.HttpHost.Features.Security.SecurityController", "ListRoles", true),
        new("GET", "/api/v1/security/users", "SecurityParityController", "ListUsers", 0, "MyDmsVn.Server.HttpHost.Features.Security.SecurityController", "ListUsers", true),
        new("GET", "/api/v2/operations/context", "OperationResolutionController", "Context", 0, "MyDmsVn.Server.HttpHost.Features.Operations.ResolutionController", "Context", false),
        new("GET", "/api/v2/operations/{operationId}/resolution", "OperationResolutionController", "Resolution", 1, "MyDmsVn.Server.HttpHost.Features.Operations.ResolutionController", "Resolution", false),
        new("GET", "/api/v3/operations/resend-context", "Web6ResendEligibilityController", "Context", 0, "MyDmsVn.Server.HttpHost.Features.Operations.ResendController", "Context", false),
        new("GET", "/api/v3/operations/{operationId}/resend-eligibility", "Web6ResendEligibilityController", "Eligibility", 2, "MyDmsVn.Server.HttpHost.Features.Operations.ResendController", "Eligibility", false),
        new("GET", "/api/v3/operations/{operationId}/resend-receipt", "Web6ResendEligibilityController", "Receipt", 1, "MyDmsVn.Server.HttpHost.Features.Operations.ResendController", "Receipt", false),
        new("POST", "/api/v1/auth/login", "AuthController", "Login", 0, "MyDmsVn.Server.HttpHost.Features.Auth.AuthController", "Login", false),
        new("POST", "/api/v1/auth/logout", "AuthController", "Logout", 0, "MyDmsVn.Server.HttpHost.Features.Auth.AuthController", "Logout", false),
        new("POST", "/api/v1/auth/refresh", "AuthController", "Refresh", 0, "MyDmsVn.Server.HttpHost.Features.Auth.AuthController", "Refresh", false),
        new("POST", "/api/v1/catalog/customers", "CustomersController", "Create", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.CustomersController", "Create", true),
        new("POST", "/api/v1/catalog/employees", "EmployeesController", "Create", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.EmployeesController", "Create", true),
        new("POST", "/api/v1/catalog/products", "ProductsController", "Create", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.ProductsController", "Create", true),
        new("POST", "/api/v1/catalog/warehouses", "WarehousesController", "Create", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.WarehousesController", "Create", true),
        new("POST", "/api/v1/goods-receipts", "GoodsReceiptsController", "Create", 0, "MyDmsVn.Server.HttpHost.Features.Receipts.GoodsReceiptsController", "Create", false),
        new("POST", "/api/v1/goods-receipts/{receiptId}/post", "GoodsReceiptsController", "Post", 0, "MyDmsVn.Server.HttpHost.Features.Receipts.GoodsReceiptsController", "Post", false),
        new("POST", "/api/v1/sales-receipts", "SalesReceiptsController", "Create", 0, "MyDmsVn.Server.HttpHost.Features.Sales.SalesReceiptsController", "Create", false),
        new("POST", "/api/v1/sales-receipts/{saleId}/post", "SalesReceiptsController", "Post", 0, "MyDmsVn.Server.HttpHost.Features.Sales.SalesReceiptsController", "Post", false),
        new("POST", "/api/v1/security/import/apply", "SecurityParityController", "ApplyImport", 0, "MyDmsVn.Server.HttpHost.Features.Security.SecurityController", "ApplyImport", true),
        new("POST", "/api/v1/security/import/preview", "SecurityParityController", "PreviewImport", 0, "MyDmsVn.Server.HttpHost.Features.Security.SecurityController", "PreviewImport", true),
        new("POST", "/api/v1/security/mutations", "SecurityParityController", "Mutate", 0, "MyDmsVn.Server.HttpHost.Features.Security.SecurityController", "Mutate", true),
        new("PUT", "/api/v1/catalog/customers/{id:int}", "CustomersController", "Update", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.CustomersController", "Update", true),
        new("PUT", "/api/v1/catalog/customers/{id:int}/active", "CustomersController", "SetActive", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.CustomersController", "SetActive", true),
        new("PUT", "/api/v1/catalog/employees/{id:int}", "EmployeesController", "Update", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.EmployeesController", "Update", true),
        new("PUT", "/api/v1/catalog/employees/{id:int}/active", "EmployeesController", "SetActive", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.EmployeesController", "SetActive", true),
        new("PUT", "/api/v1/catalog/products/{id:int}", "ProductsController", "Update", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.ProductsController", "Update", true),
        new("PUT", "/api/v1/catalog/products/{id:int}/active", "ProductsController", "SetActive", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.ProductsController", "SetActive", true),
        new("PUT", "/api/v1/catalog/warehouses/{id:int}", "WarehousesController", "Update", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.WarehousesController", "Update", true),
        new("PUT", "/api/v1/catalog/warehouses/{id:int}/active", "WarehousesController", "SetActive", 0, "MyDmsVn.Server.HttpHost.Features.Catalog.WarehousesController", "SetActive", true),
        new("PUT", "/api/v1/goods-receipts/{receiptId}/draft", "GoodsReceiptsController", "Update", 0, "MyDmsVn.Server.HttpHost.Features.Receipts.GoodsReceiptsController", "Update", false),
        new("PUT", "/api/v1/sales-receipts/{saleId}/draft", "SalesReceiptsController", "Update", 0, "MyDmsVn.Server.HttpHost.Features.Sales.SalesReceiptsController", "Update", false),
    };
}
