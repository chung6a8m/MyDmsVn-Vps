// CI-only, real HTTPS and SQL13: two distinct warehouses sharing a Product,
// four-decimal moving-average costing, duplicate lines and parallel Posts.
// This does NOT run in approved operator staging or against production SQL.
#if VPS04A_LIVE
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps04aCiMultiWarehouseSqlAcceptanceTests
{
    private static string Required(string key) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } value
        ? value : throw new InvalidOperationException("Missing CI-only multiwarehouse configuration: " + key);

    private static int Seed(string key) => int.Parse(Required(key), CultureInfo.InvariantCulture);
    private static string String(JsonElement data, string name) => data.GetProperty(name).GetString()!;
    private static long Long(JsonElement data, string name) =>
        long.Parse(String(data, name), CultureInfo.InvariantCulture);

    private static void Guard()
    {
        Assert.Equal("approved-container-only", Required("VPS04A_CI_EPHEMERAL_SQL"));
        Assert.Equal("approved-nonproduction-only", Required("VPS04A_LIVE_ALLOW_MUTATION"));
        Assert.Equal("approved-ci-only", Required("VPS04A_CI_FAULT_APPROVED"));
        Assert.Equal("web5", Required("MYDMSVN_WEB_OPERATION_MODE"));
        Assert.Equal("https://localhost:50443", Required("MYDMSVN_WEB_ORIGIN"));
    }

    private sealed class Gateway : IDisposable
    {
        private readonly HttpClient _client = new(new HttpClientHandler { UseCookies = false })
        {
            BaseAddress = new Uri(Required("MYDMSVN_WEB_ORIGIN") + "/"),
            Timeout = TimeSpan.FromSeconds(45)
        };

        public async Task<JsonDocument> JsonAsync(HttpMethod method, string route, int status,
            object? payload = null, string? token = null, string? epoch = null, Guid? key = null)
        {
            using var request = new HttpRequestMessage(method, route.TrimStart('/'));
            if (method != HttpMethod.Get)
                request.Headers.TryAddWithoutValidation("Origin", Required("MYDMSVN_WEB_ORIGIN"));
            if (route == "/api/v1/auth/login")
                request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            if (token != null)
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            if (epoch != null)
                request.Headers.TryAddWithoutValidation("X-MyDmsVn-Deployment-Epoch", epoch);
            if (key.HasValue)
                request.Headers.TryAddWithoutValidation("Idempotency-Key", key.Value.ToString("D"));
            if (payload != null)
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            else if (method != HttpMethod.Get)
                request.Content = new ByteArrayContent(Array.Empty<byte>());
            using var response = await _client.SendAsync(request);
            Assert.Equal(status, (int)response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.True(response.Headers.TryGetValues("Cache-Control", out var cache) &&
                cache.Any(x => x.Contains("no-store", StringComparison.OrdinalIgnoreCase)));
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        }

        public void Dispose() => _client.Dispose();
    }

    private static async Task<(string Token, string Epoch)> AdmitAsync(Gateway gateway)
    {
        using var login = await gateway.JsonAsync(HttpMethod.Post, "/api/v1/auth/login", 200,
            new { username = Required("VPS04A_LIVE_USERNAME"), password = Required("VPS04A_LIVE_PASSWORD") });
        var token = String(login.RootElement.GetProperty("data"), "accessToken");
        using var context = await gateway.JsonAsync(HttpMethod.Get, "/api/v1/operations/context", 200, token: token);
        return (token, String(context.RootElement.GetProperty("data"), "deploymentEpoch"));
    }

    private static async Task<(long Id, string Version)> ReceiptAsync(
        Gateway client, string token, string epoch, int warehouse, int employee, int product,
        string quantity, string cost)
    {
        using var receipt = await client.JsonAsync(HttpMethod.Post, "/api/v1/goods-receipts", 200,
            new {
                receiptDate = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                warehouseId = warehouse, employeeId = employee,
                note = "VPS04A-FRACTIONAL-RECEIPT",
                lines = new[] { new { productId = product, quantity, unitCost = cost } }
            }, token, epoch, Guid.NewGuid());
        var data = receipt.RootElement.GetProperty("data");
        var id = Long(data, "receiptId");
        var version = String(data, "version");
        using var posted = await client.JsonAsync(HttpMethod.Post,
            "/api/v1/goods-receipts/" + id + "/post", 200,
            new { expectedVersion = version }, token, epoch, Guid.NewGuid());
        Assert.Equal("Posted", String(posted.RootElement.GetProperty("data"), "status"));
        return (id, version);
    }

    private static async Task<(long Id, string Version)> SaleAsync(
        Gateway client, string token, string epoch, int customer, int warehouse,
        int employee, int product, string[] quantities)
    {
        var lines = quantities.Select(qty => new
        {
            saleLineId = "0", productId = product, qty, unitPrice = "5.0000", isGift = false
        }).ToArray();
        using var receipt = await client.JsonAsync(HttpMethod.Post, "/api/v1/sales-receipts", 200,
            new {
                saleDate = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                customerId = customer, warehouseId = warehouse, employeeId = employee,
                note = "VPS04A-FRACTIONAL-SALE", lines
            }, token, epoch, Guid.NewGuid());
        var data = receipt.RootElement.GetProperty("data");
        return (Long(data, "saleId"), String(data, "version"));
    }

    private static SqlConnection Oracle() =>
        new(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));

    private static async Task<(int Rows, decimal Quantity, decimal Value)> BalancesAsync(int warehouse, int product)
    {
        using var sql = Oracle();
        await sql.OpenAsync();
        using var cmd = new SqlCommand(@"
SELECT COUNT(*),COALESCE(SUM(CONVERT(decimal(28,4),Quantity)),0),
    COALESCE(SUM(CONVERT(decimal(28,4),InventoryValue)),0)
FROM dbo.StockBalances
WHERE WarehouseId=@wh AND ProductId=@product;", sql);
        cmd.Parameters.Add("@wh", SqlDbType.Int).Value = warehouse;
        cmd.Parameters.Add("@product", SqlDbType.Int).Value = product;
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetDecimal(1), reader.GetDecimal(2));
    }

    private static async Task<(int Rows, decimal Quantity, decimal Value)> LedgerAsync(int warehouse, int product)
    {
        using var sql = Oracle();
        await sql.OpenAsync();
        using var cmd = new SqlCommand(@"
SELECT COUNT(*),COALESCE(SUM(CONVERT(decimal(28,4),l.QuantityChange)),0),
    COALESCE(SUM(CONVERT(decimal(28,4),v.ValueChange)),0)
FROM dbo.StockLedger l
JOIN dbo.StockValuationEntries v ON v.LedgerId=l.LedgerId
WHERE l.WarehouseId=@wh AND l.ProductId=@product;", sql);
        cmd.Parameters.Add("@wh", SqlDbType.Int).Value = warehouse;
        cmd.Parameters.Add("@product", SqlDbType.Int).Value = product;
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetDecimal(1), reader.GetDecimal(2));
    }

    private static async Task<decimal[]> LineCostsAsync(long sale)
    {
        using var sql = Oracle();
        await sql.OpenAsync();
        using var cmd = new SqlCommand(
            "SELECT CostAmount FROM dbo.SalesReceiptLines WHERE SaleId=@id ORDER BY LineNo;", sql);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = sale;
        var costs = new List<decimal>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Assert.False(reader.IsDBNull(0));
            costs.Add(reader.GetDecimal(0));
        }
        return costs.ToArray();
    }

    private static async Task<int> CountAsync(string sqlText, string parameter, object value)
    {
        using var sql = Oracle();
        await sql.OpenAsync();
        using var cmd = new SqlCommand(sqlText, sql);
        cmd.Parameters.AddWithValue(parameter, value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task VerifyInventoryApiAsync(
        Gateway gateway, string token, int warehouse, int product,
        decimal quantity, decimal value, decimal average)
    {
        using var response = await gateway.JsonAsync(HttpMethod.Get,
            $"/api/v1/inventory/balances?pageNumber=1&pageSize=25&warehouseId={warehouse}&productId={product}",
            200, token: token);
        var rows = response.RootElement.GetProperty("data").GetProperty("items");
        Assert.Single(rows.EnumerateArray());
        var row = rows[0];
        static decimal D(JsonElement record, string name) =>
            decimal.Parse(record.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);
        Assert.Equal(quantity, D(row, "quantity"));
        Assert.Equal(value, D(row, "inventoryValue"));
        Assert.Equal(average, D(row, "averageUnitCost"));
    }

    [Fact]
    public async Task ParallelPostsForSameProductAcrossTwoWarehousesKeepFourDecimalCostAllocations()
    {
        Guard();
        var original = Seed("VPS04A_LIVE_WAREHOUSE_ID");
        var warehouseA = Seed("VPS04A_CI_FRACTIONAL_WAREHOUSE_A");
        var warehouseB = Seed("VPS04A_CI_FRACTIONAL_WAREHOUSE_B");
        var product = Seed("VPS04A_LIVE_PRODUCT_ID");
        var customer = Seed("VPS04A_LIVE_CUSTOMER_ID");
        var employee = Seed("VPS04A_LIVE_EMPLOYEE_ID");
        Assert.Equal(3, new[] { original, warehouseA, warehouseB }.Distinct().Count());
        var originalBefore = await BalancesAsync(original, product);
        var ledgerBefore = await LedgerAsync(original, product);
        Assert.Equal((0, 0m, 0m), await BalancesAsync(warehouseA, product));
        Assert.Equal((0, 0m, 0m), await BalancesAsync(warehouseB, product));

        using var clientA = new Gateway();
        using var clientB = new Gateway();
        var (tokenA, epochA) = await AdmitAsync(clientA);
        var (tokenB, epochB) = await AdmitAsync(clientB);
        Assert.Equal(epochA, epochB);

        // SQL decimal(?,4) receipt values are 3 * .3333 = .9999
        // and 3 * .6667 = 2.0001, with independent warehouse stock keys.
        var recA = await ReceiptAsync(clientA, tokenA, epochA, warehouseA, employee, product, "3.0000", "0.3333");
        var recB = await ReceiptAsync(clientB, tokenB, epochB, warehouseB, employee, product, "3.0000", "0.6667");
        Assert.NotEqual(recA.Id, recB.Id);
        Assert.Equal((1, 3m, 0.9999m), await BalancesAsync(warehouseA, product));
        Assert.Equal((1, 3m, 2.0001m), await BalancesAsync(warehouseB, product));

        var saleA = await SaleAsync(clientA, tokenA, epochA, customer, warehouseA, employee, product,
            new[] { "0.5000", "0.5000" });
        var saleB = await SaleAsync(clientB, tokenB, epochB, customer, warehouseB, employee, product,
            new[] { "2.0000" });
        Assert.NotEqual(saleA.Id, saleB.Id);
        var postAKey = Guid.NewGuid();
        var postBKey = Guid.NewGuid();

        // Parallel Kestrel HTTPS requests with the same ProductId but
        // different WarehouseId keys must not cross-charge COGS.
        var posts = await Task.WhenAll(
            clientA.JsonAsync(HttpMethod.Post, "/api/v1/sales-receipts/" + saleA.Id + "/post",
                200, new { expectedVersion = saleA.Version }, tokenA, epochA, postAKey),
            clientB.JsonAsync(HttpMethod.Post, "/api/v1/sales-receipts/" + saleB.Id + "/post",
                200, new { expectedVersion = saleB.Version }, tokenB, epochB, postBKey));
        using (posts[0]) using (posts[1])
        {
            Assert.Equal("Posted", String(posts[0].RootElement.GetProperty("data"), "status"));
            Assert.Equal("Posted", String(posts[1].RootElement.GetProperty("data"), "status"));
        }

        // The independent SQL Oracle checks exact four-place residual
        // assignment: .3333 cost for two equal lines => .1667 + .1666.
        Assert.Equal(new[] { 0.1667m, 0.1666m }, await LineCostsAsync(saleA.Id));
        Assert.Equal(new[] { 1.3334m }, await LineCostsAsync(saleB.Id));
        Assert.Equal((1, 2m, 0.6666m), await BalancesAsync(warehouseA, product));
        Assert.Equal((1, 1m, 0.6667m), await BalancesAsync(warehouseB, product));
        Assert.Equal((3, 2m, 0.6666m), await LedgerAsync(warehouseA, product));
        Assert.Equal((2, 1m, 0.6667m), await LedgerAsync(warehouseB, product));
        Assert.Equal(originalBefore, await BalancesAsync(original, product));
        Assert.Equal(ledgerBefore, await LedgerAsync(original, product));

        await VerifyInventoryApiAsync(clientA, tokenA, warehouseA, product, 2m, 0.6666m, 0.3333m);
        await VerifyInventoryApiAsync(clientB, tokenB, warehouseB, product, 1m, 0.6667m, 0.6667m);
        foreach (var (saleId, operation) in new[] { (saleA.Id, postAKey), (saleB.Id, postBKey) })
        {
            Assert.Equal(1, await CountAsync(
                "SELECT COUNT(*) FROM dbo.WebOperationJournal WHERE OperationId=@id AND Disposition='COMMITTED';",
                "@id", operation));
            Assert.Equal(1, await CountAsync(
                "SELECT COUNT(*) FROM dbo.SalesReceipts WHERE SaleId=@id AND Status='Posted' AND PostedAtUtc IS NOT NULL;",
                "@id", saleId));
        }
    }
}
#endif
