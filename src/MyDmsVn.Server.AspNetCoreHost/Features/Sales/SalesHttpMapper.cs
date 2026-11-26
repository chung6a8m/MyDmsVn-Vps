using System;
using System.Globalization;
using System.Linq;
using MyDmsVn.Contracts;

namespace MyDmsVn.Server.HttpHost.Features.Sales;

internal static class SalesHttpMapper
{
    internal static SalesReceiptListRequest ToApplication(SalesListQuery query) =>
        new(query.FromDate?.ToDateTime(TimeOnly.MinValue), query.ToDate?.ToDateTime(TimeOnly.MinValue),
            query.CustomerId, query.WarehouseId, query.EmployeeId, query.Status, query.ProductId,
            query.Search, query.PageSize, query.Cursor);

    internal static SalesDetailResponse Detail(SalesReceiptDto value) => new(
        Id(value.SaleId), value.SaleNumber, DateOnly.FromDateTime(value.SaleDate), value.CustomerId,
        value.WarehouseId, value.EmployeeId, value.Status, value.Note,
        value.PostedAtUtc.HasValue ? Utc(value.PostedAtUtc.Value) : null, value.Version,
        value.Lines.Select(line => new SalesDetailLineResponse(
            Id(line.SaleLineId), line.LineNo, line.ProductId, line.Unit, Number(line.Qty), Number(line.UnitPrice),
            line.IsGift, Number(line.LineAmount), Number(line.PostedUnitCost), Number(line.CostAmount),
            line.ProductDisplayName, line.ProductIsActive)).ToArray(),
        Number(value.TotalAmount), Number(value.TotalCost), Number(value.GrossProfit),
        value.CustomerDisplayName, value.CustomerIsActive, value.WarehouseDisplayName, value.WarehouseIsActive,
        value.EmployeeDisplayName, value.EmployeeIsActive);

    internal static SalesListPageResponse Page(SalesReceiptListPage value) => new(
        value.Items.Select(row => new SalesListRowResponse(Id(row.SaleId), row.SaleNumber,
            DateOnly.FromDateTime(row.SaleDate), row.CustomerId, row.CustomerDisplayName,
            row.WarehouseId, row.WarehouseDisplayName, row.EmployeeId, row.EmployeeDisplayName,
            row.Status, row.LineCount, Number(row.TotalAmount))).ToArray(), value.NextCursor, value.PreviousCursor);

    internal static SaveSalesReceiptRequest ToApplication(SalesCreateRequest value) =>
        new(value.SaleDate.ToDateTime(TimeOnly.MinValue), value.CustomerId, value.WarehouseId, value.EmployeeId,
            value.Note, Lines(value.Lines));

    internal static UpdateSalesReceiptDraftRequest ToApplication(long saleId, SalesUpdateRequest value) =>
        new(saleId, value.ExpectedVersion, value.SaleDate.ToDateTime(TimeOnly.MinValue), value.CustomerId,
            value.WarehouseId, value.EmployeeId, value.Note, Lines(value.Lines));

    internal static PostSalesReceiptRequest ToApplication(long saleId, SalesPostRequest value) =>
        new(saleId, value.ExpectedVersion);

    private static SaveSalesReceiptLineRequest[] Lines(SalesSaveLineRequest[] values) =>
        values.Select(line => new SaveSalesReceiptLineRequest(line.SaleLineId, line.ProductId, line.Qty, line.UnitPrice, line.IsGift)).ToArray();

    internal static SalesPostResponse Post(PostSalesReceiptResponse value) =>
        new(Id(value.SaleId), value.Status, Utc(value.PostedAtUtc), value.Version);

    private static string Id(long value) => value.ToString(CultureInfo.InvariantCulture);
    // Do not pad or round: preserve the represented scale of each decimal.
    private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static string? Number(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);
    private static string Utc(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
    }
}
