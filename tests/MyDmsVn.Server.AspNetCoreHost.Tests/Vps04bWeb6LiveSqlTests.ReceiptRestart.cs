// VPS-10.5B: disposable HTTPS/SQL13 only; no credentials in restart intent.
#if VPS04A_LIVE
using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed partial class Vps04bWeb6LiveSqlTests
{
    private const string ReceiptRoute = "/api/v1/goods-receipts";
    private sealed record ReceiptIntent(int Actor, Guid Key, string Epoch, long ReceiptId,
        string Version, int Warehouse, int Product, decimal BeforeQty, decimal BeforeValue,
        string? ResultHash = null, string? Fingerprint = null);

    private static string ReceiptStatePath() => Required("VPS105B_RESTART_STATE");
    private static string ReceiptIdText(long id) => id.ToString(CultureInfo.InvariantCulture);
    private static string ReceiptPostBody(ReceiptIntent intent) =>
        JsonSerializer.Serialize(new { expectedVersion = intent.Version });

    private static void SaveReceiptIntent(ReceiptIntent intent, FileMode mode)
    {
        using var state = new FileStream(ReceiptStatePath(), new FileStreamOptions
        {
            Mode = mode, Access = FileAccess.Write, Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        });
        JsonSerializer.Serialize(state, intent);
    }

    private static ReceiptIntent LoadReceiptIntent()
    {
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(ReceiptStatePath()));
        return JsonSerializer.Deserialize<ReceiptIntent>(File.ReadAllText(ReceiptStatePath()))!;
    }

    private static async Task<(decimal Qty, decimal Value)> ReceiptStock(int warehouse, int product)
    {
        await using var connection = new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await connection.OpenAsync();
        await using var cmd = new SqlCommand(@"
SELECT COALESCE(SUM(CONVERT(decimal(28,4),Quantity)),0),
       COALESCE(SUM(CONVERT(decimal(28,4),InventoryValue)),0)
FROM dbo.StockBalances WHERE WarehouseId=@wh AND ProductId=@product;", connection);
        cmd.Parameters.Add("@wh", SqlDbType.Int).Value = warehouse;
        cmd.Parameters.Add("@product", SqlDbType.Int).Value = product;
        await using var row = await cmd.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        return (row.GetDecimal(0), row.GetDecimal(1));
    }

    private static async Task<int> ReceiptCount(string query, string parameter, object value)
    {
        await using var connection = new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await connection.OpenAsync();
        await using var cmd = new SqlCommand(query, connection);
        cmd.Parameters.AddWithValue(parameter, value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<(byte[] Result, string Fingerprint)> ReceiptCommitted(ReceiptIntent intent)
    {
        await using var connection = new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await connection.OpenAsync();
        await using var cmd = new SqlCommand(@"
SELECT ResultDocumentId, ResultBytes, RequestFingerprint
FROM dbo.WebOperationJournal
WHERE ActorUserId=@actor AND OperationId=@key AND Disposition='COMMITTED';", connection);
        cmd.Parameters.Add("@actor", SqlDbType.Int).Value = intent.Actor;
        cmd.Parameters.Add("@key", SqlDbType.UniqueIdentifier).Value = intent.Key;
        await using var row = await cmd.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync(), "Committed receipt post journal is missing.");
        Assert.Equal(intent.ReceiptId, row.GetInt64(0));
        var result = ((byte[])row.GetValue(1), Convert.ToHexString((byte[])row.GetValue(2)));
        Assert.False(await row.ReadAsync());
        return result;
    }

    private static async Task AssertReceiptPostedOnce(ReceiptIntent intent)
    {
        Assert.Equal((intent.BeforeQty + 1.25m, intent.BeforeValue + 3m),
            await ReceiptStock(intent.Warehouse, intent.Product));
        Assert.Equal(1, await ReceiptCount(
            "SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE ReceiptId=@id AND Status='Posted' AND PostedAtUtc IS NOT NULL;", "@id", intent.ReceiptId));
        Assert.Equal(1, await ReceiptCount(
            "SELECT COUNT(*) FROM dbo.StockLedger WHERE DocumentType='GoodsReceipt' AND DocumentId=@id;", "@id", intent.ReceiptId));
        Assert.Equal(1, await ReceiptCount(@"
SELECT COUNT(*) FROM dbo.StockLedger l JOIN dbo.StockValuationEntries v ON v.LedgerId=l.LedgerId
WHERE l.DocumentType='GoodsReceipt' AND l.DocumentId=@id AND l.QuantityChange=1.2500 AND v.ValueChange=3.0000;", "@id", intent.ReceiptId));
        Assert.Equal(1, await ReceiptCount(
            "SELECT COUNT(*) FROM dbo.WebOperationJournal WHERE OperationId=@key;", "@key", intent.Key));
    }

    [Fact]
    public async Task NativeReceiptUpdateRaceAndInjectedPostRollback()
    {
        Guard();
        Assert.Equal("approved-ci-only", Required("VPS04A_CI_FAULT_APPROVED"));
        var wh = FixtureId("VPS04A_LIVE_WAREHOUSE_ID");
        var product = FixtureId("VPS04A_LIVE_PRODUCT_ID");
        var employee = FixtureId("VPS04A_LIVE_EMPLOYEE_ID");
        var before = await ReceiptStock(wh, product);
        using var api = new Gateway();
        using var other = new Gateway();
        var session = await api.Login();
        using var context = await api.Send(HttpMethod.Get, "/api/v1/operations/context", bearer: session.Bearer);
        using var contextJson = await Json(context, 200);
        var epoch = contextJson.RootElement.GetProperty("data").GetProperty("deploymentEpoch").GetString()!;
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var lines = new[] { new { productId = product, quantity = "1.2500", unitCost = "2.4000" } };
        var createBody = JsonSerializer.Serialize(new { receiptDate = date, warehouseId = wh, employeeId = employee,
            note = "VPS105B-" + Guid.NewGuid().ToString("N"), lines });
        using var created = await api.Send(HttpMethod.Post, ReceiptRoute, bearer: session.Bearer,
            json: createBody, headers: WriteHeaders(Guid.NewGuid(), epoch));
        using var createdJson = await Json(created, 200);
        var data = createdJson.RootElement.GetProperty("data");
        var id = long.Parse(data.GetProperty("receiptId").GetString()!, CultureInfo.InvariantCulture);
        var version = data.GetProperty("version").GetString()!;
        string UpdateBody(string note) => JsonSerializer.Serialize(new { receiptDate = date, warehouseId = wh,
            employeeId = employee, note, lines, expectedVersion = version });
        var path = ReceiptRoute + "/" + ReceiptIdText(id);
        // Distinct client connections and operation keys, same original rowversion.
        var firstTask = api.Send(HttpMethod.Put, path + "/draft", bearer: session.Bearer,
            json: UpdateBody("VPS105B-race-A"), headers: WriteHeaders(Guid.NewGuid(), epoch));
        var secondTask = other.Send(HttpMethod.Put, path + "/draft", bearer: session.Bearer,
            json: UpdateBody("VPS105B-race-B"), headers: WriteHeaders(Guid.NewGuid(), epoch));
        var replies = await Task.WhenAll(firstTask, secondTask);
        using var first = replies[0];
        using var second = replies[1];
        Assert.Equal(new[] { 200, 409 }, replies.Select(x => (int)x.StatusCode).OrderBy(x => x).ToArray());
        using var winning = await Json(replies.Single(x => (int)x.StatusCode == 200), 200);
        using var stale = await Json(replies.Single(x => (int)x.StatusCode == 409), 409);
        Assert.False(stale.RootElement.TryGetProperty("data", out _));
        var newVersion = winning.RootElement.GetProperty("data").GetProperty("version").GetString()!;
        Assert.NotEqual(version, newVersion);
        var intent = new ReceiptIntent(session.UserId, Guid.NewGuid(), epoch, id, newVersion, wh, product, before.Qty, before.Value);

        // Trigger throws AFTER the ledger INSERT. No physical movement,
        // stock/cost change, posted header or committed journal may survive.
        using var failed = await api.Send(HttpMethod.Post, path + "/post", bearer: session.Bearer,
            json: ReceiptPostBody(intent), headers: WriteHeaders(intent.Key, epoch));
        using var error = await Json(failed, 500);
        Assert.Equal("InternalError", error.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(before, await ReceiptStock(wh, product));
        Assert.Equal(0, await ReceiptCount(
            "SELECT COUNT(*) FROM dbo.WebOperationJournal WHERE OperationId=@key;", "@key", intent.Key));
        Assert.Equal(0, await ReceiptCount(
            "SELECT COUNT(*) FROM dbo.StockLedger WHERE DocumentType='GoodsReceipt' AND DocumentId=@id;", "@id", id));
        Assert.Equal(0, await ReceiptCount(@"
SELECT COUNT(*) FROM dbo.StockLedger l JOIN dbo.StockValuationEntries v ON v.LedgerId=l.LedgerId
WHERE l.DocumentType='GoodsReceipt' AND l.DocumentId=@id;", "@id", id));
        Assert.Equal(1, await ReceiptCount(
            "SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE ReceiptId=@id AND Status='Draft' AND PostedAtUtc IS NULL;", "@id", id));
        using var reread = await api.Send(HttpMethod.Get, path, bearer: session.Bearer);
        using var rereadJson = await Json(reread, 200);
        Assert.Equal(newVersion, rereadJson.RootElement.GetProperty("data").GetProperty("version").GetString());
        SaveReceiptIntent(intent, FileMode.CreateNew);
    }

    [Fact]
    public async Task NativeReceiptDisarmedSameKeyCommitsBeforeRestart()
    {
        Guard();
        var intent = LoadReceiptIntent();
        Assert.Null(intent.ResultHash);
        using var api = new Gateway();
        var session = await api.Login();
        Assert.Equal(intent.Actor, session.UserId);
        var path = ReceiptRoute + "/" + ReceiptIdText(intent.ReceiptId) + "/post";
        // Model a client that does not consume the committed HTTP response.
        // This is not a claim of a physical network packet drop.
        using (var reply = await api.Send(HttpMethod.Post, path, bearer: session.Bearer,
            json: ReceiptPostBody(intent), headers: WriteHeaders(intent.Key, intent.Epoch)))
            Assert.Equal(200, (int)reply.StatusCode);
        var row = await ReceiptCommitted(intent);
        await AssertReceiptPostedOnce(intent);
        using var json = JsonDocument.Parse(row.Result);
        Assert.Equal("Posted", json.RootElement.GetProperty("data").GetProperty("status").GetString());
        SaveReceiptIntent(intent with { ResultHash = Hash(row.Result), Fingerprint = row.Fingerprint }, FileMode.Truncate);
    }

    [Fact]
    public async Task NativeReceiptPostReplaysAfterRestartAndPreservesInventoryCost()
    {
        Guard();
        var intent = LoadReceiptIntent();
        try
        {
            Assert.NotNull(intent.ResultHash);
            using var api = new Gateway();
            var session = await api.Login();
            Assert.Equal(intent.Actor, session.UserId);
            var before = await ReceiptCommitted(intent);
            Assert.Equal(intent.ResultHash, Hash(before.Result));
            Assert.Equal(intent.Fingerprint, before.Fingerprint);
            var path = ReceiptRoute + "/" + ReceiptIdText(intent.ReceiptId) + "/post";
            using var replay = await api.Send(HttpMethod.Post, path, bearer: session.Bearer,
                json: ReceiptPostBody(intent), headers: WriteHeaders(intent.Key, intent.Epoch));
            using var response = await Json(replay, 200);
            Assert.Equal(before.Result, await replay.Content.ReadAsByteArrayAsync());
            var after = await ReceiptCommitted(intent);
            Assert.Equal(before.Result, after.Result);
            Assert.Equal(before.Fingerprint, after.Fingerprint);
            await AssertReceiptPostedOnce(intent);
            var stock = await ReceiptStock(intent.Warehouse, intent.Product);
            using var balance = await api.Send(HttpMethod.Get,
                "/api/v1/inventory/balances?pageNumber=1&pageSize=25&warehouseId=" +
                intent.Warehouse.ToString(CultureInfo.InvariantCulture) + "&productId=" +
                intent.Product.ToString(CultureInfo.InvariantCulture), bearer: session.Bearer);
            using var balanceJson = await Json(balance, 200);
            var item = Assert.Single(balanceJson.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
            decimal Number(string name) => decimal.Parse(item.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);
            Assert.Equal(stock.Qty, Number("quantity"));
            Assert.Equal(stock.Value, Number("inventoryValue"));
            Assert.Equal(decimal.Round(stock.Value / stock.Qty, 8, MidpointRounding.AwayFromZero), Number("averageUnitCost"));
        }
        finally { File.Delete(ReceiptStatePath()); }
    }
}
#endif
