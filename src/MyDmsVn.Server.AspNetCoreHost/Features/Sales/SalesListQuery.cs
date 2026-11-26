using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MyDmsVn.Server.HttpHost.Features.Sales;

public sealed class SalesListQuery
{
    [FromQuery(Name = "fromDate")]
    public DateOnly? FromDate { get; init; }
    [FromQuery(Name = "toDate")]
    public DateOnly? ToDate { get; init; }
    [FromQuery(Name = "customerId"), Range(1, int.MaxValue)]
    public int? CustomerId { get; init; }
    [FromQuery(Name = "warehouseId"), Range(1, int.MaxValue)]
    public int? WarehouseId { get; init; }
    [FromQuery(Name = "employeeId"), Range(1, int.MaxValue)]
    public int? EmployeeId { get; init; }
    [FromQuery(Name = "status")]
    public string? Status { get; init; }
    [FromQuery(Name = "productId"), Range(1, int.MaxValue)]
    public int? ProductId { get; init; }
    // The limit applies after Trim(), not to the raw query string.
    [FromQuery(Name = "search")]
    public string? Search { get; init; }
    [FromQuery(Name = "pageSize"), DefaultValue(25), Range(1, 200)]
    public int PageSize { get; init; } = 25;
    [FromQuery(Name = "cursor"), StringLength(4096, MinimumLength = 1)]
    public string? Cursor { get; init; }
}

internal sealed class SalesListQueryBinder : IModelBinder
{
    public async Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);
        try
        {
            var request = bindingContext.HttpContext.Request;
            await V1QueryPolicy.RequireEmptyBodyAsync(request).ConfigureAwait(false);
            var query = V1QueryPolicy.Read(request, "fromDate", "toDate", "customerId", "warehouseId",
                "employeeId", "status", "productId", "search", "pageSize", "cursor");
            string? Value(string key) => query.TryGetValue(key, out var value) ? value : null;
            DateOnly? Date(string key) => Value(key) is { } value ? V1ScalarBinders.CalendarDate(value, key) : null;
            int? Id(string key) => Value(key) is { } value ? V1QueryPolicy.PositiveInt(value, key, int.MaxValue) : null;
            // Preserve legacy validation precedence and pass raw search/cursor
            // unchanged to Application, which owns normalization and cursor binding.
            var status = Value("status");
            if (status != null && status is not ("Draft" or "Posted")) throw new V1BindingException("status");
            var cursor = Value("cursor");
            if (cursor != null && (cursor.Length == 0 || cursor.Length > 4096)) throw new V1BindingException("cursor");
            var search = Value("search");
            if (search != null) V1QueryPolicy.ValidateText(search.Trim(), "search", 256);
            bindingContext.Result = ModelBindingResult.Success(new SalesListQuery
            {
                FromDate = Date("fromDate"), ToDate = Date("toDate"),
                CustomerId = Id("customerId"), WarehouseId = Id("warehouseId"), EmployeeId = Id("employeeId"),
                Status = status, ProductId = Id("productId"), Search = search,
                PageSize = Value("pageSize") is { } size ? V1QueryPolicy.PositiveInt(size, "pageSize", 200) : 25,
                Cursor = cursor
            });
        }
        catch (V1BindingException error)
        {
            V1ScalarBinders.Fail(bindingContext, error);
        }
    }
}
