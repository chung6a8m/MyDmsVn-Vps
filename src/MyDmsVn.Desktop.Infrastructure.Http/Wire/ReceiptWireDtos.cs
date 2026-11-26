using System;
using System.Collections.Generic;
using System.Linq;
using MyDmsVn.Contracts;
using Newtonsoft.Json;

namespace MyDmsVn.Desktop.Infrastructure.Http.Wire
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ReceiptSaveWireDto
    {
        private readonly bool _includeVersion;
        internal ReceiptSaveWireDto(DateTime receiptDate, int warehouseId, int employeeId,
            string? note, IReadOnlyList<ReceiptLineWireDto> lines, bool includeVersion,
            string? expectedVersion)
        {
            ReceiptDate = receiptDate;
            WarehouseId = warehouseId;
            EmployeeId = employeeId;
            Note = note;
            Lines = lines;
            _includeVersion = includeVersion;
            ExpectedVersion = expectedVersion;
        }

        [JsonProperty("receiptDate", Order = 0)]
        [JsonConverter(typeof(CalendarDateJsonConverter))]
        public DateTime ReceiptDate { get; }
        [JsonProperty("warehouseId", Order = 1)]
        public int WarehouseId { get; }
        [JsonProperty("employeeId", Order = 2)]
        public int EmployeeId { get; }
        [JsonProperty("note", Order = 3, NullValueHandling = NullValueHandling.Include)]
        public string? Note { get; }
        [JsonProperty("lines", Order = 4)]
        public IReadOnlyList<ReceiptLineWireDto> Lines { get; }
        [JsonProperty("expectedVersion", Order = 5, NullValueHandling = NullValueHandling.Include)]
        public string? ExpectedVersion { get; }
        public bool ShouldSerializeExpectedVersion() => _includeVersion;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ReceiptLineWireDto
    {
        internal ReceiptLineWireDto(SaveGoodsReceiptLineRequest line)
        {
            ProductId = line.ProductId;
            Quantity = line.Quantity;
            UnitCost = line.UnitCost;
        }

        [JsonProperty("productId", Order = 0)]
        public int ProductId { get; }
        [JsonProperty("quantity", Order = 1)]
        [JsonConverter(typeof(V1InvariantTextConverter))]
        public decimal Quantity { get; }
        [JsonProperty("unitCost", Order = 2)]
        [JsonConverter(typeof(V1InvariantTextConverter))]
        public decimal UnitCost { get; }
    }

    // Explicit wire names, not a rename/re-serialize pass over a response DOM.
    // The neutral id alias remains readable for existing adapter callers.
    internal sealed class ReceiptResponseWireDto
    {
        [JsonProperty("receiptId")]
        public long? ReceiptId { get; set; }
        public long Id { get; set; }
        public string ReceiptNumber { get; set; } = null!;
        [JsonConverter(typeof(CalendarDateJsonConverter))]
        public DateTime ReceiptDate { get; set; }
        public int WarehouseId { get; set; }
        public int EmployeeId { get; set; }
        public string Status { get; set; } = null!;
        public string? Note { get; set; }
        public DateTime? PostedAtUtc { get; set; }
        public string Version { get; set; } = null!;
        public IReadOnlyList<ReceiptLineResponseWireDto> Lines { get; set; } = null!;
        public string? WarehouseDisplayName { get; set; }
        public bool WarehouseIsActive { get; set; } = true;
        public string? EmployeeDisplayName { get; set; }
        public bool EmployeeIsActive { get; set; } = true;

        public GoodsReceiptDto ToContract() =>
            new GoodsReceiptDto(ReceiptId ?? Id, ReceiptNumber, ReceiptDate, WarehouseId,
                EmployeeId, Status, Note, PostedAtUtc, Version, Lines.Select(line => line.ToContract()),
                WarehouseDisplayName, WarehouseIsActive, EmployeeDisplayName, EmployeeIsActive);
    }

    internal sealed class ReceiptLineResponseWireDto
    {
        [JsonProperty("receiptLineId")]
        public long? ReceiptLineId { get; set; }
        public long Id { get; set; }
        public int LineNumber { get; set; }
        public int ProductId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public string? ProductDisplayName { get; set; }
        public bool ProductIsActive { get; set; } = true;

        public GoodsReceiptLineDto ToContract() =>
            new GoodsReceiptLineDto(ReceiptLineId ?? Id, LineNumber, ProductId,
                Quantity, UnitCost, ProductDisplayName, ProductIsActive);
    }
}
