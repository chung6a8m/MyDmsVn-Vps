using System;
using System.ComponentModel.DataAnnotations;

namespace MyDmsVn.Server.HttpHost.Features.Sales;

// Host-owned wire types: strings here are the established decimal/Int64
// protocol representations, not strings used for domain calculation.
public sealed record SalesDetailResponse(
    string SaleId, string SaleNumber, DateOnly SaleDate, int CustomerId, int WarehouseId, int EmployeeId,
    string Status, string? Note, string? PostedAtUtc, string Version,
    SalesDetailLineResponse[] Lines, string TotalAmount, string? TotalCost, string? GrossProfit,
    string? CustomerDisplayName, bool CustomerIsActive, string? WarehouseDisplayName,
    bool WarehouseIsActive, string? EmployeeDisplayName, bool EmployeeIsActive);

public sealed record SalesDetailLineResponse(
    string SaleLineId, int LineNo, int ProductId, string Unit, string Qty, string UnitPrice,
    bool IsGift, string LineAmount, string? PostedUnitCost, string? CostAmount,
    string? ProductDisplayName, bool ProductIsActive);

public sealed record SalesListPageResponse(SalesListRowResponse[] Items, string? NextCursor, string? PreviousCursor);

public sealed record SalesListRowResponse(
    string SaleId, string SaleNumber, DateOnly SaleDate, int CustomerId, string? CustomerDisplayName,
    int WarehouseId, string? WarehouseDisplayName, int EmployeeId, string? EmployeeDisplayName,
    string Status, int LineCount, string TotalAmount);
