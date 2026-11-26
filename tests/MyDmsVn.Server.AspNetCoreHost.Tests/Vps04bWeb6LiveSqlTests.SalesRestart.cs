// VPS-10.4B native Sales recovery tests; HTTP Gateway.Send is not MediatR dispatch.
#if VPS04A_LIVE
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed partial class Vps04bWeb6LiveSqlTests
{
    // The workflow invokes these two facts in separate test processes around
    // a real Kestrel worker stop/start. The state file is private temporary
    // fixture data only: no access token, refresh cookie, password or SQL secret.
    private sealed record RestartIntent(int Actor, Guid Key, string Epoch, string Body,
        string Note, long SaleId, string ResultHash, string Fingerprint);

    private static int FixtureId(string name) =>
        int.Parse(Required(name), CultureInfo.InvariantCulture);

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

    private static Dictionary<string, string> WriteHeaders(Guid key, string epoch) => new()
    {
        ["Idempotency-Key"] = key.ToString("D"),
        ["X-MyDmsVn-Deployment-Epoch"] = epoch
    };

    private static async Task<(long SaleId, byte[] Result, string Fingerprint)> ReadCommittedSale(
        int actor, Guid key)
    {
        await using var connection = new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await connection.OpenAsync();
        await using var command = new SqlCommand(@"
SELECT ResultDocumentId, ResultBytes, RequestFingerprint
FROM dbo.WebOperationJournal
WHERE ActorUserId=@actor AND OperationId=@key AND OperationKind=1 AND Disposition='COMMITTED';", connection);
        command.Parameters.Add("@actor", SqlDbType.Int).Value = actor;
        command.Parameters.Add("@key", SqlDbType.UniqueIdentifier).Value = key;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Committed Sales.Create journal missing.");
        var result = (reader.GetInt64(0), (byte[])reader.GetValue(1),
            Convert.ToHexString((byte[])reader.GetValue(2)));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static async Task AssertOneSale(string note, long saleId)
    {
        await using var connection = new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT SaleId FROM dbo.SalesReceipts WHERE Note=@note;", connection);
        command.Parameters.Add("@note", SqlDbType.NVarChar, 200).Value = note;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(saleId, reader.GetInt64(0));
        Assert.False(await reader.ReadAsync(), "Same intent created more than one receipt.");
    }

    [Fact]
    public async Task NativeSalesCommitBeforeHostRestart()
    {
        Guard();
        using var api = new Gateway();
        var session = await api.Login();
        using var context = await api.Send(HttpMethod.Get, "/api/v1/operations/context", bearer: session.Bearer);
        using var contextJson = await Json(context, 200);
        var epoch = contextJson.RootElement.GetProperty("data").GetProperty("deploymentEpoch").GetString()!;
        var key = Guid.NewGuid();
        var note = "VPS104B-RESTART-" + key.ToString("N");
        var body = JsonSerializer.Serialize(new
        {
            saleDate = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            customerId = FixtureId("VPS04A_LIVE_CUSTOMER_ID"),
            warehouseId = FixtureId("VPS04A_LIVE_WAREHOUSE_ID"),
            employeeId = FixtureId("VPS04A_LIVE_EMPLOYEE_ID"),
            note,
            lines = new[] { new { saleLineId = "0", productId = FixtureId("VPS04A_LIVE_PRODUCT_ID"),
                qty = "1.0000", unitPrice = "2.0000", isGift = false } }
        });
        // Simulate a client that never consumes the successful result, rather
        // than claim a transport-level dropped ACK. SQL is the independent oracle.
        using (var response = await api.Send(HttpMethod.Post, "/api/v1/sales-receipts",
            bearer: session.Bearer, json: body, headers: WriteHeaders(key, epoch)))
            Assert.Equal(200, (int)response.StatusCode);

        var row = await ReadCommittedSale(session.UserId, key);
        await AssertOneSale(note, row.SaleId);
        var intent = new RestartIntent(session.UserId, key, epoch, body, note, row.SaleId,
            Hash(row.Result), row.Fingerprint);
        using var state = new FileStream(Required("VPS104B_RESTART_STATE"), new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        });
        await JsonSerializer.SerializeAsync(state, intent);
    }

    [Fact]
    public async Task NativeSalesReplaysAfterHostRestart()
    {
        Guard();
        var path = Required("VPS104B_RESTART_STATE");
        try
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            var intent = JsonSerializer.Deserialize<RestartIntent>(File.ReadAllText(path))!;
            using var api = new Gateway();
            // Re-admit the same actor with a fresh session; no credential is
            // transferred through the fixture state file.
            var session = await api.Login();
            Assert.Equal(intent.Actor, session.UserId);
            var before = await ReadCommittedSale(intent.Actor, intent.Key);
            Assert.Equal(intent.SaleId, before.SaleId);
            Assert.Equal(intent.ResultHash, Hash(before.Result));
            Assert.Equal(intent.Fingerprint, before.Fingerprint);
            using var response = await api.Send(HttpMethod.Post, "/api/v1/sales-receipts",
                bearer: session.Bearer, json: intent.Body, headers: WriteHeaders(intent.Key, intent.Epoch));
            Assert.Equal(200, (int)response.StatusCode);
            Assert.Equal(before.Result, await response.Content.ReadAsByteArrayAsync());
            var after = await ReadCommittedSale(intent.Actor, intent.Key);
            Assert.Equal(intent.SaleId, after.SaleId);
            Assert.Equal(intent.ResultHash, Hash(after.Result));
            Assert.Equal(intent.Fingerprint, after.Fingerprint);
            await AssertOneSale(intent.Note, intent.SaleId);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
#endif
