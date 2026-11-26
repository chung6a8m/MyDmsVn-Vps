using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyDmsVn.Contracts;

namespace MyDmsVn.Server.HttpHost.Features.Receipts;

public sealed record ReceiptSaveLineRequest(
    [property: JsonRequired] int ProductId,
    [property: JsonRequired] decimal Quantity,
    [property: JsonRequired] decimal UnitCost);

public sealed record ReceiptCreateRequest(
    [property: JsonRequired] DateOnly ReceiptDate,
    [property: JsonRequired] int WarehouseId,
    [property: JsonRequired] int EmployeeId,
    string? Note,
    [property: JsonRequired] ReceiptSaveLineRequest[] Lines);

public sealed record ReceiptUpdateRequest(
    [property: JsonRequired] DateOnly ReceiptDate,
    [property: JsonRequired] int WarehouseId,
    [property: JsonRequired] int EmployeeId,
    string? Note,
    [property: JsonRequired] ReceiptSaveLineRequest[] Lines,
    [property: JsonRequired] string ExpectedVersion);

public sealed record ReceiptPostRequest([property: JsonRequired] string ExpectedVersion);

// Duplicate products are wire-valid; preserve Application's business error and precedence.
internal static class ReceiptRequestReader
{
    private static readonly string[] HeaderFields = { "receiptDate", "warehouseId", "employeeId", "note", "lines" };
    private static readonly string[] LineFields = { "productId", "quantity", "unitCost" };

    internal static bool Supports(Type type) =>
        type == typeof(ReceiptCreateRequest) || type == typeof(ReceiptUpdateRequest) || type == typeof(ReceiptPostRequest);

    internal static object Read(Type type, JsonElement body)
    {
        if (type == typeof(ReceiptPostRequest))
        {
            Fields(body, new[] { "expectedVersion" }, new[] { "expectedVersion" });
            return new ReceiptPostRequest(V1ScalarJsonConverters.ReceiptVersion(body.GetProperty("expectedVersion")));
        }
        var update = type == typeof(ReceiptUpdateRequest);
        if (!update && type != typeof(ReceiptCreateRequest)) throw new InvalidOperationException("Unsupported Receipt input.");
        var allowed = update ? HeaderFields.Append("expectedVersion").ToArray() : HeaderFields;
        Fields(body, allowed, allowed.Where(name => name != "note"));
        var lines = body.GetProperty("lines");
        if (lines.ValueKind != JsonValueKind.Array || lines.GetArrayLength() is < 1 or > 200)
            throw new V1BindingException("lines");

        var result = new List<ReceiptSaveLineRequest>(lines.GetArrayLength());
        foreach (var line in lines.EnumerateArray())
        {
            var prefix = "lines[" + result.Count.ToString(CultureInfo.InvariantCulture) + "]";
            Fields(line, LineFields, LineFields, prefix);
            result.Add(new ReceiptSaveLineRequest(
                V1ScalarJsonConverters.PositiveInt(line.GetProperty("productId"), prefix + ".productId"),
                V1ScalarJsonConverters.Decimal(line.GetProperty("quantity"), true, GoodsReceiptContractLimits.MaximumQuantity, prefix + ".quantity"),
                V1ScalarJsonConverters.Decimal(line.GetProperty("unitCost"), false, GoodsReceiptContractLimits.MaximumUnitCost, prefix + ".unitCost")));
        }
        string? note = null;
        if (body.TryGetProperty("note", out var noteValue) && noteValue.ValueKind != JsonValueKind.Null)
        {
            note = V1ScalarJsonConverters.Text(noteValue, "note");
            V1QueryPolicy.ValidateText(note, "note", 1000);
        }
        var date = V1ScalarBinders.CalendarDate(V1ScalarJsonConverters.Text(body.GetProperty("receiptDate"), "receiptDate"), "receiptDate");
        if (date == DateOnly.MinValue) throw new V1BindingException("receiptDate");
        var warehouse = V1ScalarJsonConverters.PositiveInt(body.GetProperty("warehouseId"), "warehouseId");
        var employee = V1ScalarJsonConverters.PositiveInt(body.GetProperty("employeeId"), "employeeId");
        return update
            ? new ReceiptUpdateRequest(date, warehouse, employee, note, result.ToArray(),
                V1ScalarJsonConverters.ReceiptVersion(body.GetProperty("expectedVersion")))
            : new ReceiptCreateRequest(date, warehouse, employee, note, result.ToArray());
    }

    private static void Fields(JsonElement body, string[] allowed, IEnumerable<string> required, string? prefix = null)
    {
        if (body.ValueKind != JsonValueKind.Object ||
            body.EnumerateObject().Any(p => !allowed.Contains(p.Name, StringComparer.Ordinal)))
            throw new V1BindingException(prefix);
        foreach (var name in required)
            if (!body.TryGetProperty(name, out _))
                throw new V1BindingException(prefix == null ? name : prefix + "." + name);
    }
}
