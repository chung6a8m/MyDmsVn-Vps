using System;
using System.Collections.Generic;
using System.Globalization;
using MyDmsVn.Contracts;
using MyDmsVn.Desktop.Infrastructure.Http.Wire;
using Newtonsoft.Json.Linq;

namespace MyDmsVn.Desktop.Infrastructure.Http
{
    /// <summary>Invariant query projection and strict journal/corpus JSON reader; mutation DTOs live in Wire.</summary>
    public static class HttpDesktopWire
    {
        public static string Query(params (string Name, object? Value)[] parameters)
        {
            var entries = new List<string>();
            foreach (var item in parameters)
            {
                if (item.Value == null) continue;
                string value;
                if (item.Value is bool boolean)
                    value = boolean ? "true" : "false";
                else if (item.Value is DateTime date)
                    value = (date.Kind == DateTimeKind.Local ? date.ToUniversalTime() :
                        DateTime.SpecifyKind(date, DateTimeKind.Utc))
                        .ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
                else if (item.Value is IFormattable formattable)
                    value = formattable.ToString(null, CultureInfo.InvariantCulture);
                else
                    value = item.Value.ToString() ?? "";
                entries.Add(Uri.EscapeDataString(item.Name) + "=" + Uri.EscapeDataString(value));
            }
            return entries.Count == 0 ? "" : "?" + string.Join("&", entries);
        }

        public static string CatalogList(CatalogListRequest request) =>
            Query(("search", request.Search), ("pageNumber", request.PageNumber),
                ("pageSize", request.PageSize), ("includeInactive", request.IncludeInactive));

        public static string Lookup(CatalogLookupRequest request) =>
            Query(("search", request.Search), ("limit", request.Limit));

        public static string SalesList(SalesReceiptListRequest request) =>
            Query(("fromDate", request.FromDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("toDate", request.ToDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("customerId", request.CustomerId), ("warehouseId", request.WarehouseId),
                ("employeeId", request.EmployeeId), ("status", request.Status),
                ("productId", request.ProductId), ("search", request.Search),
                ("pageSize", request.PageSize), ("cursor", request.Cursor));

        public static string ReceiptList(GoodsReceiptListRequest request) =>
            Query(("pageNumber", request.PageNumber), ("pageSize", request.PageSize),
                ("warehouseId", request.WarehouseId), ("status", request.Status),
                ("search", request.Search));

        public static string Balances(StockBalanceQuery request) =>
            Query(("pageNumber", request.PageNumber), ("pageSize", request.PageSize),
                ("warehouseId", request.WarehouseId), ("productId", request.ProductId),
                ("search", request.Search));

        public static string StockCard(StockCardQuery request) =>
            Query(("pageNumber", request.PageNumber), ("pageSize", request.PageSize),
                ("warehouseId", request.WarehouseId), ("productId", request.ProductId),
                ("fromUtc", request.FromUtc), ("toUtc", request.ToUtc));

        /// <summary>Operation and frozen corpus JSON preserve the original calendar/instant strings.</summary>
        public static JObject ParseObjectPreservingDates(string json) =>
            V1WireMapper.ReadEnvelope(json, true);
    }
}
