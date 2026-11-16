using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MyDmsVn.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MyDmsVn.Desktop.Infrastructure.Http
{
    /// <summary>Explicit v1 wire projection. Do not serialize Sales/Inventory commands as plain DTOs.</summary>
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

        public static JObject SalesSave(SaveSalesReceiptRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return SalesSaveCore(request.SaleDate, request.CustomerId, request.WarehouseId,
                request.EmployeeId, request.Note, request.Lines);
        }

        public static JObject SalesUpdate(UpdateSalesReceiptDraftRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var body = SalesSaveCore(request.SaleDate, request.CustomerId, request.WarehouseId,
                request.EmployeeId, request.Note, request.Lines);
            body["expectedVersion"] = request.ExpectedVersion;
            return body;
        }

        private static JObject SalesSaveCore(DateTime date, int customerId, int warehouseId,
            int employeeId, string? note, IReadOnlyList<SaveSalesReceiptLineRequest> lines)
        {
            var output = new JArray();
            foreach (var line in lines)
            {
                output.Add(new JObject
                {
                    ["saleLineId"] = Long(line.SaleLineId),
                    ["productId"] = line.ProductId,
                    ["qty"] = Decimal(line.Qty),
                    ["unitPrice"] = Decimal(line.UnitPrice),
                    ["isGift"] = line.IsGift,
                });
            }
            return new JObject
            {
                ["saleDate"] = Calendar(date),
                ["customerId"] = customerId,
                ["warehouseId"] = warehouseId,
                ["employeeId"] = employeeId,
                ["note"] = note == null ? JValue.CreateNull() : new JValue(note),
                ["lines"] = output,
            };
        }

        public static JObject ReceiptSave(SaveGoodsReceiptRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return ReceiptSaveCore(request.ReceiptDate, request.WarehouseId,
                request.EmployeeId, request.Note, request.Lines);
        }

        public static JObject ReceiptUpdate(UpdateGoodsReceiptDraftRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var body = ReceiptSaveCore(request.ReceiptDate, request.WarehouseId,
                request.EmployeeId, request.Note, request.Lines);
            body["expectedVersion"] = request.ExpectedVersion;
            return body;
        }

        private static JObject ReceiptSaveCore(DateTime date, int warehouseId,
            int employeeId, string? note, IReadOnlyList<SaveGoodsReceiptLineRequest> lines)
        {
            var output = new JArray();
            foreach (var line in lines)
            {
                output.Add(new JObject
                {
                    ["productId"] = line.ProductId,
                    ["quantity"] = Decimal(line.Quantity),
                    ["unitCost"] = Decimal(line.UnitCost),
                });
            }
            return new JObject
            {
                ["receiptDate"] = Calendar(date),
                ["warehouseId"] = warehouseId,
                ["employeeId"] = employeeId,
                ["note"] = note == null ? JValue.CreateNull() : new JValue(note),
                ["lines"] = output,
            };
        }

        public static JObject ExpectedVersion(string version) =>
            new JObject { ["expectedVersion"] = version };

        private static string Calendar(DateTime value) =>
            value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static string Long(long value) =>
            value.ToString(CultureInfo.InvariantCulture);

        private static string Decimal(decimal value) =>
            value.ToString(CultureInfo.InvariantCulture);

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

        /// <summary>
        /// The frozen inventory HTTP API names IDs receiptId/receiptLineId,
        /// while Desktop's neutral DTOs call both Id. Normalize only these
        /// response names, never mutate the source API or numeric precision.
        /// </summary>
        public static string NormalizeResponse<T>(string json)
        {
            if (typeof(T) != typeof(GoodsReceiptDto) &&
                typeof(T) != typeof(PagedResult<GoodsReceiptDto>))
                return json;

            var envelope = JObject.Parse(json);
            if (!(envelope["data"] is JObject data)) return json;
            if (typeof(T) == typeof(GoodsReceiptDto))
                NormalizeReceipt(data);
            else if (data["items"] is JArray items)
                foreach (var item in items.OfType<JObject>()) NormalizeReceipt(item);
            return envelope.ToString(Formatting.None);
        }

        private static void NormalizeReceipt(JObject receipt)
        {
            var receiptId = receipt["receiptId"];
            if (receiptId == null) return;
            receipt.Remove("receiptId");
            receipt["id"] = receiptId;
            if (!(receipt["lines"] is JArray lines)) return;
            foreach (var row in lines.OfType<JObject>())
            {
                var lineId = row["receiptLineId"];
                if (lineId == null) continue;
                row.Remove("receiptLineId");
                row["id"] = lineId;
            }
        }
    }
}
