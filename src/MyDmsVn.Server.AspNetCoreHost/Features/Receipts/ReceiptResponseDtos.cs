using System;
using System.Text.Json.Serialization;

namespace MyDmsVn.Server.HttpHost.Features.Receipts;

// WEB3 requires every member, including nullable labels and draft timestamp.
public sealed record ReceiptDetailResponse(
    string ReceiptId, string ReceiptNumber, DateOnly ReceiptDate,
    int WarehouseId, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? WarehouseDisplayName, bool WarehouseIsActive,
    int EmployeeId, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? EmployeeDisplayName, bool EmployeeIsActive,
    string Status, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Note,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? PostedAtUtc,
    string Version, ReceiptLineResponse[] Lines);

public sealed record ReceiptLineResponse(
    string ReceiptLineId, int LineNumber, int ProductId, string Quantity, string UnitCost,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ProductDisplayName, bool ProductIsActive);
