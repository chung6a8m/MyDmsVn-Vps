using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MyDmsVn.Contracts;
using MyDmsVn.Server.HttpHost.Features.Inventory;
using static MyDmsVn.Server.HttpHost.Features.Inventory.Web3ResponseValues;

namespace MyDmsVn.Server.HttpHost.Features.Receipts;

internal static class ReceiptHttpMapper
{
    internal static Web3PageResponse<ReceiptDetailResponse> ReceiptPage(PagedResult<GoodsReceiptDto> value) => Page(value, Detail);

    internal static ReceiptDetailResponse Detail(GoodsReceiptDto value)
    {
        Require(value.Status is "Draft" or "Posted");
        Require(value.Status == "Draft" ? !value.PostedAtUtc.HasValue : value.PostedAtUtc.HasValue);
        Require(value.ReceiptDate != default && value.Lines.Count is >= 1 and <= 200);
        Require(value.ReceiptNumber != null &&
            Regex.IsMatch(value.ReceiptNumber, @"\AGR[0-9]{10}\z", RegexOptions.CultureInvariant) &&
            value.ReceiptNumber != "GR0000000000");
        Require(CanonicalVersion(value.Version));
        var products = new HashSet<int>();
        var ids = new HashSet<long>();
        var lines = value.Lines.Select((line, index) =>
        {
            Require(line.LineNumber == index + 1 && products.Add(line.ProductId) && ids.Add(line.Id));
            return new ReceiptLineResponse(Id(line.Id), line.LineNumber, Positive(line.ProductId),
                Number(line.Quantity, 4, GoodsReceiptContractLimits.MaximumQuantity, positive: true),
                Number(line.UnitCost, 4, GoodsReceiptContractLimits.MaximumUnitCost),
                NullableText(line.ProductDisplayName), line.ProductIsActive);
        }).ToArray();
        return new(Id(value.Id), value.ReceiptNumber!, DateOnly.FromDateTime(value.ReceiptDate),
            Positive(value.WarehouseId), NullableText(value.WarehouseDisplayName), value.WarehouseIsActive,
            Positive(value.EmployeeId), NullableText(value.EmployeeDisplayName), value.EmployeeIsActive,
            value.Status, NullableText(value.Note, 1000),
            value.PostedAtUtc.HasValue ? Utc(value.PostedAtUtc.Value) : null, value.Version, lines);
    }

    internal static SaveGoodsReceiptRequest ToApplication(ReceiptCreateRequest value) =>
        new(value.ReceiptDate.ToDateTime(TimeOnly.MinValue), value.WarehouseId, value.EmployeeId, value.Note, Lines(value.Lines));

    internal static UpdateGoodsReceiptDraftRequest ToApplication(long receiptId, ReceiptUpdateRequest value) =>
        new(receiptId, value.ExpectedVersion, value.ReceiptDate.ToDateTime(TimeOnly.MinValue),
            value.WarehouseId, value.EmployeeId, value.Note, Lines(value.Lines));

    internal static PostGoodsReceiptRequest ToApplication(long receiptId, ReceiptPostRequest value) =>
        new(receiptId, value.ExpectedVersion);

    private static SaveGoodsReceiptLineRequest[] Lines(ReceiptSaveLineRequest[] values) =>
        values.Select(line => new SaveGoodsReceiptLineRequest(line.ProductId, line.Quantity, line.UnitCost)).ToArray();

    internal static ReceiptPostResponse Post(PostGoodsReceiptResponse value)
    {
        Require(value.Status == "Posted");
        return new(Id(value.ReceiptId), value.Status, Utc(value.PostedAtUtc));
    }

    private static bool CanonicalVersion(string value)
    {
        if (value == null || value.Length != 12) return false;
        try
        {
            var bytes = Convert.FromBase64String(value);
            return bytes.Length == 8 && Convert.ToBase64String(bytes) == value;
        }
        catch (FormatException) { return false; }
    }
}
