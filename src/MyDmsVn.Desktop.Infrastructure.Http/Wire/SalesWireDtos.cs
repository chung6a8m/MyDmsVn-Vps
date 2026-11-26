using System;
using System.Collections.Generic;
using MyDmsVn.Contracts;
using Newtonsoft.Json;

namespace MyDmsVn.Desktop.Infrastructure.Http.Wire
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SalesSaveWireDto
    {
        private readonly bool _includeVersion;
        internal SalesSaveWireDto(DateTime saleDate, int customerId, int warehouseId,
            int employeeId, string? note, IReadOnlyList<SalesLineWireDto> lines,
            bool includeVersion, string? expectedVersion)
        {
            SaleDate = saleDate;
            CustomerId = customerId;
            WarehouseId = warehouseId;
            EmployeeId = employeeId;
            Note = note;
            Lines = lines;
            _includeVersion = includeVersion;
            ExpectedVersion = expectedVersion;
        }

        [JsonProperty("saleDate", Order = 0)]
        [JsonConverter(typeof(CalendarDateJsonConverter))]
        public DateTime SaleDate { get; }
        [JsonProperty("customerId", Order = 1)]
        public int CustomerId { get; }
        [JsonProperty("warehouseId", Order = 2)]
        public int WarehouseId { get; }
        [JsonProperty("employeeId", Order = 3)]
        public int EmployeeId { get; }
        [JsonProperty("note", Order = 4, NullValueHandling = NullValueHandling.Include)]
        public string? Note { get; }
        [JsonProperty("lines", Order = 5)]
        public IReadOnlyList<SalesLineWireDto> Lines { get; }
        [JsonProperty("expectedVersion", Order = 6, NullValueHandling = NullValueHandling.Include)]
        public string? ExpectedVersion { get; }
        public bool ShouldSerializeExpectedVersion() => _includeVersion;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SalesLineWireDto
    {
        internal SalesLineWireDto(SaveSalesReceiptLineRequest line)
        {
            SaleLineId = line.SaleLineId;
            ProductId = line.ProductId;
            Qty = line.Qty;
            UnitPrice = line.UnitPrice;
            IsGift = line.IsGift;
        }

        [JsonProperty("saleLineId", Order = 0)]
        [JsonConverter(typeof(V1InvariantTextConverter))]
        public long SaleLineId { get; }
        [JsonProperty("productId", Order = 1)]
        public int ProductId { get; }
        [JsonProperty("qty", Order = 2)]
        [JsonConverter(typeof(V1InvariantTextConverter))]
        public decimal Qty { get; }
        [JsonProperty("unitPrice", Order = 3)]
        [JsonConverter(typeof(V1InvariantTextConverter))]
        public decimal UnitPrice { get; }
        [JsonProperty("isGift", Order = 4)]
        public bool IsGift { get; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ExpectedVersionWireDto
    {
        internal ExpectedVersionWireDto(string version) { ExpectedVersion = version; }

        [JsonProperty("expectedVersion", NullValueHandling = NullValueHandling.Include)]
        public string ExpectedVersion { get; }
    }
}
