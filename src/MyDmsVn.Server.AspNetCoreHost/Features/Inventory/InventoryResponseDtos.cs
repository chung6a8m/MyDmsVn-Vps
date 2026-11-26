using System.Text.Json.Serialization;

namespace MyDmsVn.Server.HttpHost.Features.Inventory;

public sealed record Web3PageResponse<T>(T[] Items, int PageNumber, int PageSize, string TotalCount);

public sealed record StockBalanceResponse(
    int WarehouseId, string WarehouseCode, string WarehouseName, bool WarehouseIsActive,
    int ProductId, string ProductCode, string ProductName, bool ProductIsActive,
    string Quantity, string InventoryValue,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? AverageUnitCost);

public sealed record StockCardResponse(
    int WarehouseId, string WarehouseCode, string WarehouseName, bool WarehouseIsActive,
    int ProductId, string ProductCode, string ProductName, bool ProductIsActive,
    string LedgerId, string PostedAtUtc, string DocumentType, string DocumentId, string DocumentNumber,
    string QuantityChange, string ValueChange);
