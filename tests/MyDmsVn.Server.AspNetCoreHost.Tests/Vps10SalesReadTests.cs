using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using MyDmsVn.Contracts;
using MyDmsVn.Server.Application.Sales;
using MyDmsVn.Server.HttpHost;
using MyDmsVn.Server.HttpHost.Features.Sales;
using Xunit;
using LiveFixture = MyDmsVn.Server.AspNetCoreHost.Tests.Vps05cDesktopRouteLiveParityTests.DesktopLiveFixture;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps10SalesReadTests
{
    private const string Sales = "/api/v1/sales-receipts";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task List_default_and_detail_remain_available_in_both_desktop_modes(bool enabled)
    {
        await using var fixture = await LiveFixture.StartAsync(desktopApiEnabled: enabled);
        using var response = await fixture.SendAsync("GET", Sales, null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var query = Assert.IsType<ListSalesReceiptsQuery>(fixture.LastRequest);
        Assert.Equal(25, query.Request.PageSize);
        using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = page.RootElement.GetProperty("data");
        Assert.False(data.TryGetProperty("nextCursor", out _));
        Assert.False(data.TryGetProperty("previousCursor", out _));
        Assert.Equal("9223372036854775807", data.GetProperty("items")[0].GetProperty("saleId").GetString());
        using var detail = await fixture.SendAsync("GET", Sales + "/9223372036854775807", null);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(long.MaxValue, Assert.IsType<GetSalesReceiptByIdQuery>(fixture.LastRequest).SaleId);
        Assert.Equal(2, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData("vi-VN")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("")]
    public async Task Typed_filter_and_posted_wire_are_invariant(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            await using var fixture = await LiveFixture.StartAsync();
            using var list = await fixture.SendAsync("GET", Sales +
                "?fromDate=2024-02-29&toDate=2024-03-01&customerId=101&warehouseId=102&employeeId=103&status=Posted&productId=104&search=%20a%2Bb%20&pageSize=200", null);
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var q = Assert.IsType<ListSalesReceiptsQuery>(fixture.LastRequest).Request;
            Assert.Equal(new DateTime(2024, 2, 29), q.FromDate);
            Assert.Equal(new DateTime(2024, 3, 1), q.ToDate);
            Assert.Equal(DateTimeKind.Unspecified, q.FromDate!.Value.Kind);
            Assert.Equal(101, q.CustomerId);
            Assert.Equal(102, q.WarehouseId);
            Assert.Equal(103, q.EmployeeId);
            Assert.Equal(104, q.ProductId);
            Assert.Equal("Posted", q.Status);
            Assert.Equal(" a+b ", q.Search);
            Assert.Equal(200, q.PageSize);

            using var detail = await fixture.SendAsync("GET", Sales + "/9223372036854775807", null);
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            using var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
            var data = json.RootElement.GetProperty("data");
            Assert.Equal("2024-02-29", data.GetProperty("saleDate").GetString());
            Assert.Equal("2026-10-31T03:00:00Z", data.GetProperty("postedAtUtc").GetString());
            Assert.Equal("-0.7500", data.GetProperty("grossProfit").GetString());
            var lines = data.GetProperty("lines").EnumerateArray().ToArray();
            Assert.Equal(3, lines.Length);
            Assert.All(lines, line =>
            {
                Assert.Equal(104, line.GetProperty("productId").GetInt32());
                Assert.Equal("1.00000001", line.GetProperty("postedUnitCost").GetString());
            });
            Assert.Equal("9007199254740993", lines[0].GetProperty("saleLineId").GetString());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Theory]
    [InlineData("?pageSize=0", "pageSize")]
    [InlineData("?pageSize=201", "pageSize")]
    [InlineData("?pageSize=01", "pageSize")]
    [InlineData("?pageSize=%2B1", "pageSize")]
    [InlineData("?pageSize=1.0", "pageSize")]
    [InlineData("?fromDate=2023-02-29", "fromDate")]
    [InlineData("?fromDate=2024-2-29", "fromDate")]
    [InlineData("?toDate=2024-02-29T00:00:00Z", "toDate")]
    [InlineData("?customerId=2147483648", "customerId")]
    [InlineData("?warehouseId=0", "warehouseId")]
    [InlineData("?employeeId=01", "employeeId")]
    [InlineData("?productId=-1", "productId")]
    [InlineData("?status=posted&fromDate=x", "status")]
    [InlineData("?cursor=&fromDate=x", "cursor")]
    [InlineData("?search=%00&fromDate=x", "search")]
    [InlineData("?pageSize=1&%70ageSize=2", null)]
    [InlineData("?PageSize=1", null)]
    [InlineData("?search=%FF", null)]
    [InlineData("?search", null)]
    [InlineData("?extra=x", null)]
    public async Task List_invalid_wire_is_rejected_before_dispatch(string query, string? field)
    {
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("GET", Sales + query, null);
        await Invalid(response, field);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData("/0", null, "saleId")]
    [InlineData("/01", null, "saleId")]
    [InlineData("/9223372036854775808", null, "saleId")]
    [InlineData("/-1", null, "saleId")]
    [InlineData("/1.0", null, "saleId")]
    [InlineData("/bad?extra=x", null, null)]
    [InlineData("/bad", "{}", null)]
    [InlineData("/1?search=x", null, null)]
    [InlineData("?pageSize=0", "{}", null)]
    public async Task Invalid_id_and_body_query_precedence(string path, string? body, string? field)
    {
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("GET", Sales + path, body);
        await Invalid(response, field);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData(25, "Posted", true)]
    [InlineData(26, "Posted", false)]
    [InlineData(25, "Draft", false)]
    public async Task Application_retains_cursor_filter_and_page_size_binding(int pageSize, string status, bool accepted)
    {
        var request = new SalesReceiptListRequest(null, null, null, null, null, "Posted", null, null, 25);
        var cursor = SalesReceiptCursor.Parse(request).Encode(42, false);
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("GET", Sales + "?status=" + status + "&pageSize=" +
            pageSize.ToString(CultureInfo.InvariantCulture) + "&cursor=" + Uri.EscapeDataString(cursor), null);
        Assert.Equal(accepted ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
        if (accepted)
            Assert.Equal(cursor, Assert.IsType<ListSalesReceiptsQuery>(fixture.LastRequest).Request.Cursor);
        else
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("Validation.InvalidCursor", json.RootElement.GetProperty("error").GetProperty("details")[0].GetProperty("code").GetString());
        }
        Assert.Equal(accepted ? 1 : 0, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("/1", false)]
    [InlineData("", true)]
    [InlineData("/1", true)]
    public async Task Authentication_and_permission_pipeline_stays_in_charge(string path, bool authenticated)
    {
        await using var fixture = await LiveFixture.StartAsync(denyPermissions: true);
        using var response = await fixture.SendAsync("GET", Sales + path, null, authenticated);
        Assert.Equal(authenticated ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, response.StatusCode);
        if (authenticated) Assert.NotEmpty(fixture.PermissionChecks);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Fact]
    public void Draft_omits_optional_fields_and_decimal_scale_is_not_padded()
    {
        var draft = new SalesReceiptDto(1, "S1", new DateTime(2024, 2, 29), 1, 2, 3, "Draft",
            null, null, "AAAAAAAAAAA=", new[] { new SalesReceiptLineDto(2, 1, 4, "kg", 1.25m,
                2m, false, 2.50m, null, null, null, false) }, 2.50m, null, null, null, false, null, false, null, false);
        var options = new JsonSerializerOptions();
        V1HttpJson.Configure(options);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(SalesHttpMapper.Detail(draft), options));
        var data = json.RootElement;
        Assert.Equal("2.50", data.GetProperty("totalAmount").GetString());
        foreach (var key in new[] { "note", "postedAtUtc", "totalCost", "grossProfit", "customerDisplayName" })
            Assert.False(data.TryGetProperty(key, out _));
        var line = data.GetProperty("lines")[0];
        Assert.Equal("1.25", line.GetProperty("qty").GetString());
        Assert.Equal("2", line.GetProperty("unitPrice").GetString());
        Assert.False(line.TryGetProperty("postedUnitCost", out _));
        Assert.False(line.TryGetProperty("costAmount", out _));
    }

    [Fact]
    public void Posted_SQL_unspecified_UTC_and_short_cost_remain_exact()
    {
        var posted = new SalesReceiptDto(1, "S1", new DateTime(2024, 2, 29), 1, 2, 3, "Posted",
            "2024-02-29", new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Unspecified), "AAAAAAAAAAA=",
            new[] { new SalesReceiptLineDto(2, 1, 4, "kg", 1m, 2m, false, 2m, 1.5m, 1.5m, null, false) },
            2m, 1.5m, 0.5m, null, false, null, false, null, false);
        var detail = SalesHttpMapper.Detail(posted);
        Assert.Equal("2024-03-01T00:00:00Z", detail.PostedAtUtc);
        Assert.Equal("1.5", detail.Lines[0].PostedUnitCost);
        Assert.Equal("2024-02-29", detail.Note);
    }

    private static async Task Invalid(System.Net.Http.HttpResponseMessage response, string? field)
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
