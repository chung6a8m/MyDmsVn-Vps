// Disposable-CI-only, two-client oversell race against real HTTPS and SQL13.
// This MUST run after the six normal SQL cases and before the fault/recovery
// phases. Never execute against independently protected staging or production.
#if VPS04A_LIVE
using System;
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

public sealed class Vps04aCiOversellSqlAcceptanceTests
{
    private static string Required(string key) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } value
        ? value : throw new InvalidOperationException("Missing CI-only acceptance configuration: " + key);

    private static void Guard()
    {
        Assert.Equal("approved-container-only", Required("VPS04A_CI_EPHEMERAL_SQL"));
        Assert.Equal("approved-ci-only", Required("VPS04A_CI_FAULT_APPROVED"));
        Assert.Equal("approved-nonproduction-only", Required("VPS04A_LIVE_ALLOW_MUTATION"));
        Assert.Equal("web5", Required("MYDMSVN_WEB_OPERATION_MODE"));
        Assert.Equal("https://localhost:50443", Required("MYDMSVN_WEB_ORIGIN"));
    }

    private static int Seed(string key) => int.Parse(Required(key), CultureInfo.InvariantCulture);
    private static string Field(JsonElement element, string key) => element.GetProperty(key).GetString()!;
    private static long SaleId(JsonDocument document) =>
        long.Parse(Field(document.RootElement.GetProperty("data"), "saleId"), CultureInfo.InvariantCulture);

    private sealed class Gateway : IDisposable
    {
        private readonly HttpClient _http = new(new HttpClientHandler { UseCookies = false })
        {
            BaseAddress = new Uri(Required("MYDMSVN_WEB_ORIGIN") + "/"),
            Timeout = TimeSpan.FromSeconds(45)
        };

        public async Task<(int Status, JsonDocument Body)> SendAsync(
            HttpMethod method, string route, object? payload = null, string? token = null,
            string? epoch = null, Guid? operation = null)
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
            if (operation.HasValue)
                request.Headers.TryAddWithoutValidation("Idempotency-Key", operation.Value.ToString("D"));
            if (payload != null)
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            else if (method != HttpMethod.Get)
                request.Content = new ByteArrayContent(Array.Empty<byte>());

            using var response = await _http.SendAsync(request);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.True(response.Headers.TryGetValues("Cache-Control", out var values) &&
                values.Any(value => value.Contains("no-store", StringComparison.OrdinalIgnoreCase)));
            return ((int)response.StatusCode,
                JsonDocument.Parse(await response.Content.ReadAsStringAsync()));
        }

        public void Dispose() => _http.Dispose();
    }

    private static async Task<(string Token, string Epoch)> AdmitAsync(Gateway gateway)
    {
        var login = await gateway.SendAsync(HttpMethod.Post, "/api/v1/auth/login",
            new { username = Required("VPS04A_LIVE_USERNAME"), password = Required("VPS04A_LIVE_PASSWORD") });
        using (login.Body)
        {
            Assert.Equal(200, login.Status);
            var token = Field(login.Body.RootElement.GetProperty("data"), "accessToken");
            var context = await gateway.SendAsync(HttpMethod.Get, "/api/v1/operations/context", token: token);
            using (context.Body)
            {
                Assert.Equal(200, context.Status);
                return (token, Field(context.Body.RootElement.GetProperty("data"), "deploymentEpoch"));
            }
        }
    }

    private static async Task<(long Id, string Version)> CreateAsync(
        Gateway gateway, string token, string epoch, int customer, int warehouse, int employee, int product)
    {
        var draft = new
        {
            saleDate = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            customerId = customer, warehouseId = warehouse, employeeId = employee,
            note = "VPS04A-CI-OVERSELL-" + Guid.NewGuid().ToString("N"),
            lines = new[] { new
            {
                saleLineId = "0", productId = product, qty = "4.0000",
                unitPrice = "30.0000", isGift = false
            }}
        };
        var created = await gateway.SendAsync(HttpMethod.Post, "/api/v1/sales-receipts",
            draft, token, epoch, Guid.NewGuid());
        using (created.Body)
        {
            Assert.Equal(200, created.Status);
            return (SaleId(created.Body), Field(created.Body.RootElement.GetProperty("data"), "version"));
        }
    }

    private static async Task<int> CountAsync(string sql, string param, object value)
    {
        using var connection = new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await connection.OpenAsync();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue(param, value);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<decimal> DecimalAsync(string sql, long sale)
    {
        using var connection = new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await connection.OpenAsync();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@sale", SqlDbType.BigInt).Value = sale;
        return Convert.ToDecimal(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<(int Rows, decimal Quantity, decimal Value)> BalanceAsync(int warehouse, int product)
    {
        using var connection = new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await connection.OpenAsync();
        using var command = new SqlCommand(@"
SELECT COUNT(*), COALESCE(SUM(CONVERT(decimal(28,4),Quantity)),0),
       COALESCE(SUM(CONVERT(decimal(28,4),InventoryValue)),0)
FROM dbo.StockBalances WHERE WarehouseId=@warehouse AND ProductId=@product;", connection);
        command.Parameters.Add("@warehouse", SqlDbType.Int).Value = warehouse;
        command.Parameters.Add("@product", SqlDbType.Int).Value = product;
        using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetDecimal(1), reader.GetDecimal(2));
    }

    [Fact]
    public async Task ConcurrentTwoClientOversellIsFailClosedAndNeverDoublePosts()
    {
        Guard();
        var warehouse = Seed("VPS04A_LIVE_WAREHOUSE_ID");
        var product = Seed("VPS04A_LIVE_PRODUCT_ID");
        var customer = Seed("VPS04A_LIVE_CUSTOMER_ID");
        var employee = Seed("VPS04A_LIVE_EMPLOYEE_ID");

        // Deliberately chained to the preceding golden stock/COGS CI phase:
        // 10 received, 5 issued, leaving 5 at a 10.0000 moving-average cost.
        // Leave at least 1 unit for the later isolated fault/retry phase.
        Assert.Equal((1, 5m, 50m), await BalanceAsync(warehouse, product));
        using var a = new Gateway();
        using var b = new Gateway();
        var (accessA, epochA) = await AdmitAsync(a);
        var (accessB, epochB) = await AdmitAsync(b);
        Assert.Equal(epochA, epochB);
        var draftA = await CreateAsync(a, accessA, epochA, customer, warehouse, employee, product);
        var draftB = await CreateAsync(b, accessB, epochB, customer, warehouse, employee, product);
        Assert.NotEqual(draftA.Id, draftB.Id);
        var keyA = Guid.NewGuid();
        var keyB = Guid.NewGuid();

        // Each Draft independently demands four units, but only five exist.
        // A genuine concurrent race must produce exactly one successful Post.
        var first = a.SendAsync(HttpMethod.Post,
            "/api/v1/sales-receipts/" + draftA.Id + "/post",
            new { expectedVersion = draftA.Version }, accessA, epochA, keyA);
        var second = b.SendAsync(HttpMethod.Post,
            "/api/v1/sales-receipts/" + draftB.Id + "/post",
            new { expectedVersion = draftB.Version }, accessB, epochB, keyB);
        var answers = await Task.WhenAll(first, second);
        using var firstBody = answers[0].Body;
        using var secondBody = answers[1].Body;
        Assert.Equal(new[] { 200, 400 }, answers.Select(a => a.Status).OrderBy(x => x).ToArray());

        var accepted = answers[0].Status == 200 ? 0 : 1;
        var rejected = 1 - accepted;
        var sales = new[] { draftA.Id, draftB.Id };
        var keys = new[] { keyA, keyB };
        Assert.Equal("Posted", Field(answers[accepted].Body.RootElement.GetProperty("data"), "status"));
        Assert.Equal(sales[accepted], SaleId(answers[accepted].Body));
        var error = answers[rejected].Body.RootElement.GetProperty("error");
        Assert.Equal("ValidationError", Field(error, "code"));
        Assert.Contains(error.GetProperty("details").EnumerateArray(),
            item => Field(item, "code") == "SalesReceipt.InsufficientStock");

        Assert.Equal((1, 1m, 10m), await BalanceAsync(warehouse, product));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.WebOperationJournal WHERE OperationId=@key AND Disposition='COMMITTED';",
            "@key", keys[accepted]));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM dbo.WebOperationJournal WHERE OperationId=@key;", "@key", keys[rejected]));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.StockLedger WHERE DocumentType='SalesReceipt' AND DocumentId=@sale;",
            "@sale", sales[accepted]));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM dbo.StockLedger WHERE DocumentType='SalesReceipt' AND DocumentId=@sale;",
            "@sale", sales[rejected]));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.StockValuationEntries v JOIN dbo.StockLedger l ON l.LedgerId=v.LedgerId WHERE l.DocumentType='SalesReceipt' AND l.DocumentId=@sale;",
            "@sale", sales[accepted]));
        Assert.Equal(40m, await DecimalAsync(
            "SELECT COALESCE(SUM(CostAmount),0) FROM dbo.SalesReceiptLines WHERE SaleId=@sale;", sales[accepted]));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.SalesReceipts WHERE SaleId=@sale AND Status='Draft' AND PostedAtUtc IS NULL;",
            "@sale", sales[rejected]));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.SalesReceiptLines WHERE SaleId=@sale AND CostAmount IS NULL AND PostedUnitCost IS NULL;",
            "@sale", sales[rejected]));

        // A successful original-key replay must not decrement twice; an
        // unsuccessful original-key retry must remain denied without a journal.
        var acceptedGateway = accepted == 0 ? a : b;
        var rejectedGateway = rejected == 0 ? a : b;
        var tokens = new[] { accessA, accessB };
        var epochs = new[] { epochA, epochB };
        var versions = new[] { draftA.Version, draftB.Version };
        var replay = await acceptedGateway.SendAsync(HttpMethod.Post,
            "/api/v1/sales-receipts/" + sales[accepted] + "/post",
            new { expectedVersion = versions[accepted] },
            tokens[accepted], epochs[accepted], keys[accepted]);
        using (replay.Body)
        {
            Assert.Equal(200, replay.Status);
            Assert.Equal(sales[accepted], SaleId(replay.Body));
        }
        var deniedAgain = await rejectedGateway.SendAsync(HttpMethod.Post,
            "/api/v1/sales-receipts/" + sales[rejected] + "/post",
            new { expectedVersion = versions[rejected] },
            tokens[rejected], epochs[rejected], keys[rejected]);
        using (deniedAgain.Body)
        {
            Assert.Equal(400, deniedAgain.Status);
            Assert.Equal("ValidationError", Field(deniedAgain.Body.RootElement.GetProperty("error"), "code"));
        }
        Assert.Equal((1, 1m, 10m), await BalanceAsync(warehouse, product));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM dbo.WebOperationJournal WHERE OperationId=@key;", "@key", keys[rejected]));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.WebOperationJournal WHERE OperationId=@key;", "@key", keys[accepted]));
    }
}
#endif
