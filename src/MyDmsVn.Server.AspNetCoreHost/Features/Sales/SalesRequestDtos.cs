using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyDmsVn.Contracts;

namespace MyDmsVn.Server.HttpHost.Features.Sales;

// Typed values are transport-owned; Application receives only its existing
// request objects. The native formatter enforces the legacy presence/order
// rules before constructing these records.
public sealed record SalesSaveLineRequest(
    [property: JsonRequired] long SaleLineId,
    [property: JsonRequired] int ProductId,
    [property: JsonRequired] decimal Qty,
    [property: JsonRequired] decimal UnitPrice,
    [property: JsonRequired] bool IsGift);

public sealed record SalesCreateRequest(
    [property: JsonRequired] DateOnly SaleDate,
    [property: JsonRequired] int CustomerId,
    [property: JsonRequired] int WarehouseId,
    [property: JsonRequired] int EmployeeId,
    string? Note,
    [property: JsonRequired] SalesSaveLineRequest[] Lines);

public sealed record SalesUpdateRequest(
    [property: JsonRequired] DateOnly SaleDate,
    [property: JsonRequired] int CustomerId,
    [property: JsonRequired] int WarehouseId,
    [property: JsonRequired] int EmployeeId,
    string? Note,
    [property: JsonRequired] SalesSaveLineRequest[] Lines,
    [property: JsonRequired] string ExpectedVersion);

public sealed record SalesPostRequest([property: JsonRequired] string ExpectedVersion);

internal static class SalesRequestReader
{
    private static readonly string[] HeaderFields = { "saleDate", "customerId", "warehouseId", "employeeId", "note", "lines" };
    private static readonly string[] LineFields = { "saleLineId", "productId", "qty", "unitPrice", "isGift" };

    internal static bool Supports(Type type) =>
        type == typeof(SalesCreateRequest) || type == typeof(SalesUpdateRequest) || type == typeof(SalesPostRequest);

    internal static object Read(Type type, JsonElement body)
    {
        if (type == typeof(SalesPostRequest))
        {
            Fields(body, new[] { "expectedVersion" }, new[] { "expectedVersion" });
            return new SalesPostRequest(V1ScalarJsonConverters.SalesVersion(body.GetProperty("expectedVersion")));
        }
        var update = type == typeof(SalesUpdateRequest);
        if (!update && type != typeof(SalesCreateRequest)) throw new InvalidOperationException("Unsupported Sales input.");
        var allowed = update ? HeaderFields.Append("expectedVersion").ToArray() : HeaderFields;
        Fields(body, allowed, allowed.Where(name => name != "note"));
        var lines = body.GetProperty("lines");
        if (lines.ValueKind != JsonValueKind.Array || lines.GetArrayLength() is < 1 ||
            lines.GetArrayLength() > SalesContractLimits.MaximumLines) throw new V1BindingException("lines");
        var result = new List<SalesSaveLineRequest>(lines.GetArrayLength());
        foreach (var line in lines.EnumerateArray())
        {
            var prefix = "lines[" + result.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
            Fields(line, LineFields, LineFields, prefix);
            var id = V1ScalarJsonConverters.LineId(line.GetProperty("saleLineId"), prefix + ".saleLineId");
            if (!update && id != 0) throw new V1BindingException(prefix + ".saleLineId");
            // The baseline checks gift before product/quantity/price.
            var gift = V1ScalarJsonConverters.Boolean(line.GetProperty("isGift"), prefix + ".isGift");
            result.Add(new SalesSaveLineRequest(id,
                V1ScalarJsonConverters.PositiveInt(line.GetProperty("productId"), prefix + ".productId"),
                V1ScalarJsonConverters.Decimal(line.GetProperty("qty"), true, SalesContractLimits.MaximumQuantity, prefix + ".qty"),
                V1ScalarJsonConverters.Decimal(line.GetProperty("unitPrice"), false, SalesContractLimits.MaximumUnitPrice, prefix + ".unitPrice"),
                gift));
        }
        string? note = null;
        if (body.TryGetProperty("note", out var noteValue) && noteValue.ValueKind != JsonValueKind.Null)
        {
            note = V1ScalarJsonConverters.Text(noteValue, "note");
            V1QueryPolicy.ValidateText(note, "note", 1000);
        }
        var date = V1ScalarBinders.CalendarDate(V1ScalarJsonConverters.Text(body.GetProperty("saleDate"), "saleDate"), "saleDate");
        if (date == DateOnly.MinValue) throw new V1BindingException("saleDate");
        var customer = V1ScalarJsonConverters.PositiveInt(body.GetProperty("customerId"), "customerId");
        var warehouse = V1ScalarJsonConverters.PositiveInt(body.GetProperty("warehouseId"), "warehouseId");
        var employee = V1ScalarJsonConverters.PositiveInt(body.GetProperty("employeeId"), "employeeId");
        return update
            ? new SalesUpdateRequest(date, customer, warehouse, employee, note, result.ToArray(),
                V1ScalarJsonConverters.SalesVersion(body.GetProperty("expectedVersion")))
            : new SalesCreateRequest(date, customer, warehouse, employee, note, result.ToArray());
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
