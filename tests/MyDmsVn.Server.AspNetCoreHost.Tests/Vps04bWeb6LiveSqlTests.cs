// Disposable HTTPS -> SQL13 -> WEB6 acceptance. Opt-in only; no production
// credentials, SQL schema changes or intentionally permissive TLS.
#if VPS04A_LIVE
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

[SupportedOSPlatform("linux")]
public sealed class Vps04bWeb6LiveSqlTests
{
    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value :
        throw new InvalidOperationException("Missing disposable VPS-04B CI configuration.");

    private static void Guard()
    {
        Assert.True(OperatingSystem.IsLinux());
        Assert.Equal("approved-container-only", Required("VPS04A_CI_EPHEMERAL_SQL"));
        Assert.Equal("approved-nonproduction-only", Required("VPS04A_LIVE_ALLOW_MUTATION"));
        Assert.Equal("web5", Required("MYDMSVN_WEB_OPERATION_MODE"));
        Assert.Equal("receipt-v1", Required("MYDMSVN_WEB6_RESEND_ELIGIBILITY"));
        Assert.Equal("https://localhost:50443", Required("MYDMSVN_WEB_ORIGIN"));
        Assert.Equal("vps04b-ci", Required("MYDMSVN_WEB6_CANDIDATE_ID"));
        var root = Required("MYDMSVN_WEB6_CANDIDATE_ROOT");
        var path = Required("MYDMSVN_WEB_SECURITY_AUDIT_PATH");
        Assert.StartsWith(root + "/", path, StringComparison.Ordinal);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(root));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    private sealed record Session(string Bearer, string Cookie, int UserId, Guid SessionId);
    private sealed record Journal(Guid Id, Guid Epoch, string Fingerprint);

