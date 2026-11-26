using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MyDmsVn.Contracts;
using MyDmsVn.Server.Application.Inventory;
using MyDmsVn.Server.HttpHost;
using MyDmsVn.Server.HttpHost.Features.Inventory;
using MyDmsVn.Server.HttpHost.Features.Receipts;
using Xunit;
using LiveFixture = MyDmsVn.Server.AspNetCoreHost.Tests.Vps05cDesktopRouteLiveParityTests.DesktopLiveFixture;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps10InventoryReadTests
{
    private const string Receipts = "/api/v1/goods-receipts";
    private const string Balances = "/api/v1/inventory/balances";
    private const string Card = "/api/v1/inventory/stock-card";
    private const string Paging = "?pageNumber=1&pageSize=25";
    private const string CardPaging = Paging + "&warehouseId=1&productId=7";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task All_four_reads_remain_available_in_both_desktop_modes(bool enabled)
    {
        await using var fixture = await LiveFixture.StartAsync(desktopApiEnabled: enabled);
        foreach (var path in new[] { Receipts + Paging, Receipts + "/9007199254740993", Balances + Paging, Card + CardPaging })
        {
            using var response = await fixture.SendAsync("GET", path, null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        }
        Assert.Equal(4, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData("vi-VN")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("")]
    public async Task Typed_queries_and_WEB3_scalar_output_are_invariant_in_live_host(string culture)
    {
        await using var fixture = await LiveFixture.StartAsync(requestCultureName: culture);
        using var list = await fixture.SendAsync("GET", Receipts + "?pageNumber=2&pageSize=200&warehouseId=1&status=Posted&search=%20a%2Bb%20", null);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(culture, fixture.LastRequestCulture);
        var rq = Assert.IsType<ListGoodsReceiptsQuery>(fixture.LastRequest).Request;
        Assert.Equal(2, rq.PageNumber);
        Assert.Equal(200, rq.PageSize);
        Assert.Equal(1, rq.WarehouseId);
        Assert.Equal("Posted", rq.Status);
        Assert.Equal(" a+b ", rq.Search);
        using var receiptPage = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var item = receiptPage.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Equal("9007199254740993", item.GetProperty("receiptId").GetString());
        Assert.Equal("GR0000000042", item.GetProperty("receiptNumber").GetString());
        Assert.Equal("2024-02-29", item.GetProperty("receiptDate").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("postedAtUtc").ValueKind);
        Assert.Equal("9007199254740996", item.GetProperty("lines")[0].GetProperty("receiptLineId").GetString());
        Assert.Equal("3.0000", item.GetProperty("lines")[0].GetProperty("quantity").GetString());
        Assert.Equal("0.0000", item.GetProperty("lines")[0].GetProperty("unitCost").GetString());
        Assert.Equal("1", receiptPage.RootElement.GetProperty("data").GetProperty("totalCount").GetString());

        using var detail = await fixture.SendAsync("GET", Receipts + "/9223372036854775807", null);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(long.MaxValue, Assert.IsType<GetGoodsReceiptByIdQuery>(fixture.LastRequest).Id);
        using var receipt = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal("2026-11-01T02:03:04.1234567Z", receipt.RootElement.GetProperty("data").GetProperty("postedAtUtc").GetString());

        using var balances = await fixture.SendAsync("GET", Balances + Paging + "&warehouseId=1&productId=7&search=a+b%2Bc", null);
        Assert.Equal(HttpStatusCode.OK, balances.StatusCode);
        var bq = Assert.IsType<GetStockBalancesQuery>(fixture.LastRequest).Request;
        Assert.Equal(1, bq.WarehouseId);
        Assert.Equal(7, bq.ProductId);
        Assert.Equal("a b+c", bq.Search);
        Assert.Equal(culture, fixture.LastRequestCulture);
        using var balance = JsonDocument.Parse(await balances.Content.ReadAsStringAsync());
        var row = balance.RootElement.GetProperty("data").GetProperty("items")[0];
        Assert.Equal("16.0000", row.GetProperty("quantity").GetString());
        Assert.Equal("240.0000", row.GetProperty("inventoryValue").GetString());
        Assert.Equal("15.00000000", row.GetProperty("averageUnitCost").GetString());
        Assert.False(row.GetProperty("warehouseIsActive").GetBoolean());

        using var card = await fixture.SendAsync("GET", Card + CardPaging +
            "&fromUtc=2026-11-01T02%3A03%3A04Z&toUtc=2026-11-01T02%3A03%3A05.1234567Z", null);
        Assert.Equal(HttpStatusCode.OK, card.StatusCode);
        Assert.Equal(culture, fixture.LastRequestCulture);
        var cq = Assert.IsType<GetStockCardQuery>(fixture.LastRequest).Request;
        Assert.Equal(1, cq.WarehouseId);
        Assert.Equal(7, cq.ProductId);
        Assert.Equal(new DateTime(2026, 11, 1, 2, 3, 4, DateTimeKind.Utc), cq.FromUtc);
        Assert.Equal(new DateTime(2026, 11, 1, 2, 3, 5, DateTimeKind.Utc).AddTicks(1234567), cq.ToUtc);
        Assert.Equal(DateTimeKind.Utc, cq.FromUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, cq.ToUtc!.Value.Kind);
        using var ledger = JsonDocument.Parse(await card.Content.ReadAsStringAsync());
        var rows = ledger.RootElement.GetProperty("data").GetProperty("items");
        Assert.Equal("20.0000", rows[0].GetProperty("quantityChange").GetString());
        Assert.Equal("-4.0000", rows[1].GetProperty("quantityChange").GetString());
        Assert.Equal("-60.0000", rows[1].GetProperty("valueChange").GetString());
        Assert.Equal("9223372036854775807", rows[1].GetProperty("ledgerId").GetString());
        Assert.Equal("2026-11-01T02:03:05.1234567Z", rows[1].GetProperty("postedAtUtc").GetString());
    }

    [Theory]
    [InlineData(Receipts, "pageNumber")]
    [InlineData(Receipts + "?pageNumber=1", "pageSize")]
    [InlineData(Receipts + "?pageNumber=01&pageSize=1", "pageNumber")]
    [InlineData(Receipts + "?pageNumber=1&pageSize=201", "pageSize")]
    [InlineData(Receipts + "?status=posted&pageNumber=0", "status")]
    [InlineData(Receipts + "?search=%00&pageNumber=0", "search")]
    [InlineData(Receipts + Paging + "&warehouseId=2147483648", "warehouseId")]
    [InlineData(Receipts + Paging + "&%70ageSize=1", null)]
    [InlineData(Receipts + "?PageNumber=1&pageSize=25", null)]
    [InlineData(Receipts + Paging + "&search=%FF", null)]
    [InlineData(Receipts + Paging + "&search", null)]
    [InlineData(Balances, "pageNumber")]
    [InlineData(Balances + "?warehouseId=0", "warehouseId")]
    [InlineData(Balances + "?productId=%2B1", "productId")]
    [InlineData(Balances + Paging + "&extra=x", null)]
    [InlineData(Balances + Paging + "&search=%00&warehouseId=0", "search")]
    [InlineData(Card + Paging, "warehouseId")]
    [InlineData(Card + Paging + "&warehouseId=1", "productId")]
    [InlineData(Card + CardPaging + "&fromUtc=2026-11-01", "fromUtc")]
    [InlineData(Card + CardPaging + "&toUtc=2026-11-01T02:03:04%2B07:00", "toUtc")]
    [InlineData(Card + CardPaging + "&fromUtc=2026-11-01T02:03:04.12345678Z", "fromUtc")]
    [InlineData(Card + CardPaging + "&fromUtc=2026-02-29T00:00:00Z", "fromUtc")]
    [InlineData(Card + CardPaging + "&fromUtc=2026-11-02T00:00:00Z&toUtc=2026-11-01T00:00:00Z", "toUtc")]
    [InlineData(Card + "?fromUtc=bad", "fromUtc")]
    [InlineData(Receipts + "/0", "receiptId")]
    [InlineData(Receipts + "/01", "receiptId")]
    [InlineData(Receipts + "/9223372036854775808", "receiptId")]
    [InlineData(Receipts + "/bad?extra=x", null)]
    [InlineData(Receipts + "/1?search=x", null)]
    public async Task Invalid_grammar_and_precedence_are_rejected_before_handler(string path, string? field)
    {
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("GET", path, null);
        await Invalid(response, field);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData(Receipts + "?pageNumber=0")]
    [InlineData(Receipts + "/bad")]
    [InlineData(Balances + "?productId=0")]
    [InlineData(Card + "?fromUtc=bad")]
    public async Task Nonempty_body_precedes_scalar_validation(string path)
    {
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("GET", path, "{}");
        await Invalid(response, null);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData(Receipts + Paging, false)]
    [InlineData(Receipts + Paging, true)]
    [InlineData(Receipts + "/1", false)]
    [InlineData(Receipts + "/1", true)]
    [InlineData(Balances + Paging, false)]
    [InlineData(Balances + Paging, true)]
    [InlineData(Card + CardPaging, false)]
    [InlineData(Card + CardPaging, true)]
    public async Task Reads_preserve_authentication_and_application_permissions(string path, bool authenticated)
    {
        await using var fixture = await LiveFixture.StartAsync(denyPermissions: true);
        using var response = await fixture.SendAsync("GET", path, null, authenticated);
        Assert.Equal(authenticated ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, response.StatusCode);
        if (authenticated) Assert.NotEmpty(fixture.PermissionChecks);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData(Receipts, "GET, POST")]
    [InlineData(Receipts + "/1", "GET")]
    [InlineData(Balances, "GET")]
    [InlineData(Card, "GET")]
    public async Task Mixed_ownership_preserves_method_miss(string path, string allow)
    {
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("PATCH", path, null, withOrigin: true);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(allow, string.Join(", ", response.Content.Headers.Allow));
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Fact]
    public async Task Edge_case_HTTP_keeps_explicit_nulls_and_concurrent_count_semantics()
    {
        await using var fixture = await LiveFixture.StartAsync(web3ReadEdgeCases: true);
        using var detail = await fixture.SendAsync("GET", Receipts + "/1", null);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var receipt = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        var data = receipt.RootElement.GetProperty("data");
        foreach (var name in new[] { "warehouseDisplayName", "employeeDisplayName", "note", "postedAtUtc" })
            Assert.Equal(JsonValueKind.Null, data.GetProperty(name).ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("lines")[0].GetProperty("productDisplayName").ValueKind);
        using var balances = await fixture.SendAsync("GET", Balances + Paging, null);
        Assert.Equal(HttpStatusCode.OK, balances.StatusCode);
        using var balance = JsonDocument.Parse(await balances.Content.ReadAsStringAsync());
        var page = balance.RootElement.GetProperty("data");
        Assert.Equal("0", page.GetProperty("totalCount").GetString());
        Assert.Single(page.GetProperty("items").EnumerateArray());
        var row = page.GetProperty("items")[0];
        Assert.Equal("0.0000", row.GetProperty("quantity").GetString());
        Assert.Equal("0.0000", row.GetProperty("inventoryValue").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("averageUnitCost").ValueKind);
    }

    [Theory]
    [InlineData("quantity-scale")]
    [InlineData("average-scale")]
    [InlineData("zero-average")]
    [InlineData("positive-missing-average")]
    [InlineData("negative-stock")]
    [InlineData("null-code")]
    public void Invalid_balance_does_not_get_rounded_or_silently_serialized(string variant)
    {
        var quantity = variant switch { "quantity-scale" => 1.00001m, "zero-average" => 0m, "negative-stock" => -1m, _ => 1m };
        decimal? average = variant switch { "average-scale" => 1.000000001m, "positive-missing-average" => null, _ => 1m };
        var value = new StockBalanceRow(1, variant == "null-code" ? null! : "W", "Warehouse", true,
            7, "P", "Product", true, quantity, 0m, average);
        Assert.Throws<InvalidOperationException>(() => InventoryHttpMapper.Balance(value));
    }

    [Theory]
    [InlineData("invalid-version")]
    [InlineData("duplicate-products")]
    [InlineData("wrong-line-order")]
    [InlineData("wrong-status-timestamp")]
    public void Receipt_response_invariants_remain_fail_closed(string variant)
    {
        var line = new GoodsReceiptLineDto(2, variant == "wrong-line-order" ? 2 : 1, 7, 1m, 0m);
        var lines = variant == "duplicate-products" ? new[] { line, new GoodsReceiptLineDto(3, 2, 7, 1m, 0m) } : new[] { line };
        var dto = new GoodsReceiptDto(1, "GR0000000042", new DateTime(2024, 2, 29), 1, 2,
            "Draft", null, variant == "wrong-status-timestamp" ? DateTime.UtcNow : null,
            variant == "invalid-version" ? "bad" : "AAAAAAAAAAI=", lines);
        Assert.Throws<InvalidOperationException>(() => ReceiptHttpMapper.Detail(dto));
    }

    [Fact]
    public void SQL_unspecified_timestamp_is_labelled_UTC_without_offset_shift()
    {
        var stamp = new DateTime(2026, 11, 1, 2, 3, 4, DateTimeKind.Unspecified).AddTicks(1234567);
        var row = new StockCardRow(long.MaxValue, 1, "W", "Warehouse", false, 7, "P", "Product", true,
            stamp, "SalesReceipt", 9007199254740993L, "SR0000000001", -1.25m, -0.0001m);
        var mapped = InventoryHttpMapper.Card(row);
        Assert.Equal("2026-11-01T02:03:04.1234567Z", mapped.PostedAtUtc);
        Assert.Equal("-1.2500", mapped.QuantityChange);
        Assert.Equal("-0.0001", mapped.ValueChange);
        Assert.Equal("9007199254740993", mapped.DocumentId);
        Assert.Throws<InvalidOperationException>(() => InventoryHttpMapper.Card(new StockCardRow(1, 1, "W", "W", true,
            7, "P", "P", true, stamp, "SalesReceipt", 1, "SR1", 1m, 0m)));
    }

    private static async Task Invalid(HttpResponseMessage response, string? field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("ValidationError", error.GetProperty("code").GetString());
        var detail = error.GetProperty("details")[0];
        Assert.Equal("Validation.InvalidValue", detail.GetProperty("code").GetString());
        Assert.Equal(field, detail.TryGetProperty("field", out var value) ? value.GetString() : null);
    }
}
