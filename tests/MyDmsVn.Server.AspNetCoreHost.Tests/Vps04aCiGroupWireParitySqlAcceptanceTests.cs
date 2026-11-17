// WEB2–WEB6 bounded, actual Kestrel HTTPS -> disposable SQL acceptance.
// An allowlisted, read-only nine-table Oracle supplies independently derived
// expected business values. Safe per-group JSON reports contain ONLY selected
// synthetic IDs/statuses/field names and expected/actual/diff; never credentials,
// JWTs, cookies, raw request bodies, or full sensitive HTTP response payloads.
// WEB6 in this WEB5/Linux host proves disabled/fail-closed behavior ONLY.
#if VPS04A_LIVE
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps04aCiGroupWireParitySqlAcceptanceTests
{
    private const string Origin = "https://localhost:50443";
    private static string Required(string key) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } value
            ? value : throw new InvalidOperationException("Missing disposable parity CI environment: " + key);
    private static int Id(string key) => int.Parse(Required(key), CultureInfo.InvariantCulture);
    private static string S(JsonElement element, string name) => element.GetProperty(name).GetString()!;
    private static long L(JsonElement element, string name) => long.Parse(S(element, name), CultureInfo.InvariantCulture);
    private static decimal D(JsonElement element, string name) =>
        decimal.Parse(S(element, name), CultureInfo.InvariantCulture);

    private static void Guard()
    {
        Assert.Equal("approved-container-only", Required("VPS04A_CI_EPHEMERAL_SQL"));
        Assert.Equal("approved-nonproduction-only", Required("VPS04A_LIVE_ALLOW_MUTATION"));
        Assert.Equal("approved-ci-only", Required("VPS04A_CI_FAULT_APPROVED"));
        Assert.Equal("web5", Required("MYDMSVN_WEB_OPERATION_MODE"));
        Assert.Equal(Origin, Required("MYDMSVN_WEB_ORIGIN"));
        Assert.True(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MYDMSVN_WEB6_RESEND_ELIGIBILITY")) ||
                    Environment.GetEnvironmentVariable("MYDMSVN_WEB6_RESEND_ELIGIBILITY") == "disabled");
        Assert.Null(Environment.GetEnvironmentVariable("MYDMSVN_WEB6_CANDIDATE_ID"));
        Assert.Null(Environment.GetEnvironmentVariable("MYDMSVN_WEB6_CANDIDATE_ROOT"));
        Assert.Null(Environment.GetEnvironmentVariable("MYDMSVN_WEB_SECURITY_AUDIT_PATH"));
    }

    private sealed record Session(string Token, int UserId, Guid SessionId);
    private sealed record Reply(int Status, JsonDocument Body) : IDisposable
    {
        public void Dispose() => Body.Dispose();
    }

    private sealed class Gateway : IDisposable
    {
        private readonly HttpClient _client = new(new HttpClientHandler { UseCookies = false })
        {
            BaseAddress = new Uri(Origin + "/"),
            Timeout = TimeSpan.FromSeconds(45)
        };

        public async Task<Reply> GetAsync(string path, string? bearer = null, string? epoch = null,
            string? fingerprint = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path.TrimStart('/'));
            if (bearer != null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
            if (epoch != null) request.Headers.TryAddWithoutValidation("X-MyDmsVn-Deployment-Epoch", epoch);
            if (fingerprint != null) request.Headers.TryAddWithoutValidation("X-MyDmsVn-Operation-Fingerprint", fingerprint);
            return await SendAsync(request);
        }

        public async Task<Session> LoginAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/login");
            request.Headers.TryAddWithoutValidation("Origin", Origin);
            request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            request.Content = new StringContent(JsonSerializer.Serialize(new
            {
                username = Required("VPS04A_LIVE_USERNAME"),
                password = Required("VPS04A_LIVE_PASSWORD")
            }), Encoding.UTF8, "application/json");
            using var response = await SendAsync(request);
            Assert.Equal(200, response.Status);
            var data = response.Body.RootElement.GetProperty("data");
            var token = S(data, "accessToken");
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            var sid = Guid.Parse(jwt.Claims.Single(c => c.Type == "sid").Value);
            var userId = data.GetProperty("user").GetProperty("userId").GetInt32();
            return new Session(token, userId, sid);
        }

        private async Task<Reply> SendAsync(HttpRequestMessage request)
        {
            using var response = await _client.SendAsync(request);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.True(response.Headers.TryGetValues("Cache-Control", out var cache) &&
                cache.Any(v => v.Contains("no-store", StringComparison.OrdinalIgnoreCase)));
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return new Reply((int)response.StatusCode, JsonDocument.Parse(bytes));
        }

        public void Dispose() => _client.Dispose();
    }

    private static SqlConnection Oracle() => new(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));

    private static async Task<int> ScalarIntAsync(string sql, string? parameter = null, object? value = null)
    {
        using var connection = Oracle();
        await connection.OpenAsync();
        using var command = new SqlCommand(sql, connection);
        if (parameter != null) command.Parameters.AddWithValue(parameter, value!);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<(long Id, string State)> LastDocumentAsync(string table, string idColumn)
    {
        // Both SQL identifiers must be hard-coded within this allowlist: the
        // Oracle is prohibited from reading catalog/user/security tables.
        Assert.True((table == "SalesReceipts" && idColumn == "SaleId") ||
                    (table == "GoodsReceipts" && idColumn == "ReceiptId"));
        using var connection = Oracle();
        await connection.OpenAsync();
        using var command = new SqlCommand(
            "SELECT TOP(1) " + idColumn + ",Status FROM dbo." + table +
            " WHERE Status='Posted' ORDER BY " + idColumn + " DESC;", connection);
        using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Expected posted synthetic CI source");
        return (reader.GetInt64(0), reader.GetString(1));
    }

    private sealed record Journal(Guid OperationId, string Fingerprint, Guid Epoch,
        long DocumentId, int ActorUserId);
    private static async Task<Journal> LatestSalesCreateJournalAsync(int actor)
    {
        using var sql = Oracle();
        await sql.OpenAsync();
        using var command = new SqlCommand(@"
SELECT TOP(1) OperationId, RequestFingerprint, DeploymentEpoch, ResultDocumentId, ActorUserId
FROM dbo.WebOperationJournal
WHERE ActorUserId=@actor AND OperationKind=1 AND Disposition='COMMITTED'
  AND ResultDocumentId IS NOT NULL
ORDER BY CreatedAtUtc DESC;", sql);
        command.Parameters.Add("@actor", SqlDbType.Int).Value = actor;
        using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "No journalled Sales.Create source for WEB4/5");
        return new Journal(reader.GetGuid(0),
            Convert.ToHexString((byte[])reader.GetValue(1)).ToLowerInvariant(),
            reader.GetGuid(2), reader.GetInt64(3), reader.GetInt32(4));
    }

    private static async Task<(decimal Quantity, decimal Value)> BalanceAsync(int wh, int product)
    {
        using var sql = Oracle();
        await sql.OpenAsync();
        using var cmd = new SqlCommand(@"
SELECT Quantity,InventoryValue FROM dbo.StockBalances
WHERE WarehouseId=@wh AND ProductId=@product;", sql);
        cmd.Parameters.Add("@wh", SqlDbType.Int).Value = wh;
        cmd.Parameters.Add("@product", SqlDbType.Int).Value = product;
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "The seeded product must have a balance");
        var row = (reader.GetDecimal(0), reader.GetDecimal(1));
        Assert.False(await reader.ReadAsync());
        return row;
    }

    private static async Task<(int Rows, decimal Quantity, decimal Value)> StockCardAsync(int wh, int product)
    {
        using var sql = Oracle();
        await sql.OpenAsync();
        using var cmd = new SqlCommand(@"
SELECT COUNT(*),COALESCE(SUM(CONVERT(decimal(28,4),l.QuantityChange)),0),
 COALESCE(SUM(CONVERT(decimal(28,4),v.ValueChange)),0)
FROM dbo.StockLedger l JOIN dbo.StockValuationEntries v ON v.LedgerId=l.LedgerId
WHERE l.WarehouseId=@wh AND l.ProductId=@product;", sql);
        cmd.Parameters.Add("@wh", SqlDbType.Int).Value = wh;
        cmd.Parameters.Add("@product", SqlDbType.Int).Value = product;
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetDecimal(1), reader.GetDecimal(2));
    }

    private static async Task<int> SqlSessionActorAsync(Guid sid)
    {
        using var sql = Oracle();
        await sql.OpenAsync();
        using var command = new SqlCommand(
            "SELECT UserId FROM dbo.WebAuthSessions WHERE SessionId=@sid AND RevokedAtUtc IS NULL;", sql);
        command.Parameters.Add("@sid", SqlDbType.UniqueIdentifier).Value = sid;
        var result = await command.ExecuteScalarAsync();
        Assert.NotNull(result);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static string Keys(JsonElement obj) => string.Join(",",
        obj.EnumerateObject().Select(p => p.Name).OrderBy(k => k, StringComparer.Ordinal));

    private static string FixtureKeys(string family, string filename, params string[] path)
    {
        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (root != null && !File.Exists(Path.Combine(root.FullName, "docs", "contracts", "corpus-blobs.sha1")))
            root = root.Parent;
        Assert.NotNull(root);
        var fixture = Path.Combine(root!.FullName, "docs", "contracts", family, filename);
        using var document = JsonDocument.Parse(File.ReadAllBytes(fixture));
        var element = document.RootElement;
        foreach (var key in path) element = element.GetProperty(key);
        return Keys(element);
    }

    private static string FixtureArrayItemKeys(string family, string filename)
    {
        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (root != null && !File.Exists(Path.Combine(root.FullName, "docs", "contracts", "corpus-blobs.sha1")))
            root = root.Parent;
        Assert.NotNull(root);
        using var document = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(root!.FullName, "docs", "contracts", family, filename)));
        return Keys(document.RootElement.GetProperty("data").GetProperty("items")[0]);
    }

    private static string FixtureLookupRowKeys(string family, string filename)
    {
        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (root != null && !File.Exists(Path.Combine(root.FullName, "docs", "contracts", "corpus-blobs.sha1")))
            root = root.Parent;
        Assert.NotNull(root);
        using var document = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(root!.FullName, "docs", "contracts", family, filename)));
        return Keys(document.RootElement.GetProperty("data")[0]);
    }

    private static string Code(Reply reply) =>
        S(reply.Body.RootElement.GetProperty("error"), "code");

    // Only safe, explicitly selected projections ever reach the report.
    // Every discrepancy is surfaced with its JSON path and both values.
    private sealed class GroupReport
    {
        private readonly string _name;
        private readonly List<object> _scenarios = new();
        public GroupReport(string name) => _name = name;
        private static string Location => Path.Combine(
            Required("GITHUB_WORKSPACE"), "vps04a-ci-parity");

        public void Record(string id, string route, string fixture, string oracle,
            object expected, object actual)
        {
            using var expectedDoc = JsonDocument.Parse(JsonSerializer.Serialize(expected));
            using var actualDoc = JsonDocument.Parse(JsonSerializer.Serialize(actual));
            var differences = new List<object>();
            Compare(expectedDoc.RootElement, actualDoc.RootElement, "$", differences);
            _scenarios.Add(new {
                id, route, fixture, sqlOracle = oracle,
                expected = expectedDoc.RootElement.Clone(),
                actual = actualDoc.RootElement.Clone(),
                diff = differences
            });
            Directory.CreateDirectory(Location);
            File.WriteAllText(Path.Combine(Location, _name + ".json"), JsonSerializer.Serialize(new {
                group = _name, environment = "disposable-sql13", mode = "web5",
                scope = _name == "WEB6" ? "disabled-fail-closed-only" : "bounded-live-positive-and-negative",
                cases = _scenarios
            }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            Assert.Empty(differences);
        }

        private static void Compare(JsonElement expected, JsonElement actual, string path, List<object> diff)
        {
            if (expected.ValueKind != actual.ValueKind)
            {
                diff.Add(new { path, expected = expected.GetRawText(), actual = actual.GetRawText() });
                return;
            }
            if (expected.ValueKind == JsonValueKind.Object)
            {
                var keys = expected.EnumerateObject().Select(p => p.Name)
                    .Union(actual.EnumerateObject().Select(p => p.Name))
                    .OrderBy(x => x, StringComparer.Ordinal);
                foreach (var key in keys)
                {
                    var hasExpected = expected.TryGetProperty(key, out var e);
                    var hasActual = actual.TryGetProperty(key, out var a);
                    if (!hasExpected || !hasActual)
                        diff.Add(new { path = path + "." + key, expected = hasExpected ? e.GetRawText() : "(missing)",
                            actual = hasActual ? a.GetRawText() : "(missing)" });
                    else Compare(e, a, path + "." + key, diff);
                }
            }
            else if (expected.ValueKind == JsonValueKind.Array)
            {
                if (expected.GetArrayLength() != actual.GetArrayLength())
                    diff.Add(new { path, expected = expected.GetArrayLength().ToString(CultureInfo.InvariantCulture),
                        actual = actual.GetArrayLength().ToString(CultureInfo.InvariantCulture) });
                else for (var index = 0; index < expected.GetArrayLength(); index++)
                    Compare(expected[index], actual[index], path + "[" + index + "]", diff);
            }
            else if (expected.GetRawText() != actual.GetRawText())
                diff.Add(new { path, expected = expected.GetRawText(), actual = actual.GetRawText() });
        }
    }

    [Fact]
    public async Task Web2AuthCatalogAndSalesLiveFieldsMatchSqlOracle()
    {
        Guard();
        var report = new GroupReport("WEB2");
        using var http = new Gateway();
        var session = await http.LoginAsync();
        var sqlActor = await SqlSessionActorAsync(session.SessionId);
        using (var me = await http.GetAsync("/api/v1/auth/me", session.Token))
            report.Record("auth-me", "GET /api/v1/auth/me", "web2-v1/auth-me-response.json",
                "WebAuthSessions(SessionId)->UserId",
                new { status = 200, userId = sqlActor, username = Required("VPS04A_LIVE_USERNAME"),
                    fields = FixtureKeys("web2-v1", "auth-me-response.json", "data") },
                new { status = me.Status, userId = me.Body.RootElement.GetProperty("data").GetProperty("userId").GetInt32(),
                    username = S(me.Body.RootElement.GetProperty("data"), "username"),
                    fields = Keys(me.Body.RootElement.GetProperty("data")) });

        using (var capability = await http.GetAsync("/api/v1/auth/capabilities?feature=sales", session.Token))
        {
            var data = capability.Body.RootElement.GetProperty("data");
            report.Record("capabilities-sales", "GET /api/v1/auth/capabilities?feature=sales",
                "web2-v1/auth-capabilities-response.json", "WebAuthSessions active superuser admission",
                new { status = 200, read = true, write = true, post = true,
                    fields = FixtureKeys("web2-v1", "auth-capabilities-response.json", "data") },
                new { status = capability.Status, read = data.GetProperty("read").GetBoolean(),
                    write = data.GetProperty("write").GetBoolean(), post = data.GetProperty("post").GetBoolean(),
                    fields = Keys(data) });
        }

        var product = Id("VPS04A_LIVE_PRODUCT_ID");
        var stockKeys = await ScalarIntAsync("SELECT COUNT(*) FROM dbo.StockBalances WHERE ProductId=@product;", "@product", product);
        Assert.True(stockKeys > 0, "Seeded product must have independent stock provenance");
        using (var lookup = await http.GetAsync("/api/v1/catalog/products/lookup?limit=25", session.Token))
        {
            var rows = lookup.Body.RootElement.GetProperty("data");
            var matched = rows.EnumerateArray().Where(x => x.GetProperty("id").GetInt32() == product).ToArray();
            report.Record("catalog-products", "GET /api/v1/catalog/products/lookup?limit=25",
                "web2-v1/catalog-products-response.json", "StockBalances.ProductId provenance (catalog table not Oracle-readable)",
                new { status = 200, seededProductMatched = 1, seededStockKeysPresent = true,
                    rowFields = FixtureLookupRowKeys("web2-v1", "catalog-products-response.json") },
                new { status = lookup.Status, seededProductMatched = matched.Length, seededStockKeysPresent = stockKeys > 0,
                    rowFields = matched.Length == 1 ? Keys(matched[0]) : "(missing)" });
        }


        // customers: Oracle sees only document reference keys, not catalog
        // rows; verify the lookup contains the independently posted reference.
        var customerId = await ScalarIntAsync("SELECT TOP(1) CustomerId FROM dbo.SalesReceipts ORDER BY SaleId DESC;");
        using (var lookup = await http.GetAsync("/api/v1/catalog/customers/lookup?limit=25", session.Token))
        {
            var rows = lookup.Body.RootElement.GetProperty("data");
            var matched = rows.EnumerateArray()
                .Where(x => x.GetProperty("id").GetInt32() == customerId).ToArray();
            report.Record("catalog-customers", "GET /api/v1/catalog/customers/lookup?limit=25",
                "web2-v1/catalog-customers-response.json", "SalesReceipts.CustomerId referenced from posted source rows",
                new { status = 200, referencedRowsMatched = 1, referenceId = customerId,
                    rowFields = FixtureLookupRowKeys("web2-v1", "catalog-customers-response.json") },
                new { status = lookup.Status, referencedRowsMatched = matched.Length,
                    referenceId = matched.Length == 1 ? matched[0].GetProperty("id").GetInt32() : -1,
                    rowFields = matched.Length == 1 ? Keys(matched[0]) : "(missing)" });
        }

        // warehouses: Oracle sees only document reference keys, not catalog
        // rows; verify the lookup contains the independently posted reference.
        var warehouseId = await ScalarIntAsync("SELECT TOP(1) WarehouseId FROM dbo.GoodsReceipts ORDER BY ReceiptId DESC;");
        using (var lookup = await http.GetAsync("/api/v1/catalog/warehouses/lookup?limit=25", session.Token))
        {
            var rows = lookup.Body.RootElement.GetProperty("data");
            var matched = rows.EnumerateArray()
                .Where(x => x.GetProperty("id").GetInt32() == warehouseId).ToArray();
            report.Record("catalog-warehouses", "GET /api/v1/catalog/warehouses/lookup?limit=25",
                "web2-v1/catalog-warehouses-response.json", "GoodsReceipts.WarehouseId referenced from posted source rows",
                new { status = 200, referencedRowsMatched = 1, referenceId = warehouseId,
                    rowFields = FixtureLookupRowKeys("web2-v1", "catalog-warehouses-response.json") },
                new { status = lookup.Status, referencedRowsMatched = matched.Length,
                    referenceId = matched.Length == 1 ? matched[0].GetProperty("id").GetInt32() : -1,
                    rowFields = matched.Length == 1 ? Keys(matched[0]) : "(missing)" });
        }

        // employees: Oracle sees only document reference keys, not catalog
        // rows; verify the lookup contains the independently posted reference.
        var employeeId = await ScalarIntAsync("SELECT TOP(1) EmployeeId FROM dbo.GoodsReceipts ORDER BY ReceiptId DESC;");
        using (var lookup = await http.GetAsync("/api/v1/catalog/employees/lookup?limit=25", session.Token))
        {
            var rows = lookup.Body.RootElement.GetProperty("data");
            var matched = rows.EnumerateArray()
                .Where(x => x.GetProperty("id").GetInt32() == employeeId).ToArray();
            report.Record("catalog-employees", "GET /api/v1/catalog/employees/lookup?limit=25",
                "web2-v1/catalog-employees-response.json", "GoodsReceipts.EmployeeId referenced from posted source rows",
                new { status = 200, referencedRowsMatched = 1, referenceId = employeeId,
                    rowFields = FixtureLookupRowKeys("web2-v1", "catalog-employees-response.json") },
                new { status = lookup.Status, referencedRowsMatched = matched.Length,
                    referenceId = matched.Length == 1 ? matched[0].GetProperty("id").GetInt32() : -1,
                    rowFields = matched.Length == 1 ? Keys(matched[0]) : "(missing)" });
        }

        var sqlCount = await ScalarIntAsync("SELECT COUNT(*) FROM dbo.SalesReceipts;");
        using (var list = await http.GetAsync("/api/v1/sales-receipts?pageSize=25", session.Token))
        {
            var items = list.Body.RootElement.GetProperty("data").GetProperty("items");
            report.Record("sales-list-count", "GET /api/v1/sales-receipts?pageSize=25",
                "web2-v1/sales-list-response.json", "SELECT COUNT(*) FROM dbo.SalesReceipts",
                new { status = 200, visibleItems = Math.Min(sqlCount, 25) },
                new { status = list.Status, visibleItems = items.GetArrayLength() });
        }
        var sale = await LastDocumentAsync("SalesReceipts", "SaleId");
        using (var detail = await http.GetAsync("/api/v1/sales-receipts/" + sale.Id, session.Token))
        {
            var data = detail.Body.RootElement.GetProperty("data");
            report.Record("sales-get-posted", "GET /api/v1/sales-receipts/{saleId}",
                "web2-v1/sales-get-posted-response.json", "SalesReceipts(SaleId,Status)",
                new { status = 200, id = sale.Id, documentState = sale.State },
                new { status = detail.Status, id = L(data, "saleId"), documentState = S(data, "status") });
        }
        var journalBefore = await ScalarIntAsync("SELECT COUNT(*) FROM dbo.WebOperationJournal;");
        using (var missing = await http.GetAsync("/api/v1/sales-receipts/9223372036854775807", session.Token))
            report.Record("sales-get-not-found", "GET /api/v1/sales-receipts/{missing}",
                "web2-v1/error-404.json", "WebOperationJournal count unchanged on denied read",
                new { status = 404, code = "SalesReceipt.NotFound", journalCount = journalBefore },
                new { status = missing.Status, code = Code(missing),
                    journalCount = await ScalarIntAsync("SELECT COUNT(*) FROM dbo.WebOperationJournal;") });
    }

    [Fact]
    public async Task Web3GoodsReceiptsInventoryAndErrorsMatchSqlOracle()
    {
        Guard();
        var report = new GroupReport("WEB3");
        using var http = new Gateway();
        var session = await http.LoginAsync();
        var sqlCount = await ScalarIntAsync("SELECT COUNT(*) FROM dbo.GoodsReceipts;");
        using (var list = await http.GetAsync("/api/v1/goods-receipts?pageNumber=1&pageSize=25", session.Token))
        {
            var data = list.Body.RootElement.GetProperty("data");
            report.Record("receipts-list", "GET /api/v1/goods-receipts?pageNumber=1&pageSize=25",
                "web3-v1/receipt-list-response.json", "SELECT COUNT(*) FROM dbo.GoodsReceipts",
                new { status = 200, totalCount = sqlCount, items = Math.Min(sqlCount, 25) },
                new { status = list.Status, totalCount = int.Parse(S(data, "totalCount"), CultureInfo.InvariantCulture),
                    items = data.GetProperty("items").GetArrayLength() });
        }
        var receipt = await LastDocumentAsync("GoodsReceipts", "ReceiptId");
        using (var detail = await http.GetAsync("/api/v1/goods-receipts/" + receipt.Id, session.Token))
        {
            var data = detail.Body.RootElement.GetProperty("data");
            report.Record("receipt-get-posted", "GET /api/v1/goods-receipts/{receiptId}",
                "web3-v1/receipt-get-posted-response.json", "GoodsReceipts(ReceiptId,Status)",
                new { status = 200, id = receipt.Id, documentState = receipt.State,
                    fields = FixtureKeys("web3-v1", "receipt-get-posted-response.json", "data") },
                new { status = detail.Status, id = L(data, "receiptId"), documentState = S(data, "status"),
                    fields = Keys(data) });
        }

        var wh = Id("VPS04A_LIVE_WAREHOUSE_ID");
        var product = Id("VPS04A_LIVE_PRODUCT_ID");
        var balance = await BalanceAsync(wh, product);
        using (var balances = await http.GetAsync(
            "/api/v1/inventory/balances?pageNumber=1&pageSize=25&warehouseId=" + wh + "&productId=" + product, session.Token))
        {
            var data = balances.Body.RootElement.GetProperty("data");
            var items = data.GetProperty("items");
            Assert.Equal(1, items.GetArrayLength());
            var row = items[0];
            report.Record("inventory-balance", "GET /api/v1/inventory/balances?warehouseId={seed}&productId={seed}",
                "web3-v1/balances-response.json", "StockBalances(Quantity,InventoryValue)",
                new { status = 200, quantity = balance.Quantity.ToString("F4", CultureInfo.InvariantCulture),
                    value = balance.Value.ToString("F4", CultureInfo.InvariantCulture),
                    rowFields = FixtureArrayItemKeys("web3-v1", "balances-response.json") },
                new { status = balances.Status, quantity = S(row, "quantity"), value = S(row, "inventoryValue"),
                    rowFields = Keys(row) });
        }
        var ledger = await StockCardAsync(wh, product);
        using (var card = await http.GetAsync(
            "/api/v1/inventory/stock-card?pageNumber=1&pageSize=25&warehouseId=" + wh + "&productId=" + product,
            session.Token))
        {
            var items = card.Body.RootElement.GetProperty("data").GetProperty("items");
            report.Record("inventory-stock-card", "GET /api/v1/inventory/stock-card?warehouseId={seed}&productId={seed}",
                "web3-v1/stock-card-response.json", "StockLedger JOIN StockValuationEntries",
                new { status = 200, rows = ledger.Rows, quantityChange = ledger.Quantity.ToString("F4", CultureInfo.InvariantCulture),
                    valueChange = ledger.Value.ToString("F4", CultureInfo.InvariantCulture) },
                new { status = card.Status, rows = items.GetArrayLength(),
                    quantityChange = items.EnumerateArray().Sum(x => D(x, "quantityChange")).ToString("F4", CultureInfo.InvariantCulture),
                    valueChange = items.EnumerateArray().Sum(x => D(x, "valueChange")).ToString("F4", CultureInfo.InvariantCulture) });
        }
        var before = await ScalarIntAsync("SELECT COUNT(*) FROM dbo.WebOperationJournal;");
        using (var missing = await http.GetAsync("/api/v1/goods-receipts/9223372036854775807", session.Token))
            report.Record("receipt-get-not-found", "GET /api/v1/goods-receipts/{missing}",
                "web3-v1/error-404-not-found.json", "WebOperationJournal unchanged",
                new { status = 404, code = "GoodsReceipt.NotFound", journalCount = before },
                new { status = missing.Status, code = Code(missing),
                    journalCount = await ScalarIntAsync("SELECT COUNT(*) FROM dbo.WebOperationJournal;") });
    }

    [Fact]
    public async Task Web4OperationContextAndCommittedOutcomeMatchSqlOracle()
    {
        Guard();
        var report = new GroupReport("WEB4");
        using var http = new Gateway();
        var session = await http.LoginAsync();
        var journal = await LatestSalesCreateJournalAsync(session.UserId);
        using (var context = await http.GetAsync("/api/v1/operations/context", session.Token))
        {
            var data = context.Body.RootElement.GetProperty("data");
            report.Record("context", "GET /api/v1/operations/context", "web4-v1/context-response.json",
                "WebAuthSessions(Sid)+WebOperationJournal(DeploymentEpoch)",
                new { status = 200, contractVersion = "web4-v1", actorUserId = await SqlSessionActorAsync(session.SessionId),
                    epoch = journal.Epoch.ToString("D"),
                    fields = FixtureKeys("web4-v1", "context-response.json", "data") },
                new { status = context.Status, contractVersion = S(data, "contractVersion"),
                    actorUserId = data.GetProperty("actorUserId").GetInt32(), epoch = S(data, "deploymentEpoch"),
                    fields = Keys(data) });
        }
        var route = "/api/v1/operations/" + journal.OperationId.ToString("D");
        using (var committed = await http.GetAsync(route, session.Token, journal.Epoch.ToString("D"), journal.Fingerprint))
        {
            var data = committed.Body.RootElement.GetProperty("data");
            report.Record("sales-create-committed", "GET /api/v1/operations/{operationId}",
                "web4-v1/sales-create-status.json", "WebOperationJournal exact committed Sales.Create",
                new { status = 200, kind = "Sales.Create", state = "COMMITTED",
                    operationId = journal.OperationId.ToString("D"), fingerprint = journal.Fingerprint,
                    documentId = journal.DocumentId },
                new { status = committed.Status, kind = S(data, "operationKind"), state = S(data, "state"),
                    operationId = S(data, "operationId"), fingerprint = S(data, "requestFingerprint"),
                    documentId = L(data.GetProperty("outcome").GetProperty("data"), "saleId") });
        }
        using (var absent = await http.GetAsync("/api/v1/operations/" + Guid.NewGuid().ToString("D"),
            session.Token, journal.Epoch.ToString("D"), journal.Fingerprint))
            report.Record("status-not-observed", "GET /api/v1/operations/{unknown}",
                "web4-v1/error-operation-notobserved.json", "WebOperationJournal contains no matching actor/key",
                new { status = 404, code = "Operation.NotObserved" },
                new { status = absent.Status, code = Code(absent) });
        using (var stale = await http.GetAsync(route, session.Token, Guid.NewGuid().ToString("D"), journal.Fingerprint))
            report.Record("status-stale-epoch", "GET /api/v1/operations/{operationId} with stale epoch",
                "web4-v1/error-deployment-epochmismatch.json", "WebOperationJournal epoch unchanged",
                new { status = 409, code = "Deployment.EpochMismatch" },
                new { status = stale.Status, code = Code(stale) });
    }

    [Fact]
    public async Task Web5ResolutionContextAndCommittedOutcomeMatchSqlOracle()
    {
        Guard();
        var report = new GroupReport("WEB5");
        using var http = new Gateway();
        var session = await http.LoginAsync();
        var journal = await LatestSalesCreateJournalAsync(session.UserId);
        using (var context = await http.GetAsync("/api/v2/operations/context", session.Token))
        {
            var data = context.Body.RootElement.GetProperty("data");
            report.Record("context", "GET /api/v2/operations/context",
                "web5-resolution-v1/context-response.json", "WebAuthSessions + WebOperationJournal(epoch)",
                new { status = 200, version = "web5-resolution-v1", actor = await SqlSessionActorAsync(session.SessionId),
                    epoch = journal.Epoch.ToString("D"), resend = false,
                    fields = FixtureKeys("web5-resolution-v1", "context-response.json", "data") },
                new { status = context.Status, version = S(data, "contractVersion"),
                    actor = data.GetProperty("actorUserId").GetInt32(), epoch = S(data, "deploymentEpoch"),
                    resend = data.GetProperty("sameKeyResendEnabled").GetBoolean(), fields = Keys(data) });
        }
        using (var resolved = await http.GetAsync("/api/v2/operations/" + journal.OperationId.ToString("D") + "/resolution",
            session.Token, journal.Epoch.ToString("D"), journal.Fingerprint))
        {
            var data = resolved.Body.RootElement.GetProperty("data");
            report.Record("committed-sales-create", "GET /api/v2/operations/{operationId}/resolution",
                "web5-resolution-v1/sales-create-committed.json", "WebOperationJournal committed Sales.Create + no terminal decision",
                new { status = 200, contract = "web5-resolution-v1", actor = journal.ActorUserId,
                    state = "COMMITTED", operationId = journal.OperationId.ToString("D"), saleId = journal.DocumentId,
                    terminalNull = true,
                    fields = FixtureKeys("web5-resolution-v1", "sales-create-committed.json", "data") },
                new { status = resolved.Status, contract = S(data, "contractVersion"),
                    actor = data.GetProperty("actorUserId").GetInt32(), state = S(data, "state"),
                    operationId = S(data, "operationId"),
                    saleId = L(data.GetProperty("outcome").GetProperty("data"), "saleId"),
                    terminalNull = data.GetProperty("terminal").ValueKind == JsonValueKind.Null, fields = Keys(data) });
        }
        using (var absent = await http.GetAsync("/api/v2/operations/" + Guid.NewGuid().ToString("D") + "/resolution",
            session.Token, journal.Epoch.ToString("D"), journal.Fingerprint))
            report.Record("resolution-not-observed", "GET /api/v2/operations/{unknown}/resolution",
                "web5-resolution-v1/error-operation-notobserved.json", "WebOperationJournal no matching actor/key",
                new { status = 404, code = "Operation.NotObserved" },
                new { status = absent.Status, code = Code(absent) });
    }

    [Fact]
    public async Task Web6AllThreeRoutesFailClosedWithoutProtectedLinuxAudit()
    {
        Guard();
        var report = new GroupReport("WEB6");
        using var http = new Gateway();
        var session = await http.LoginAsync();
        var journal = await LatestSalesCreateJournalAsync(session.UserId);
        var before = await ScalarIntAsync("SELECT COUNT(*) FROM dbo.WebOperationJournal;");
        var requests = new [] {
            ("context-disabled", "/api/v3/operations/resend-context"),
            ("receipt-disabled", "/api/v3/operations/" + journal.OperationId.ToString("D") + "/resend-receipt"),
            ("eligibility-disabled", "/api/v3/operations/" + journal.OperationId.ToString("D") + "/resend-eligibility")
        };
        foreach (var (id, route) in requests)
        {
            using var response = await http.GetAsync(route, session.Token,
                journal.Epoch.ToString("D"), journal.Fingerprint);
            report.Record(id, "GET " + (id == "context-disabled" ? "/api/v3/operations/resend-context" :
                "/api/v3/operations/{operationId}/" + (id == "receipt-disabled" ? "resend-receipt" : "resend-eligibility")),
                "WEB6 disabled by net10 Linux protected-audit admission boundary",
                "WebOperationJournal count unchanged; WEB6 enabled fixture NOT replayed",
                new { status = 404, code = "NotFound", journalCount = before },
                new { status = response.Status, code = Code(response),
                    journalCount = await ScalarIntAsync("SELECT COUNT(*) FROM dbo.WebOperationJournal;") });
        }
    }
}
#endif
