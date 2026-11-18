// VPS-06B independent post-phase SQL oracles against disposable HTTPS->SQL13 only.
// Runs after existing rollback/retry and oversell phases, before WEB6 restart.
// NEVER run on protected staging or an operator/production database.
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

public sealed class Vps06bSqlGeneratedMediatorParityTests
{
    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value : throw new InvalidOperationException("Missing disposable VPS-06B SQL admission.");

    private static void Guard()
    {
        Assert.Equal("approved-container-only", Required("VPS04A_CI_EPHEMERAL_SQL"));
        Assert.Equal("approved-ci-only", Required("VPS04A_CI_FAULT_APPROVED"));
        Assert.Equal("approved-nonproduction-only", Required("VPS04A_LIVE_ALLOW_MUTATION"));
        Assert.Equal("https://localhost:50443", Required("MYDMSVN_WEB_ORIGIN"));
        Assert.Equal("web5", Required("MYDMSVN_WEB_OPERATION_MODE"));
    }

    private static async Task<List<(long SaleId, string Status)>> SalesByNote(string predicate, string note)
    {
        await using var connection = new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT SaleId, Status FROM dbo.SalesReceipts WHERE Note " + predicate + " @note ORDER BY SaleId;", connection);
        command.Parameters.Add("@note", SqlDbType.NVarChar, 255).Value = note;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(long SaleId, string Status)>();
        while (await reader.ReadAsync())
            rows.Add((reader.GetInt64(0), reader.GetString(1)));
        return rows;
    }

    private static async Task<int> CountAsync(string sql, long? saleId = null)
    {
        await using var connection = new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        if (saleId.HasValue)
            command.Parameters.Add("@sale", SqlDbType.BigInt).Value = saleId.Value;
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static Task<int> LedgerCount(long id) =>
        CountAsync("SELECT COUNT(*) FROM dbo.StockLedger WHERE DocumentType='SalesReceipt' AND DocumentId=@sale;", id);

    private static Task<int> ValuationCount(long id) =>
        CountAsync("SELECT COUNT(*) FROM dbo.StockValuationEntries v JOIN dbo.StockLedger l ON v.LedgerId=l.LedgerId WHERE l.DocumentType='SalesReceipt' AND l.DocumentId=@sale;", id);

    [Fact]
    public async Task NativeMediatorOversellRaceHasOneCommittedLedgerAndZeroDraftLedger()
    {
        Guard();
        var rows = await SalesByNote("LIKE", "VPS04A-CI-OVERSELL-%");
        Assert.Equal(2, rows.Count);
        var posted = Assert.Single(rows, sale => sale.Status == "Posted");
        var denied = Assert.Single(rows, sale => sale.Status == "Draft");
        Assert.NotEqual(posted.SaleId, denied.SaleId);
        Assert.Equal(1, await LedgerCount(posted.SaleId));
        Assert.Equal(1, await ValuationCount(posted.SaleId));
        Assert.Equal(0, await LedgerCount(denied.SaleId));
        Assert.Equal(0, await ValuationCount(denied.SaleId));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.SalesReceipts WHERE SaleId=@sale AND PostedAtUtc IS NULL AND PostedByUserId IS NULL;", denied.SaleId));
    }

    [Fact]
    public async Task NativeMediatorRolledBackThenReplayedSameKeyExactlyOnce()
    {
        Guard();
        var rows = await SalesByNote("=", "CI-FAULT-CHECK");
        var sale = Assert.Single(rows);
        Assert.Equal("Posted", sale.Status);
        Assert.Equal(1, await LedgerCount(sale.SaleId));
        Assert.Equal(1, await ValuationCount(sale.SaleId));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.SalesReceipts WHERE SaleId=@sale AND PostedAtUtc IS NOT NULL AND PostedByUserId IS NOT NULL;", sale.SaleId));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.SalesReceiptLines WHERE SaleId=@sale AND PostedUnitCost IS NOT NULL AND CostAmount IS NOT NULL;", sale.SaleId));
    }

    [Fact]
    public async Task NativeMediatorConcurrentAuthenticatedAndAnonymousRequestsNeverCrossScopes()
    {
        Guard();
        using var api = new HttpClient(new HttpClientHandler { UseCookies = false })
        {
            BaseAddress = new Uri(Required("MYDMSVN_WEB_ORIGIN") + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login");
        loginRequest.Headers.TryAddWithoutValidation("Origin", Required("MYDMSVN_WEB_ORIGIN"));
        loginRequest.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        loginRequest.Content = new StringContent(JsonSerializer.Serialize(new
        {
            username = Required("VPS04A_LIVE_USERNAME"),
            password = Required("VPS04A_LIVE_PASSWORD")
        }), Encoding.UTF8, "application/json");
        using var loginReply = await api.SendAsync(loginRequest);
        Assert.Equal(200, (int)loginReply.StatusCode);
        using var login = JsonDocument.Parse(await loginReply.Content.ReadAsStringAsync());
        var bearer = login.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
        var expectedActor = login.RootElement.GetProperty("data").GetProperty("user").GetProperty("userId").GetInt32();
        var priorJournal = await CountAsync("SELECT COUNT(*) FROM dbo.WebOperationJournal;");
        var priorSales = await CountAsync("SELECT COUNT(*) FROM dbo.SalesReceipts;");

        async Task<(int Status, JsonDocument Body)> MeAsync(bool authorized)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
            if (authorized)
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
            using var response = await api.SendAsync(request);
            return ((int)response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()));
        }

        // Multiple concurrent Kestrel scopes; every authorized request must
        // preserve its own actor while anonymous dispatch must stay denied.
        var authorized = Enumerable.Range(0, 6).Select(_ => MeAsync(true)).ToArray();
        var anonymous = Enumerable.Range(0, 6).Select(_ => MeAsync(false)).ToArray();
        await Task.WhenAll(authorized.Concat(anonymous));
        foreach (var task in authorized)
        {
            var (status, json) = task.Result;
            using (json)
            {
                Assert.Equal(200, status);
                Assert.Equal(expectedActor, json.RootElement.GetProperty("data").GetProperty("userId").GetInt32());
            }
        }
        foreach (var task in anonymous)
        {
            var (status, json) = task.Result;
            using (json)
            {
                Assert.Equal(401, status);
                Assert.Equal("Auth.Unauthorized",
                    json.RootElement.GetProperty("error").GetProperty("code").GetString());
            }
        }
        Assert.Equal(priorJournal, await CountAsync("SELECT COUNT(*) FROM dbo.WebOperationJournal;"));
        Assert.Equal(priorSales, await CountAsync("SELECT COUNT(*) FROM dbo.SalesReceipts;"));
    }
}
#endif
