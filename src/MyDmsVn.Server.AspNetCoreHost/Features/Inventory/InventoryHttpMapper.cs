using System;
using System.Globalization;
using System.Linq;
using System.Text;
using MyDmsVn.Contracts;

namespace MyDmsVn.Server.HttpHost.Features.Inventory;

internal static class InventoryHttpMapper
{
    private const decimal MaximumAmount = 999999999999999999999999.9999m;
    private const decimal MaximumAverage = 99999999999999999999.99999999m;

    internal static Web3PageResponse<StockBalanceResponse> Balances(PagedResult<StockBalanceRow> value) =>
        Web3ResponseValues.Page(value, Balance);

    internal static Web3PageResponse<StockCardResponse> StockCard(PagedResult<StockCardRow> value) =>
        Web3ResponseValues.Page(value, Card);

    internal static StockBalanceResponse Balance(StockBalanceRow value)
    {
        Web3ResponseValues.Require(value.Quantity == 0
            ? value.InventoryValue == 0 && !value.AverageUnitCost.HasValue
            : value.AverageUnitCost.HasValue);
        return new(
            Web3ResponseValues.Positive(value.WarehouseId), Web3ResponseValues.Text(value.WarehouseCode),
            Web3ResponseValues.Text(value.WarehouseName), value.WarehouseIsActive,
            Web3ResponseValues.Positive(value.ProductId), Web3ResponseValues.Text(value.ProductCode),
            Web3ResponseValues.Text(value.ProductName), value.ProductIsActive,
            Web3ResponseValues.Number(value.Quantity, 4, GoodsReceiptContractLimits.MaximumQuantity),
            Web3ResponseValues.Number(value.InventoryValue, 4, MaximumAmount),
            value.AverageUnitCost.HasValue ? Web3ResponseValues.Number(value.AverageUnitCost.Value, 8, MaximumAverage) : null);
    }

    internal static StockCardResponse Card(StockCardRow value)
    {
        Web3ResponseValues.Require(value.QuantityChange != 0);
        if (value.DocumentType == "GoodsReceipt")
            Web3ResponseValues.Require(value.QuantityChange > 0 && value.ValueChange >= 0);
        if (value.DocumentType == "SalesReceipt")
            Web3ResponseValues.Require(value.QuantityChange < 0 && value.ValueChange <= 0);
        Web3ResponseValues.Require(!string.IsNullOrEmpty(value.DocumentType) && !string.IsNullOrEmpty(value.DocumentNumber));
        return new(
            Web3ResponseValues.Positive(value.WarehouseId), Web3ResponseValues.Text(value.WarehouseCode),
            Web3ResponseValues.Text(value.WarehouseName), value.WarehouseIsActive,
            Web3ResponseValues.Positive(value.ProductId), Web3ResponseValues.Text(value.ProductCode),
            Web3ResponseValues.Text(value.ProductName), value.ProductIsActive,
            Web3ResponseValues.Id(value.LedgerId), Web3ResponseValues.Utc(value.PostedAtUtc),
            Web3ResponseValues.Text(value.DocumentType), Web3ResponseValues.Id(value.DocumentId),
            Web3ResponseValues.Text(value.DocumentNumber),
            Web3ResponseValues.Number(value.QuantityChange, 4, GoodsReceiptContractLimits.MaximumQuantity, signed: true),
            Web3ResponseValues.Number(value.ValueChange, 4, MaximumAmount, signed: true));
    }
}

// WEB3 output contract checks, intentionally separate from WEB2 Sales scale.
// No rounding, normalization of business values, DOM or legacy serializer.
internal static class Web3ResponseValues
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static Web3PageResponse<TResponse> Page<T, TResponse>(PagedResult<T> value, Func<T, TResponse> map)
    {
        Require(value.PageSize <= 200 && value.Items.Count <= value.PageSize);
        // Count and items are independent SQL reads; count < items.Length is valid.
        return new(value.Items.Select(map).ToArray(), Positive(value.PageNumber),
            Positive(value.PageSize), Id(value.TotalCount, zero: true));
    }

    internal static string Id(long value, bool zero = false)
    {
        Require(value >= (zero ? 0 : 1));
        return value.ToString(CultureInfo.InvariantCulture);
    }

    internal static int Positive(int value)
    {
        Require(value > 0);
        return value;
    }

    internal static string Number(decimal value, int scale, decimal maximum, bool positive = false, bool signed = false)
    {
        Require(value <= maximum && value >= (signed ? -maximum : 0) &&
            (!positive || value > 0) && decimal.Round(value, scale) == value);
        return value.ToString(scale == 4 ? "0.0000" : "0.00000000", CultureInfo.InvariantCulture);
    }

    internal static string Utc(DateTime value)
    {
        // SQL datetime2 with Unspecified kind already denotes UTC.
        var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
    }

    internal static string? NullableText(string? value, int maximum = int.MaxValue) =>
        value == null ? null : Text(value, maximum);

    internal static string Text(string value, int maximum = int.MaxValue)
    {
        Require(value != null && value.Length <= maximum && value.IndexOf('\0') < 0);
        try { Utf8.GetByteCount(value!); }
        catch (EncoderFallbackException) { throw new InvalidOperationException("Invalid inventory wire response."); }
        return value!;
    }

    internal static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Invalid inventory wire response.");
    }
}
