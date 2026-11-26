using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using MyDmsVn.Contracts;

namespace MyDmsVn.Server.HttpHost.Features.Inventory;

public sealed class ReceiptListQuery
{
    [FromQuery(Name = "pageNumber"), BindRequired, Required, Range(1, int.MaxValue)]
    public int PageNumber { get; init; }
    [FromQuery(Name = "pageSize"), BindRequired, Required, Range(1, 200)]
    public int PageSize { get; init; }
    [FromQuery(Name = "warehouseId"), Range(1, int.MaxValue)]
    public int? WarehouseId { get; init; }
    [FromQuery(Name = "status")]
    public string? Status { get; init; }
    [FromQuery(Name = "search"), MaxLength(128)]
    public string? Search { get; init; }

    internal GoodsReceiptListRequest ToApplication() => new(PageNumber, PageSize, WarehouseId, Status, Search);
}

public sealed class InventoryBalancesQuery
{
    [FromQuery(Name = "pageNumber"), BindRequired, Required, Range(1, int.MaxValue)]
    public int PageNumber { get; init; }
    [FromQuery(Name = "pageSize"), BindRequired, Required, Range(1, 200)]
    public int PageSize { get; init; }
    [FromQuery(Name = "warehouseId"), Range(1, int.MaxValue)]
    public int? WarehouseId { get; init; }
    [FromQuery(Name = "productId"), Range(1, int.MaxValue)]
    public int? ProductId { get; init; }
    [FromQuery(Name = "search"), MaxLength(256)]
    public string? Search { get; init; }

    internal StockBalanceQuery ToApplication() => new(WarehouseId, ProductId, Search, PageNumber, PageSize);
}

public sealed class InventoryStockCardQuery
{
    [FromQuery(Name = "pageNumber"), BindRequired, Required, Range(1, int.MaxValue)]
    public int PageNumber { get; init; }
    [FromQuery(Name = "pageSize"), BindRequired, Required, Range(1, 200)]
    public int PageSize { get; init; }
    [FromQuery(Name = "warehouseId"), BindRequired, Required, Range(1, int.MaxValue)]
    public int WarehouseId { get; init; }
    [FromQuery(Name = "productId"), BindRequired, Required, Range(1, int.MaxValue)]
    public int ProductId { get; init; }
    [FromQuery(Name = "fromUtc")]
    public DateTime? FromUtc { get; init; }
    [FromQuery(Name = "toUtc")]
    public DateTime? ToUtc { get; init; }

    internal StockCardQuery ToApplication() => new(WarehouseId, ProductId, FromUtc, ToUtc, PageNumber, PageSize);
}

internal sealed class V1Web3ReadBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var type = context.Metadata.ModelType;
        if (type == typeof(ReceiptListQuery) || type == typeof(InventoryBalancesQuery) || type == typeof(InventoryStockCardQuery))
            return new Web3ListBinder();
        if (type == typeof(long) && context.Metadata.ParameterName == "receiptId" &&
            context.BindingInfo.BindingSource == BindingSource.Path)
            return new ReceiptIdBinder();
        return null;
    }
}

internal sealed class Web3ListBinder : IModelBinder
{
    public async Task BindModelAsync(ModelBindingContext context)
    {
        try
        {
            var request = context.HttpContext.Request;
            // Preserve body-before-query validation on all WEB3 collections.
            await V1QueryPolicy.RequireEmptyBodyAsync(request).ConfigureAwait(false);
            object result;
            if (context.ModelType == typeof(ReceiptListQuery))
            {
                var query = V1QueryPolicy.Read(request, "pageNumber", "pageSize", "warehouseId", "status", "search");
                query.TryGetValue("status", out var status);
                if (status != null && status is not ("Draft" or "Posted")) throw new V1BindingException("status");
                var search = V1QueryPolicy.OptionalText(query, "search", 128);
                result = new ReceiptListQuery
                {
                    PageNumber = Required(query, "pageNumber"), PageSize = Required(query, "pageSize", 200),
                    WarehouseId = Optional(query, "warehouseId"), Status = status, Search = search
                };
            }
            else if (context.ModelType == typeof(InventoryBalancesQuery))
            {
                var query = V1QueryPolicy.Read(request, "pageNumber", "pageSize", "warehouseId", "productId", "search");
                var search = V1QueryPolicy.OptionalText(query, "search", 256);
                result = new InventoryBalancesQuery
                {
                    WarehouseId = Optional(query, "warehouseId"), ProductId = Optional(query, "productId"),
                    Search = search, PageNumber = Required(query, "pageNumber"), PageSize = Required(query, "pageSize", 200)
                };
            }
            else
            {
                var query = V1QueryPolicy.Read(request, "pageNumber", "pageSize", "warehouseId", "productId", "fromUtc", "toUtc");
                var from = Instant(query, "fromUtc");
                var to = Instant(query, "toUtc");
                if (from.HasValue && to.HasValue && from.Value > to.Value) throw new V1BindingException("toUtc");
                result = new InventoryStockCardQuery
                {
                    WarehouseId = Required(query, "warehouseId"), ProductId = Required(query, "productId"),
                    FromUtc = from, ToUtc = to, PageNumber = Required(query, "pageNumber"), PageSize = Required(query, "pageSize", 200)
                };
            }
            context.Result = ModelBindingResult.Success(result);
        }
        catch (V1BindingException error) { V1ScalarBinders.Fail(context, error); }
    }

    private static int Required(Dictionary<string, string> query, string field, int maximum = int.MaxValue) =>
        query.TryGetValue(field, out var text) ? V1QueryPolicy.PositiveInt(text, field, maximum) : throw new V1BindingException(field);

    private static int? Optional(Dictionary<string, string> query, string field) =>
        query.TryGetValue(field, out var text) ? V1QueryPolicy.PositiveInt(text, field, int.MaxValue) : null;

    private static DateTime? Instant(Dictionary<string, string> query, string field)
    {
        if (!query.TryGetValue(field, out var text)) return null;
        if (!Regex.IsMatch(text, @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?Z\z", RegexOptions.CultureInvariant) ||
            !DateTime.TryParseExact(text, new[] { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" },
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
            throw new V1BindingException(field);
        return value;
    }
}

internal sealed class ReceiptIdBinder : IModelBinder
{
    public async Task BindModelAsync(ModelBindingContext context)
    {
        try
        {
            var request = context.HttpContext.Request;
            if (request.Method == "GET")
            {
                V1QueryPolicy.Read(request);
                await V1QueryPolicy.RequireEmptyBodyAsync(request).ConfigureAwait(false);
            }
            context.Result = ModelBindingResult.Success(V1ScalarBinders.PositiveLong(
                request.RouteValues["receiptId"] as string ?? "", "receiptId"));
        }
        catch (V1BindingException error) { V1ScalarBinders.Fail(context, error); }
    }
}
