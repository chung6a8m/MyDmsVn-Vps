// Compiled only for explicitly approved, destructive nonproduction VPS-04A acceptance.
// No credentials, SQL connection, trust-all TLS callback or fixture IDs are baked into tests.
#if VPS04A_LIVE
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps04aLiveSqlAcceptanceTests
{
    private static string Required(string key) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } value
            ? value : throw new InvalidOperationException("Missing required VPS04A live environment setting: " + key);

    private static void GuardNonProduction()
    {
        if (Required("VPS04A_LIVE_ALLOW_MUTATION") != "approved-nonproduction-only")
            throw new InvalidOperationException("The VPS-04A live mutation scope was not explicitly admitted.");
        if (Required("MYDMSVN_WEB_OPERATION_MODE") != "web5")
            throw new InvalidOperationException("VPS-04A live mutation requires exact web5 schema13.");
        if (!Required("MYDMSVN_WEB_ORIGIN").StartsWith("https://", StringComparison.Ordinal))
            throw new InvalidOperationException("A verified HTTPS origin is required.");
    }

    private sealed class Gateway : IDisposable
    {
        private readonly HttpClient _client = new(new HttpClientHandler { UseCookies = false })
        {
            BaseAddress = new Uri(Required("MYDMSVN_WEB_ORIGIN") + "/"),
            Timeout = TimeSpan.FromSeconds(45)
        };

        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string? json = null,
            string? bearer = null, string? cookie = null, string? epoch = null,
            string? key = null, string? fingerprint = null, string? origin = null)
        {
            using var message = new HttpRequestMessage(method, route.TrimStart('/'));
            if (method != HttpMethod.Get && method != HttpMethod.Head)
            {
                message.Headers.TryAddWithoutValidation("Origin", origin ?? Required("MYDMSVN_WEB_ORIGIN"));
                if (route.StartsWith("/api/v1/auth/", StringComparison.Ordinal))
                    message.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            }
            if (bearer != null) message.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
            if (cookie != null) message.Headers.TryAddWithoutValidation("Cookie", cookie);
            if (epoch != null) message.Headers.TryAddWithoutValidation("X-MyDmsVn-Deployment-Epoch", epoch);
            if (key != null) message.Headers.TryAddWithoutValidation("Idempotency-Key", key);
            if (fingerprint != null) message.Headers.TryAddWithoutValidation("X-MyDmsVn-Operation-Fingerprint", fingerprint);
            if (json != null) message.Content = new StringContent(json, Encoding.UTF8, "application/json");
            else if (method == HttpMethod.Post || method == HttpMethod.Put)
                message.Content = new ByteArrayContent(Array.Empty<byte>());
            return await _client.SendAsync(message).ConfigureAwait(false);
        }

        public void Dispose() => _client.Dispose();
    }

    private sealed record Session(string AccessToken, string Cookie, int UserId, Guid SessionId);

    private static async Task<JsonDocument> AssertJsonAsync(HttpResponseMessage response, int status)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Cache-Control", out var cache) &&
            cache.Any(value => value.Contains("no-store", StringComparison.OrdinalIgnoreCase)));
        Assert.True(response.Content.Headers.ContentType?.MediaType == "application/json");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    private static string Cookie(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var values));
        var header = Assert.Single(values!);
        // A failed assertion must not echo the refresh-token cookie into CI logs.
        Assert.Equal(0, header.IndexOf("__Host-mydmsvn-rt=", StringComparison.Ordinal));
        Assert.True(header.Contains("; Secure", StringComparison.Ordinal) &&
            header.Contains("; HttpOnly", StringComparison.Ordinal) &&
            header.Contains("; SameSite=Strict", StringComparison.Ordinal) &&
            header.Contains("; Path=/", StringComparison.Ordinal));
        return header.Split(';', 2)[0];
    }

    private static async Task<Session> LoginAsync(Gateway gateway, string username, string password)
    {
        var json = JsonSerializer.Serialize(new { username, password });
        using var response = await gateway.SendAsync(HttpMethod.Post, "/api/v1/auth/login", json);
        using var document = await AssertJsonAsync(response, 200);
        var payload = document.RootElement.GetProperty("data");
        var token = payload.GetProperty("accessToken").GetString()!;
        Assert.True(!document.RootElement.GetRawText().Contains("refreshToken", StringComparison.OrdinalIgnoreCase));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("HS256", jwt.Header.Alg);
        Assert.Equal(Required("MYDMSVN_WEB_JWT_ISSUER"), jwt.Issuer);
        Assert.Contains(Required("MYDMSVN_WEB_JWT_AUDIENCE"), jwt.Audiences);
        Assert.True(jwt.ValidTo > DateTime.UtcNow);
        var userId = payload.GetProperty("user").GetProperty("userId").GetInt32();
        Assert.True(userId > 0);
        Assert.Equal(userId.ToString(CultureInfo.InvariantCulture),
            jwt.Claims.Single(claim => claim.Type == "sub").Value);
        var sessionId = Guid.Parse(jwt.Claims.Single(claim => claim.Type == "sid").Value);
        Assert.NotEqual(Guid.Empty, sessionId);
        return new Session(token, Cookie(response), userId, sessionId);
    }

    private static async Task<int> CountAsync(string statement, string parameter, object value)
    {
        using var sql = new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await sql.OpenAsync();
        using var command = new SqlCommand(statement, sql);
        command.CommandTimeout = 30;
        command.Parameters.AddWithValue(parameter, value);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<(long DocumentId, string Fingerprint)> JournalAsync(int actor, Guid key)
    {
        using var sql = new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await sql.OpenAsync();
        using var command = new SqlCommand(
            "SELECT ResultDocumentId,RequestFingerprint,Disposition,ResultBytes FROM dbo.WebOperationJournal WHERE ActorUserId=@actor AND OperationId=@op;", sql);
        command.Parameters.Add("@actor", SqlDbType.Int).Value = actor;
        command.Parameters.Add("@op", SqlDbType.UniqueIdentifier).Value = key;
        using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "The real SQL journal must contain the committed operation.");
        Assert.Equal("COMMITTED", reader.GetString(2));
        Assert.False(reader.IsDBNull(3));
        var row = (reader.GetInt64(0),
            Convert.ToHexString((byte[])reader.GetValue(1)).ToLowerInvariant());
        Assert.False(await reader.ReadAsync(), "The actor/key journal must be unique.");
        return row;
    }

    private static async Task<string> ContextEpochAsync(Gateway gateway, string access, int userId)
    {
        using var response = await gateway.SendAsync(HttpMethod.Get, "/api/v1/operations/context", bearer: access);
        using var json = await AssertJsonAsync(response, 200);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(userId, data.GetProperty("actorUserId").GetInt32());
        Assert.Equal("web4-v1", data.GetProperty("contractVersion").GetString());
        var epoch = data.GetProperty("deploymentEpoch").GetString()!;
        Assert.True(Guid.TryParseExact(epoch, "D", out var parsed) && parsed != Guid.Empty);
        return epoch;
    }

    [Fact]
    public async Task RealHttpsJwtRefreshReplayLogoutAndSqlSessionRevocation()
    {
        GuardNonProduction();
        using var gateway = new Gateway();
        using (var health = await gateway.SendAsync(HttpMethod.Get, "/api/v1/health"))
        using (var body = await AssertJsonAsync(health, 200))
            Assert.Equal("ok", body.RootElement.GetProperty("data").GetProperty("status").GetString());

        using (var denied = await gateway.SendAsync(HttpMethod.Get, "/api/v1/auth/me"))
        using (var body = await AssertJsonAsync(denied, 401))
            Assert.Equal("Auth.Unauthorized", body.RootElement.GetProperty("error").GetProperty("code").GetString());

        using (var crossOrigin = await gateway.SendAsync(HttpMethod.Post, "/api/v1/auth/login",
            JsonSerializer.Serialize(new { username = "rejected", password = "rejected" }),
            origin: "https://untrusted.invalid"))
        using (var body = await AssertJsonAsync(crossOrigin, 403))
            Assert.Equal("Auth.Forbidden", body.RootElement.GetProperty("error").GetProperty("code").GetString());

        var session = await LoginAsync(gateway, Required("VPS04A_LIVE_USERNAME"), Required("VPS04A_LIVE_PASSWORD"));
        using (var me = await gateway.SendAsync(HttpMethod.Get, "/api/v1/auth/me", bearer: session.AccessToken))
        using (var body = await AssertJsonAsync(me, 200))
            Assert.Equal(session.UserId, body.RootElement.GetProperty("data").GetProperty("userId").GetInt32());

        using var refresh = await gateway.SendAsync(HttpMethod.Post, "/api/v1/auth/refresh", cookie: session.Cookie);
        using var refreshedBody = await AssertJsonAsync(refresh, 200);
        var rotatedCookie = Cookie(refresh);
        var rotatedToken = refreshedBody.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
        Assert.True(rotatedCookie != session.Cookie && rotatedToken != session.AccessToken, "Refresh must rotate both credentials.");

        using (var good = await gateway.SendAsync(HttpMethod.Get, "/api/v1/auth/me", bearer: rotatedToken))
        using (var body = await AssertJsonAsync(good, 200))
            Assert.Equal(session.UserId, body.RootElement.GetProperty("data").GetProperty("userId").GetInt32());

        using (var replay = await gateway.SendAsync(HttpMethod.Post, "/api/v1/auth/refresh", cookie: session.Cookie))
        using (var body = await AssertJsonAsync(replay, 401))
            Assert.Equal("Auth.Unauthorized", body.RootElement.GetProperty("error").GetProperty("code").GetString());

        using (var revoked = await gateway.SendAsync(HttpMethod.Get, "/api/v1/auth/me", bearer: rotatedToken))
        using (var body = await AssertJsonAsync(revoked, 401))
            Assert.Equal("Auth.Unauthorized", body.RootElement.GetProperty("error").GetProperty("code").GetString());

        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.WebAuthSessions WHERE SessionId=@id AND RevokedAtUtc IS NOT NULL",
            "@id", session.SessionId));

        var second = await LoginAsync(gateway, Required("VPS04A_LIVE_USERNAME"), Required("VPS04A_LIVE_PASSWORD"));
        using (var logout = await gateway.SendAsync(HttpMethod.Post, "/api/v1/auth/logout", cookie: second.Cookie))
        using (var body = await AssertJsonAsync(logout, 200))
            Assert.True(logout.Headers.TryGetValues("Set-Cookie", out var values) &&
                values.Any(s => s.Contains("Max-Age=0", StringComparison.Ordinal)));

        using (var invalidated = await gateway.SendAsync(HttpMethod.Get, "/api/v1/auth/me", bearer: second.AccessToken))
        using (var body = await AssertJsonAsync(invalidated, 401))
            Assert.Equal("Auth.Unauthorized", body.RootElement.GetProperty("error").GetProperty("code").GetString());

        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.WebAuthSessions WHERE SessionId=@id AND RevokedAtUtc IS NOT NULL",
            "@id", second.SessionId));
    }

    [Fact]
    public async Task ConcurrentSameKeyCreatesExactlyOneBusinessRowAndOneDurableJournal()
    {
        GuardNonProduction();
        var customer = int.Parse(Required("VPS04A_LIVE_CUSTOMER_ID"), CultureInfo.InvariantCulture);
        var warehouse = int.Parse(Required("VPS04A_LIVE_WAREHOUSE_ID"), CultureInfo.InvariantCulture);
        var employee = int.Parse(Required("VPS04A_LIVE_EMPLOYEE_ID"), CultureInfo.InvariantCulture);
        var product = int.Parse(Required("VPS04A_LIVE_PRODUCT_ID"), CultureInfo.InvariantCulture);
        using var gateway = new Gateway();
        var session = await LoginAsync(gateway, Required("VPS04A_LIVE_USERNAME"), Required("VPS04A_LIVE_PASSWORD"));
        var epoch = await ContextEpochAsync(gateway, session.AccessToken, session.UserId);
        var operation = Guid.NewGuid();
        var uniqueNote = "VPS04A-LIVE-" + Guid.NewGuid().ToString("N");
        var body = JsonSerializer.Serialize(new
        {
            saleDate = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            customerId = customer, warehouseId = warehouse, employeeId = employee,
            note = uniqueNote,
            lines = new[] { new { saleLineId = "0", productId = product, qty = "1.0000", unitPrice = "2.0000", isGift = false } }
        });
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.WebOperationJournal WHERE OperationId=@id", "@id", operation));

        var requests = Enumerable.Range(0, 3).Select(_ => gateway.SendAsync(HttpMethod.Post,
            "/api/v1/sales-receipts", body, session.AccessToken, epoch: epoch, key: operation.ToString("D")));
        var first = await Task.WhenAll(requests);
        var texts = new List<string>();
        foreach (var response in first)
        {
            using (response)
            using (var json = await AssertJsonAsync(response, 200))
                texts.Add(json.RootElement.GetRawText());
        }
        Assert.All(texts, text => Assert.Equal(texts[0], text));
        using var parsed = JsonDocument.Parse(texts[0]);
        var saleId = long.Parse(parsed.RootElement.GetProperty("data").GetProperty("saleId").GetString()!, CultureInfo.InvariantCulture);
        Assert.True(saleId > 0);

        var journal = await JournalAsync(session.UserId, operation);
        Assert.Equal(saleId, journal.DocumentId);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.SalesReceipts WHERE SaleId=@id", "@id", saleId));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.SalesReceiptLines WHERE SaleId=@id", "@id", saleId));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.WebOperationJournal WHERE OperationId=@id", "@id", operation));

        using (var replay = await gateway.SendAsync(HttpMethod.Post, "/api/v1/sales-receipts",
            body, session.AccessToken, epoch: epoch, key: operation.ToString("D")))
        using (var replayJson = await AssertJsonAsync(replay, 200))
            Assert.Equal(texts[0], replayJson.RootElement.GetRawText());

        var changed = body.Replace(uniqueNote, uniqueNote + "-changed", StringComparison.Ordinal);
        using (var conflict = await gateway.SendAsync(HttpMethod.Post, "/api/v1/sales-receipts",
            changed, session.AccessToken, epoch: epoch, key: operation.ToString("D")))
        using (var json = await AssertJsonAsync(conflict, 409))
            Assert.Equal("Idempotency.KeyConflict", json.RootElement.GetProperty("error").GetProperty("code").GetString());

        using (var status = await gateway.SendAsync(HttpMethod.Get,
            "/api/v1/operations/" + operation.ToString("D"),
            bearer: session.AccessToken, epoch: epoch, fingerprint: journal.Fingerprint))
        using (var json = await AssertJsonAsync(status, 200))
        {
            var data = json.RootElement.GetProperty("data");
            Assert.Equal("COMMITTED", data.GetProperty("state").GetString());
            Assert.Equal("Sales.Create", data.GetProperty("operationKind").GetString());
            Assert.Equal(journal.Fingerprint, data.GetProperty("requestFingerprint").GetString());
        }

        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.WebOperationJournal WHERE OperationId=@id", "@id", operation));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.SalesReceipts WHERE SaleId=@id", "@id", saleId));
    }

    [Fact]
    public async Task InvalidBodyAndStaleEpochNeverCreateJournalOrBusinessRow()
    {
        GuardNonProduction();
        var customer = int.Parse(Required("VPS04A_LIVE_CUSTOMER_ID"), CultureInfo.InvariantCulture);
        var warehouse = int.Parse(Required("VPS04A_LIVE_WAREHOUSE_ID"), CultureInfo.InvariantCulture);
        var employee = int.Parse(Required("VPS04A_LIVE_EMPLOYEE_ID"), CultureInfo.InvariantCulture);
        var product = int.Parse(Required("VPS04A_LIVE_PRODUCT_ID"), CultureInfo.InvariantCulture);
        using var gateway = new Gateway();
        var session = await LoginAsync(gateway, Required("VPS04A_LIVE_USERNAME"), Required("VPS04A_LIVE_PASSWORD"));
        var epoch = await ContextEpochAsync(gateway, session.AccessToken, session.UserId);
        var note = "VPS04A-NEGATIVE-" + Guid.NewGuid().ToString("N");
        var body = JsonSerializer.Serialize(new
        {
            saleDate = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            customerId = customer, warehouseId = warehouse, employeeId = employee,
            note,
            lines = new[] { new { saleLineId = "0", productId = product, qty = "0.0000", unitPrice = "2.0000", isGift = false } }
        });
        var invalidKey = Guid.NewGuid();
        using (var invalid = await gateway.SendAsync(HttpMethod.Post, "/api/v1/sales-receipts", body,
            session.AccessToken, epoch: epoch, key: invalidKey.ToString("D")))
        using (var json = await AssertJsonAsync(invalid, 400))
            Assert.Equal("ValidationError", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.WebOperationJournal WHERE OperationId=@id", "@id", invalidKey));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.SalesReceipts WHERE Note=@note", "@note", note));

        var staleKey = Guid.NewGuid();
        var validBody = body.Replace("\"qty\":\"0.0000\"", "\"qty\":\"1.0000\"", StringComparison.Ordinal);
        var staleEpoch = Guid.NewGuid().ToString("D");
        using (var stale = await gateway.SendAsync(HttpMethod.Post, "/api/v1/sales-receipts", validBody,
            session.AccessToken, epoch: staleEpoch, key: staleKey.ToString("D")))
        using (var json = await AssertJsonAsync(stale, 409))
            Assert.Equal("Deployment.EpochMismatch", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.WebOperationJournal WHERE OperationId=@id", "@id", staleKey));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.SalesReceipts WHERE Note=@note", "@note", note));
    }

    [Fact]
    public async Task DistinctDeniedActorCannotReadSalesEvenWithValidSqlSession()
    {
        GuardNonProduction();
        using var gateway = new Gateway();
        var denied = await LoginAsync(gateway, Required("VPS04A_LIVE_DENIED_USERNAME"), Required("VPS04A_LIVE_DENIED_PASSWORD"));
        using (var me = await gateway.SendAsync(HttpMethod.Get, "/api/v1/auth/me", bearer: denied.AccessToken))
        using (var json = await AssertJsonAsync(me, 200))
            Assert.Equal(denied.UserId, json.RootElement.GetProperty("data").GetProperty("userId").GetInt32());
        using (var sales = await gateway.SendAsync(HttpMethod.Get, "/api/v1/sales-receipts", bearer: denied.AccessToken))
        using (var json = await AssertJsonAsync(sales, 403))
            Assert.Equal("Auth.Forbidden", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }
}
#endif