    private sealed class Gateway : IDisposable
    {
        private readonly HttpClient _client = new(new HttpClientHandler { UseCookies = false })
        {
            BaseAddress = new Uri(Required("MYDMSVN_WEB_ORIGIN") + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };

        public async Task<HttpResponseMessage> Send(HttpMethod verb, string route, string? bearer = null,
            string? cookie = null, string? json = null, IReadOnlyDictionary<string, string>? headers = null)
        {
            using var request = new HttpRequestMessage(verb, route.TrimStart('/'));
            if (verb == HttpMethod.Post || verb == HttpMethod.Put)
            {
                request.Headers.TryAddWithoutValidation("Origin", Required("MYDMSVN_WEB_ORIGIN"));
                if (route.StartsWith("/api/v1/auth/", StringComparison.Ordinal))
                    request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                request.Content = json != null
                    ? new StringContent(json, Encoding.UTF8, "application/json")
                    : new ByteArrayContent(Array.Empty<byte>());
            }
            if (bearer != null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
            if (cookie != null) request.Headers.TryAddWithoutValidation("Cookie", cookie);
            if (headers != null)
                foreach (var (key, value) in headers)
                    request.Headers.TryAddWithoutValidation(key, value);
            return await _client.SendAsync(request);
        }

        public async Task<Session> Login()
        {
            var credentials = JsonSerializer.Serialize(new
            {
                username = Required("VPS04A_LIVE_USERNAME"),
                password = Required("VPS04A_LIVE_PASSWORD")
            });
            using var reply = await Send(HttpMethod.Post, "/api/v1/auth/login", json: credentials);
            Assert.Equal(200, (int)reply.StatusCode);
            using var document = await JsonDocument.ParseAsync(await reply.Content.ReadAsStreamAsync());
            var data = document.RootElement.GetProperty("data");
            var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
                .ReadJwtToken(data.GetProperty("accessToken").GetString()!);
            var id = Guid.Parse(jwt.Claims.Single(x => x.Type == "sid").Value);
            var cookie = Assert.Single(reply.Headers.GetValues("Set-Cookie")).Split(';')[0];
            Assert.StartsWith("__Host-mydmsvn-rt=", cookie, StringComparison.Ordinal);
            return new Session(data.GetProperty("accessToken").GetString()!, cookie,
                data.GetProperty("user").GetProperty("userId").GetInt32(), id);
        }

        public void Dispose() => _client.Dispose();
    }

    private static async Task<Journal> JournalFor(int actor)
    {
        await using var connection = new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await connection.OpenAsync();
        await using var command = new SqlCommand(@"
SELECT TOP(1) OperationId, DeploymentEpoch, RequestFingerprint
FROM dbo.WebOperationJournal
WHERE ActorUserId=@actor AND OperationKind=1 AND Disposition='COMMITTED'
ORDER BY CreatedAtUtc DESC;", connection);
        command.Parameters.Add("@actor", SqlDbType.Int).Value = actor;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "A real committed Sales.Create journal is required.");
        var result = new Journal(reader.GetGuid(0), reader.GetGuid(1),
            Convert.ToHexString((byte[])reader.GetValue(2)).ToLowerInvariant());
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static async Task<int> RevokedSessionCount(Guid session)
    {
        await using var connection = new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.WebAuthSessions WHERE SessionId=@id AND RevokedAtUtc IS NOT NULL;", connection);
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = session;
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, string> Headers(Journal journal, string? receipt = null)
    {
        var headers = new Dictionary<string, string>
        {
            ["X-MyDmsVn-Deployment-Epoch"] = journal.Epoch.ToString("D"),
            ["X-MyDmsVn-Operation-Fingerprint"] = journal.Fingerprint,
            ["X-MyDmsVn-Operation-Kind"] = "Sales.Create"
        };
        if (receipt != null) headers["X-MyDmsVn-Resend-Receipt"] = receipt;
        return headers;
    }

    private static async Task<JsonDocument> Json(HttpResponseMessage response, int expected)
    {
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.TryGetValues("Cache-Control", out var cache) &&
            cache.Any(value => value.Contains("no-store", StringComparison.OrdinalIgnoreCase)));
        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
    }

    private static void VerifyDurableAudit()
    {
        var content = File.ReadAllLines(Required("MYDMSVN_WEB_SECURITY_AUDIT_PATH"));
        Assert.NotEmpty(content);
        foreach (var line in content)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.Equal(new[] { "action", "category", "correlationId", "outcome", "userId", "utc" },
                doc.RootElement.EnumerateObject().Select(property => property.Name)
                    .OrderBy(x => x, StringComparer.Ordinal).ToArray());
            Assert.True(DateTime.TryParse(doc.RootElement.GetProperty("utc").GetString(),
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var utc));
            Assert.Equal(DateTimeKind.Utc, utc.Kind);
        }
        var all = string.Concat(content);
        Assert.False(all.Contains(Required("VPS04A_LIVE_PASSWORD"), StringComparison.Ordinal),
            "Durable audit must never contain the disposable login password.");
        Assert.False(all.Contains("__Host-mydmsvn-rt", StringComparison.Ordinal),
            "Durable audit must never contain refresh cookie material.");
    }

    [Fact]
    public async Task EnabledWeb6ContextReceiptEligibilityAndSessionRevocationWithRealSql()
    {
        Guard();
        using var api = new Gateway();
        var session = await api.Login();
        var journal = await JournalFor(session.UserId);

        using (var ctx = await api.Send(HttpMethod.Get, "/api/v3/operations/resend-context",
            bearer: session.Bearer))
        using (var json = await Json(ctx, 200))
        {
            var data = json.RootElement.GetProperty("data");
            Assert.Equal("web6-resend-eligibility-v1", data.GetProperty("contractVersion").GetString());
            Assert.Equal("vps04b-ci", data.GetProperty("candidateId").GetString());
            Assert.Equal(journal.Epoch.ToString("D"), data.GetProperty("deploymentEpoch").GetString());
        }

        string receipt;
        using (var issued = await api.Send(HttpMethod.Get,
            "/api/v3/operations/" + journal.Id.ToString("D") + "/resend-receipt",
            bearer: session.Bearer, headers: Headers(journal)))
        using (var json = await Json(issued, 200))
        {
            receipt = json.RootElement.GetProperty("data").GetProperty("receipt").GetString()!;
            Assert.InRange(receipt.Length, 20, 4096);
        }

        using (var checkedReceipt = await api.Send(HttpMethod.Get,
            "/api/v3/operations/" + journal.Id.ToString("D") + "/resend-eligibility",
            bearer: session.Bearer, headers: Headers(journal, receipt)))
        using (var json = await Json(checkedReceipt, 200))
        {
            var data = json.RootElement.GetProperty("data");
            Assert.Equal("DENY", data.GetProperty("decision").GetString());
            Assert.StartsWith("COMMITTED_RESULT_", data.GetProperty("reason").GetString()!,
                StringComparison.Ordinal);
            Assert.False(data.TryGetProperty("terminal", out _));
        }

        // Mutating the signature of a bound receipt cannot grant read authority.
        var corrupted = receipt[..^1] + (receipt[^1] == 'A' ? "B" : "A");
        using (var denied = await api.Send(HttpMethod.Get,
            "/api/v3/operations/" + journal.Id.ToString("D") + "/resend-eligibility",
            bearer: session.Bearer, headers: Headers(journal, corrupted)))
        using (var json = await Json(denied, 400))
            Assert.Equal("ValidationError", json.RootElement.GetProperty("error")
                .GetProperty("code").GetString());

        using (var denied = await api.Send(HttpMethod.Get, "/api/v3/operations/resend-context"))
        using (var json = await Json(denied, 401))
            Assert.Equal("Auth.Unauthorized", json.RootElement.GetProperty("error")
                .GetProperty("code").GetString());

        // Logout revokes the SQL session; a still-cryptographically-valid JWT
        // no longer grants access to the WEB6 endpoint.
        using (var logout = await api.Send(HttpMethod.Post, "/api/v1/auth/logout", cookie: session.Cookie))
        using (var json = await Json(logout, 200)) { }
        Assert.Equal(1, await RevokedSessionCount(session.SessionId));
        using (var revoked = await api.Send(HttpMethod.Get, "/api/v3/operations/resend-context",
            bearer: session.Bearer))
        using (var json = await Json(revoked, 401))
            Assert.Equal("Auth.Unauthorized", json.RootElement.GetProperty("error")
                .GetProperty("code").GetString());

        VerifyDurableAudit();
    }


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

    [Fact]
    public async Task RuntimeAuditWriteRefusalDegradesHealthWithoutLeakingPathOrSecrets()
    {
        Guard();
        using var api = new Gateway();
        var path = Required("MYDMSVN_WEB_SECURITY_AUDIT_PATH");
        var prior = File.ReadAllLines(path).Length;
        Assert.True(prior > 0, "The earlier WEB6 session must have produced durable audit.");
        var mode = File.GetUnixFileMode(path);
        try
        {
            File.SetUnixFileMode(path, mode | UnixFileMode.OtherRead);
            // A login success requires an audit write before new credentials
            // are admitted. The failure must not deliver a refresh cookie.
            var credentials = JsonSerializer.Serialize(new
            {
                username = Required("VPS04A_LIVE_USERNAME"),
                password = Required("VPS04A_LIVE_PASSWORD")
            });
            using var login = await api.Send(HttpMethod.Post, "/api/v1/auth/login", json: credentials);
            Assert.NotEqual(200, (int)login.StatusCode);
            Assert.False(login.Headers.Contains("Set-Cookie"));
            var loginBody = await login.Content.ReadAsStringAsync();
            Assert.False(loginBody.Contains(path, StringComparison.Ordinal),
                "HTTP failure must not disclose the private audit file path.");
            Assert.False(loginBody.Contains(Required("VPS04A_LIVE_PASSWORD"), StringComparison.Ordinal),
                "HTTP failure must never return the disposable login password.");

            using var health = await api.Send(HttpMethod.Get, "/api/v1/health");
            using var json = await Json(health, 503);
            Assert.Equal("SecurityAudit.Unavailable",
                json.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.False(json.RootElement.GetRawText().Contains(path, StringComparison.Ordinal),
                "Degraded-health error must not disclose the private audit file path.");
            Assert.Equal(prior, File.ReadAllLines(path).Length);
        }
        finally
        {
            File.SetUnixFileMode(path, mode);
        }
    }

    [Fact]
    public async Task NewLinuxHostRecoversHealthyAndKeepsPriorAuditAfterRestart()
    {
        Guard();
        VerifyDurableAudit();
        using var api = new Gateway();
        using (var health = await api.Send(HttpMethod.Get, "/api/v1/health"))
        using (var json = await Json(health, 200))
            Assert.Equal("ok", json.RootElement.GetProperty("data")
                .GetProperty("status").GetString());
        using var session = await api.Send(HttpMethod.Post, "/api/v1/auth/login",
            json: JsonSerializer.Serialize(new
            {
                username = Required("VPS04A_LIVE_USERNAME"),
                password = Required("VPS04A_LIVE_PASSWORD")
            }));
        using var body = await Json(session, 200);
        VerifyDurableAudit();
    }
}
#endif
