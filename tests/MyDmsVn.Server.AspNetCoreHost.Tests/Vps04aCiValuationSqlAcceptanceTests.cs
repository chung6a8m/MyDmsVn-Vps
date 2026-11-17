// Stronger CI-only SQL/HTTP fidelity tests. The historical WEB2-WEB4
// JSON files are reviewed ILLUSTRATIVE wire shapes, not database fixtures.
// Runtime IDs/versions/dates vary; assert their structural contract and
// independent SQL quantities/valuation rather than rewriting goldens.
#if VPS04A_LIVE
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps04aCiValuationSqlAcceptanceTests
{
    private static string Required(string key) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } value
        ? value : throw new InvalidOperationException("Missing CI-owned acceptance configuration: " + key);

    private static void Guard()
    {
        Assert.Equal("approved-container-only", Required("VPS04A_CI_EPHEMERAL_SQL"));
        Assert.Equal("approved-nonproduction-only", Required("VPS04A_LIVE_ALLOW_MUTATION"));
        Assert.Equal("web5", Required("MYDMSVN_WEB_OPERATION_MODE"));
        Assert.Equal("https://localhost:50443", Required("MYDMSVN_WEB_ORIGIN"));
    }

    private sealed class Client : IDisposable
    {
        private readonly HttpClient _http = new(new HttpClientHandler { UseCookies = false })
        { BaseAddress = new Uri(Required("MYDMSVN_WEB_ORIGIN") + "/"), Timeout = TimeSpan.FromSeconds(40) };

        public async Task<JsonDocument> JsonAsync(HttpMethod method, string route, int status, string? token = null,
            object? body = null, string? epoch = null, Guid? key = null, string? fingerprint = null)
        {
            using var request = new HttpRequestMessage(method, route.TrimStart('/'));
            if (method != HttpMethod.Get)
                request.Headers.TryAddWithoutValidation("Origin", Required("MYDMSVN_WEB_ORIGIN"));
            if (route == "/api/v1/auth/login")
                request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            if (token != null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            if (epoch != null) request.Headers.TryAddWithoutValidation("X-MyDmsVn-Deployment-Epoch", epoch);
            if (key.HasValue) request.Headers.TryAddWithoutValidation("Idempotency-Key", key.Value.ToString("D"));
            if (fingerprint != null) request.Headers.TryAddWithoutValidation("X-MyDmsVn-Operation-Fingerprint", fingerprint);
            if (body != null)
            {
                var json = body is string raw ? raw : JsonSerializer.Serialize(body);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            else if (method != HttpMethod.Get)
                request.Content = new ByteArrayContent(Array.Empty<byte>());

            using var response = await _http.SendAsync(request);
            Assert.Equal(status, (int)response.StatusCode);
            Assert.True(response.Headers.TryGetValues("Cache-Control", out var values) &&
                values.Any(value => value.Contains("no-store", StringComparison.OrdinalIgnoreCase)));
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            var text = await response.Content.ReadAsStringAsync();
            return JsonDocument.Parse(text);
        }

        public void Dispose() => _http.Dispose();
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "docs", "contracts", "corpus-blobs.sha1")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Frozen contracts not found.");
    }

    private static JsonDocument Fixture(string family, string name) => JsonDocument.Parse(
        File.ReadAllBytes(Path.Combine(Root(), "docs", "contracts", family, name)));

    private static void Shape(string family, string name, JsonDocument actual)
    {
        using var gold = Fixture(family, name);
        Compare(gold.RootElement, actual.RootElement, name);
    }

    private static void Compare(JsonElement expected, JsonElement actual, string path)
    {
        // Golden fixtures describe fields and JSON primitive types. The live
        // database supplies its own ID/date/version/label values.
        if (expected.ValueKind == JsonValueKind.Object)
        {
            Assert.Equal(JsonValueKind.Object, actual.ValueKind);
            var keys = expected.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var actualKeys = actual.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Assert.Equal(keys, actualKeys);
            foreach (var p in expected.EnumerateObject())
                Compare(p.Value, actual.GetProperty(p.Name), path + "." + p.Name);
        }
        else if (expected.ValueKind == JsonValueKind.Array)
        {
            Assert.Equal(JsonValueKind.Array, actual.ValueKind);
            if (expected.GetArrayLength() > 0)
            {
                Assert.True(actual.GetArrayLength() > 0, "Missing live rows for " + path);
                var shape = expected[0];
                foreach (var item in actual.EnumerateArray()) Compare(shape, item, path + "[]");
            }
        }
        else if (expected.ValueKind == JsonValueKind.String)
        {
            Assert.Equal(JsonValueKind.String, actual.ValueKind);
            var s = actual.GetString()!;
            if (Regex.IsMatch(path, @"\.(saleId|saleLineId|receiptId|receiptLineId|ledgerId|documentId|totalCount)$",
                    RegexOptions.CultureInvariant))
                Assert.Matches(@"\A(0|[1-9][0-9]*)\z", s);
            if (path.EndsWith(".version", StringComparison.Ordinal))
            {
                Assert.Equal(12, s.Length);
                Assert.Equal(s, Convert.ToBase64String(Convert.FromBase64String(s)));
            }
            if (path.EndsWith(".postedAtUtc", StringComparison.Ordinal))
                Assert.Matches(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?Z\z", s);
        }
        else if (expected.ValueKind == JsonValueKind.Null)
            Assert.True(actual.ValueKind is JsonValueKind.Null or JsonValueKind.String,
                "Expected nullable text/timestamp for " + path);
        else if (expected.ValueKind is JsonValueKind.True or JsonValueKind.False)
            Assert.True(actual.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "Expected Boolean for " + path);
        else if (expected.ValueKind == JsonValueKind.Number)
        {
            Assert.Equal(JsonValueKind.Number, actual.ValueKind);
            Assert.True(actual.TryGetInt32(out _), "Expected exact int32 JSON token for " + path);
        }
        else throw new InvalidOperationException("Unexpected frozen fixture value.");
    }

    private static string S(JsonElement data, string property) => data.GetProperty(property).GetString()!;
    private static long Id(JsonElement data, string name) => long.Parse(S(data, name), CultureInfo.InvariantCulture);
    private static decimal Decimal(JsonElement data, string name) =>
        decimal.Parse(S(data, name), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    private static int IdFromEnv(string key) => int.Parse(Required(key), CultureInfo.InvariantCulture);

    private static async Task<JsonDocument> Login(Client client)
    {
        var body = new { username = Required("VPS04A_LIVE_USERNAME"), password = Required("VPS04A_LIVE_PASSWORD") };
        return await client.JsonAsync(HttpMethod.Post, "/api/v1/auth/login", 200, body: body);
    }

    private static async Task<(string Access, string Epoch)> Admit(Client client)
    {
        using var login = await Login(client);
        var token = S(login.RootElement.GetProperty("data"), "accessToken");
        using var context = await client.JsonAsync(HttpMethod.Get, "/api/v1/operations/context", 200, token);
        Shape("web4-v1", "context-response.json", context);
        return (token, S(context.RootElement.GetProperty("data"), "deploymentEpoch"));
    }

    private static async Task<JsonDocument> Write(Client client, string method, string route, object body,
        string token, string epoch, Guid? key = null) =>
        await client.JsonAsync(method == "PUT" ? HttpMethod.Put : HttpMethod.Post, route, 200, token, body, epoch, key ?? Guid.NewGuid());

    private static async Task<(int Count, decimal Quantity, decimal Value)> StockAsync(int warehouse, int product)
    {
        using var sql = new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await sql.OpenAsync();
        using var command = new SqlCommand(@"
SELECT COUNT(*), COALESCE(SUM(CONVERT(decimal(28,4),Quantity)),0),
  COALESCE(SUM(CONVERT(decimal(28,4),InventoryValue)),0)
FROM dbo.StockBalances WHERE WarehouseId=@wh AND ProductId=@product;", sql);
        command.Parameters.Add("@wh", SqlDbType.Int).Value = warehouse;
        command.Parameters.Add("@product", SqlDbType.Int).Value = product;
        using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetDecimal(1), reader.GetDecimal(2));
    }

    private static async Task<(int Rows, decimal Quantity, decimal Value)> MovementsAsync(int warehouse, int product)
    {
        using var sql = new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await sql.OpenAsync();
        using var command = new SqlCommand(@"
SELECT COUNT(*),COALESCE(SUM(CONVERT(decimal(28,4),l.QuantityChange)),0),
  COALESCE(SUM(CONVERT(decimal(28,4),v.ValueChange)),0)
FROM dbo.StockLedger l JOIN dbo.StockValuationEntries v ON v.LedgerId=l.LedgerId
WHERE l.WarehouseId=@wh AND l.ProductId=@product;", sql);
        command.Parameters.Add("@wh", SqlDbType.Int).Value = warehouse;
        command.Parameters.Add("@product", SqlDbType.Int).Value = product;
        using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetDecimal(1), reader.GetDecimal(2));
    }

    private static async Task<decimal> CostsAsync(long sale)
    {
        using var sql = new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await sql.OpenAsync();
        using var command = new SqlCommand(
            "SELECT COALESCE(SUM(CostAmount),0) FROM dbo.SalesReceiptLines WHERE SaleId=@sale;", sql);
        command.Parameters.Add("@sale", SqlDbType.BigInt).Value = sale;
        return Convert.ToDecimal(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<string> FingerprintAsync(Guid operation)
    {
        using var sql = new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await sql.OpenAsync();
        using var command = new SqlCommand(
            "SELECT RequestFingerprint FROM dbo.WebOperationJournal WHERE OperationId=@op AND Disposition='COMMITTED';", sql);
        command.Parameters.Add("@op", SqlDbType.UniqueIdentifier).Value = operation;
        var hash = await command.ExecuteScalarAsync();
        Assert.IsType<byte[]>(hash);
        return Convert.ToHexString((byte[])hash!).ToLowerInvariant();
    }

    [Fact]
    public async Task GoldenResponseShapesAndIndependentConcurrentStockCostOracle()
    {
        Guard();
        var wh = IdFromEnv("VPS04A_LIVE_WAREHOUSE_ID");
        var product = IdFromEnv("VPS04A_LIVE_PRODUCT_ID");
        var customer = IdFromEnv("VPS04A_LIVE_CUSTOMER_ID");
        var employee = IdFromEnv("VPS04A_LIVE_EMPLOYEE_ID");
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        Assert.Equal((0,0m,0m), await StockAsync(wh,product));
        using var client = new Client();
        var (token, epoch) = await Admit(client);

        // WEB2: authorization/capabilities and lookup contracts.
        using (var capabilities = await client.JsonAsync(HttpMethod.Get, "/api/v1/auth/capabilities?feature=sales", 200, token))
        {
            Shape("web2-v1","auth-capabilities-response.json",capabilities);
            Assert.True(capabilities.RootElement.GetProperty("data").GetProperty("post").GetBoolean());
        }
        using (var products = await client.JsonAsync(HttpMethod.Get, "/api/v1/catalog/products/lookup?limit=25", 200, token))
        {
            Shape("web2-v1","catalog-products-response.json",products);
            Assert.Contains(products.RootElement.GetProperty("data").EnumerateArray(),
                row => row.GetProperty("id").GetInt32() == product);
        }

        // WEB3/WEB4: goods receipt Create/Update/Post/Get/List and stock projections.
        var receiptCreate = new { receiptDate = today, warehouseId = wh, employeeId = employee,
            note = (string?)null, lines = new[]{ new { productId = product, quantity = "10.0000", unitCost = "10.0000" }}};
        using var receipt = await Write(client, "POST","/api/v1/goods-receipts",receiptCreate,token,epoch);
        Shape("web3-v1","receipt-create-response.json",receipt);
        var receiptData = receipt.RootElement.GetProperty("data");
        var receiptId = Id(receiptData,"receiptId");
        var receiptVersion = S(receiptData, "version");

        using var updatedReceipt = await Write(client, "PUT",
            "/api/v1/goods-receipts/" + receiptId + "/draft",
            new {receiptDate = today, warehouseId = wh, employeeId = employee, note = "CI receipt",
                lines = receiptCreate.lines, expectedVersion = receiptVersion}, token, epoch);
        Shape("web3-v1","receipt-update-response.json",updatedReceipt);
        var updatedVersion = S(updatedReceipt.RootElement.GetProperty("data"),"version");
        var receiptPostKey = Guid.NewGuid();
        using var postedReceipt = await Write(client, "POST",
            "/api/v1/goods-receipts/" + receiptId + "/post",
            new { expectedVersion = updatedVersion },token,epoch,receiptPostKey);
        Shape("web3-v1","receipt-post-response.json",postedReceipt);
        Assert.Equal("Posted", S(postedReceipt.RootElement.GetProperty("data"),"status"));

        using (var readReceipt = await client.JsonAsync(HttpMethod.Get, "/api/v1/goods-receipts/" + receiptId,200,token))
            Shape("web3-v1","receipt-get-posted-response.json",readReceipt);

        using (var page = await client.JsonAsync(HttpMethod.Get, "/api/v1/goods-receipts?pageNumber=1&pageSize=25",200,token))
            Shape("web3-v1","receipt-list-response.json",page);

        using (var status = await client.JsonAsync(HttpMethod.Get, "/api/v1/operations/" + receiptPostKey.ToString("D"),
            200,token,epoch:epoch,fingerprint:await FingerprintAsync(receiptPostKey)))
        {
            Shape("web4-v1","receipt-post-status.json",status);
            Assert.Equal("COMMITTED",S(status.RootElement.GetProperty("data"),"state"));
            Assert.Equal(receiptId, Id(status.RootElement.GetProperty("data").GetProperty("outcome").GetProperty("data"),"receiptId"));
        }
        Assert.Equal((1,10m,100m),await StockAsync(wh,product));
        Assert.Equal((1,10m,100m),await MovementsAsync(wh,product));

        // Sale A has duplicate product lines and a gift. Sale B is a second
        // independent client/operation competing for the same stock row.
        var saleA = new { saleDate=today, customerId=customer, warehouseId=wh, employeeId=employee, note="CI Sale A",
            lines=new[]{
                new {saleLineId="0",productId=product,qty="2.0000",unitPrice="30.0000",isGift=false},
                new {saleLineId="0",productId=product,qty="1.0000",unitPrice="70.0000",isGift=true}
            }};
        var saleB = new { saleDate=today, customerId=customer, warehouseId=wh, employeeId=employee, note="CI Sale B",
            lines=new[]{new {saleLineId="0",productId=product,qty="2.0000",unitPrice="25.0000",isGift=false}}};
        using var draftA = await Write(client,"POST","/api/v1/sales-receipts",saleA,token,epoch);
        Shape("web2-v1","sales-create-response.json",draftA);
        var idA = Id(draftA.RootElement.GetProperty("data"),"saleId");
        var versionA = S(draftA.RootElement.GetProperty("data"),"version");
        using var draftB = await Write(client,"POST","/api/v1/sales-receipts",saleB,token,epoch);
        Shape("web2-v1","sales-create-response.json",draftB);
        var idB = Id(draftB.RootElement.GetProperty("data"),"saleId");
        var versionB = S(draftB.RootElement.GetProperty("data"),"version");

        using var competingClient = new Client();
        var (competingToken, competingEpoch) = await Admit(competingClient);
        Assert.Equal(epoch,competingEpoch);
        // Both posts must preserve the shared valuation gate and produce
        // one movement per line, not oversell or double-charge gifts.
        var first = Write(client,"POST","/api/v1/sales-receipts/" + idA + "/post",
            new {expectedVersion=versionA},token,epoch);
        var second = Write(competingClient,"POST","/api/v1/sales-receipts/" + idB + "/post",
            new {expectedVersion=versionB},competingToken,epoch);
        var both = await Task.WhenAll(first,second);
        using (both[0]) Shape("web4-v1","sales-post-response.json",both[0]);
        using (both[1]) Shape("web4-v1","sales-post-response.json",both[1]);

        using (var postedA = await client.JsonAsync(HttpMethod.Get, "/api/v1/sales-receipts/" + idA,200,token))
        {
            Shape("web2-v1","sales-get-posted-response.json",postedA);
            var data = postedA.RootElement.GetProperty("data");
            Assert.Equal(60m, Decimal(data,"totalAmount"));
            Assert.Equal(30m, Decimal(data,"totalCost"));
            Assert.Equal(30m, Decimal(data,"grossProfit"));
            Assert.Equal(2, data.GetProperty("lines").GetArrayLength());
            Assert.Equal(0m, Decimal(data.GetProperty("lines")[1],"lineAmount"));
            Assert.Equal(10m, Decimal(data.GetProperty("lines")[1],"costAmount"));
        }
        using (var postedB = await client.JsonAsync(HttpMethod.Get, "/api/v1/sales-receipts/" + idB,200,token))
        {
            Shape("web2-v1","sales-get-posted-response.json",postedB);
            var data = postedB.RootElement.GetProperty("data");
            Assert.Equal(50m, Decimal(data,"totalAmount"));
            Assert.Equal(20m, Decimal(data,"totalCost"));
            Assert.Equal(30m, Decimal(data,"grossProfit"));
        }

        Assert.Equal(30m, await CostsAsync(idA));
        Assert.Equal(20m, await CostsAsync(idB));
        Assert.Equal((1,5m,50m),await StockAsync(wh,product));
        Assert.Equal((4,5m,50m),await MovementsAsync(wh,product));

        using (var salesPage = await client.JsonAsync(HttpMethod.Get, "/api/v1/sales-receipts?pageSize=25",200,token))
        {
            var rows=salesPage.RootElement.GetProperty("data").GetProperty("items");
            Assert.True(rows.GetArrayLength()>=2);
            // A page can omit optional nextCursor/previousCursor; its item
            // schema comes directly from the frozen WEB2 response corpus.
            using var fixture = Fixture("web2-v1","sales-list-response.json");
            foreach(var row in rows.EnumerateArray())
                Compare(fixture.RootElement.GetProperty("data").GetProperty("items")[0],row,"sales-list.item");
        }
        using (var balances = await client.JsonAsync(HttpMethod.Get,
            $"/api/v1/inventory/balances?pageNumber=1&pageSize=25&warehouseId={wh}&productId={product}",200,token))
        {
            Shape("web3-v1","balances-response.json",balances);
            var row=balances.RootElement.GetProperty("data").GetProperty("items")[0];
            Assert.Equal(5m,Decimal(row,"quantity"));
            Assert.Equal(50m,Decimal(row,"inventoryValue"));
            Assert.Equal(10m,Decimal(row,"averageUnitCost"));
        }
        using (var card = await client.JsonAsync(HttpMethod.Get,
            $"/api/v1/inventory/stock-card?pageNumber=1&pageSize=25&warehouseId={wh}&productId={product}",200,token))
        {
            Shape("web3-v1","stock-card-response.json",card);
            var rows=card.RootElement.GetProperty("data").GetProperty("items");
            Assert.Equal(4,rows.GetArrayLength());
            Assert.Equal(5m, rows.EnumerateArray().Sum(row=>Decimal(row,"quantityChange")));
            Assert.Equal(50m,rows.EnumerateArray().Sum(row=>Decimal(row,"valueChange")));
        }
    }

    [Fact]
    public async Task StrictJsonRejectionsAreNoWriteWithGoldenProtocolErrors()
    {
        Guard();
        var wh=IdFromEnv("VPS04A_LIVE_WAREHOUSE_ID");
        var employee=IdFromEnv("VPS04A_LIVE_EMPLOYEE_ID");
        var product=IdFromEnv("VPS04A_LIVE_PRODUCT_ID");
        var date=DateTime.UtcNow.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        using var client=new Client();
        var (access,epoch)=await Admit(client);
        var invalidBodies=new[]{
            // Decimal wire tokens cannot be JSON numbers.
            JsonSerializer.Serialize(new {receiptDate=date,warehouseId=wh,employeeId=employee,
                lines=new[]{new {productId=product,quantity=1.2,unitCost="10.0000"}}}),
            // Unknown fields must fail closed.
            JsonSerializer.Serialize(new {receiptDate=date,warehouseId=wh,employeeId=employee,
                lines=new[]{new {productId=product,quantity="1.0000",unitCost="10.0000"}},futureField=42}),
            // Duplicate decoded member names must fail closed.
            "{\"receiptDate\":\""+date+"\",\"warehouseId\":"+wh+",\"employeeId\":"+employee+
            ",\"lines\":[{\"productId\":"+product+",\"quantity\":\"1.0000\",\"quantity\":\"2.0000\",\"unitCost\":\"10.0000\"}]}",
            // Invalid quantity precision must fail.
            JsonSerializer.Serialize(new {receiptDate=date,warehouseId=wh,employeeId=employee,
                lines=new[]{new {productId=product,quantity="1.00000",unitCost="10.0000"}}})
        };
        foreach(var body in invalidBodies)
        {
            var key=Guid.NewGuid();
            using var response=await client.JsonAsync(HttpMethod.Post,
                "/api/v1/goods-receipts",400,access,body,epoch,key);
            var error=response.RootElement.GetProperty("error");
            Assert.Equal("ValidationError",S(error,"code"));
            Assert.True(error.GetProperty("details").ValueKind==JsonValueKind.Array);
            using var sql=new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
            await sql.OpenAsync();
            using var cmd=new SqlCommand("SELECT COUNT(*) FROM dbo.WebOperationJournal WHERE OperationId=@id;",sql);
            cmd.Parameters.Add("@id",SqlDbType.UniqueIdentifier).Value=key;
            Assert.Equal(0,Convert.ToInt32(await cmd.ExecuteScalarAsync(),CultureInfo.InvariantCulture));
        }
        using var stale=await client.JsonAsync(HttpMethod.Post,
            "/api/v1/goods-receipts",409,access,
            JsonSerializer.Serialize(new {receiptDate=date,warehouseId=wh,employeeId=employee,
                lines=new[]{new {productId=product,quantity="1.0000",unitCost="10.0000"}}}),
            Guid.NewGuid().ToString("D"),Guid.NewGuid());
        Shape("web4-v1","error-deployment-epochmismatch.json",stale);
        Assert.Equal("Deployment.EpochMismatch",
            S(stale.RootElement.GetProperty("error"),"code"));
    }
}
#endif
