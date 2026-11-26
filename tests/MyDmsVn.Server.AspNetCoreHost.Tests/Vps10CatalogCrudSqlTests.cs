// Actual HTTPS -> disposable SQL13, not a staging or production acceptance.
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

public sealed class Vps10CatalogCrudSqlTests
{
    private const string Origin = "https://localhost:50443";
    private static string Required(string key) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } value
            ? value : throw new InvalidOperationException("Missing disposable Catalog CI environment: " + key);
    private sealed record Session(string Token, int UserId);
    private sealed record Reply(int Status, JsonDocument Body) : IDisposable
    {
        public JsonElement Data => Body.RootElement.GetProperty("data");
        public string? ErrorCode => Body.RootElement.GetProperty("error").GetProperty("code").GetString();
        public void Dispose() => Body.Dispose();
    }

    private sealed class Gateway : IDisposable
    {
        private readonly HttpClient _client = new(new HttpClientHandler { UseCookies = false })
        {
            BaseAddress = new Uri(Origin + "/"),
            Timeout = TimeSpan.FromSeconds(45)
        };

        public async Task<Reply> RequestAsync(HttpMethod method, string path, string? token = null, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path.TrimStart('/'));
            if (token != null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            if (body != null)
            {
                request.Headers.TryAddWithoutValidation("Origin", Origin);
                request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            }
            using var response = await _client.SendAsync(request);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            return new Reply((int)response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync()));
        }

        public async Task<Session> LoginAsync(string username)
        {
            using var response = await RequestAsync(HttpMethod.Post, "api/v1/auth/login", body: new
            {
                username,
                password = Required("VPS04A_LIVE_DENIED_PASSWORD")
            });
            Assert.Equal(200, response.Status);
            return new Session(response.Data.GetProperty("accessToken").GetString()!,
                response.Data.GetProperty("user").GetProperty("userId").GetInt32());
        }

        public void Dispose() => _client.Dispose();
    }

    private static SqlConnection Oracle() => new(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
    private static async Task<int> CountAsync(string sql, string? code = null)
    {
        using var connection = Oracle();
        await connection.OpenAsync();
        using var command = new SqlCommand(sql, connection);
        if (code != null) command.Parameters.Add("@code", SqlDbType.NVarChar, 32).Value = code;
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task AssertRowAsync(string table, string key, string code, int id, string name,
        bool active, int actor, bool updated, int? userId = null)
    {
        // Identifiers only originate in the fixed four-family matrix below.
        Assert.Contains((table, key), new[] { ("Products","ProductId"), ("Warehouses","WarehouseId"),
            ("Employees","EmployeeId"), ("Customers","CustomerId") });
        using var connection = Oracle();
        await connection.OpenAsync();
        var columns = table == "Employees" ? ",UserId" : "";
        using var command = new SqlCommand("SELECT " + key + ",Name,IsActive,CreatedByUserId,UpdatedByUserId" +
            columns + " FROM dbo." + table + " WHERE Code=@code;", connection);
        command.Parameters.Add("@code", SqlDbType.NVarChar, 32).Value = code;
        using var row = await command.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        Assert.Equal(id, row.GetInt32(0));
        Assert.Equal(name, row.GetString(1));
        Assert.Equal(active, row.GetBoolean(2));
        Assert.Equal(actor, row.GetInt32(3));
        if (updated) Assert.Equal(actor, row.GetInt32(4));
        else Assert.True(row.IsDBNull(4));
        if (table == "Employees")
            Assert.Equal(userId, row.IsDBNull(5) ? (int?)null : row.GetInt32(5));
        Assert.False(await row.ReadAsync());
    }

    private static Dictionary<string, object?> Body(string table, string code, string name, int? userId = null)
    {
        var body = new Dictionary<string, object?> { ["code"] = code, ["name"] = name };
        if (table == "Products") body["unit"] = "kg";
        if (table == "Warehouses" || table == "Customers") body["address"] = "Hải Phòng";
        if (table == "Employees" || table == "Customers") body["phone"] = null;
        if (table == "Employees") body["userId"] = userId;
        if (table == "Customers") body["taxCode"] = null;
        return body;
    }

    [Fact]
    public async Task AllFourNativeCatalogFamiliesPreserveIndependentPermissionsAndSqlState()
    {
        Assert.Equal("approved-container-only", Required("VPS04A_CI_EPHEMERAL_SQL"));
        Assert.Equal("approved-nonproduction-only", Required("VPS04A_LIVE_ALLOW_MUTATION"));
        Assert.Equal("approved-ci-only", Required("VPS04A_CI_FAULT_APPROVED"));
        Assert.Equal(Origin, Required("MYDMSVN_WEB_ORIGIN"));
        Assert.Equal("web5", Required("MYDMSVN_WEB_OPERATION_MODE"));
        Assert.Equal("enabled", Required("MYDMSVN_WEB_DESKTOP_API"));
        var inactiveUser = int.Parse(Required("VPS106A_CI_INACTIVE_USER_ID"), CultureInfo.InvariantCulture);
        var journalBefore = await CountAsync("SELECT COUNT(*) FROM dbo.WebOperationJournal;");
        using var first = new Gateway();
        using var second = new Gateway();
        var reader = await first.LoginAsync("vps106a_ci_read");
        var writer = await first.LoginAsync("vps106a_ci_write");
        var denied = await second.LoginAsync(Required("VPS04A_LIVE_DENIED_USERNAME"));
        foreach (var (table, key, singular) in new[] { ("Products","ProductId","Product"),
                     ("Warehouses","WarehouseId","Warehouse"), ("Employees","EmployeeId","Employee"),
                     ("Customers","CustomerId","Customer") })
        {
            var path = "api/v1/catalog/" + table.ToLowerInvariant();
            var code = "VPS106A-" + singular;
            const string initial = "Danh mục Đặng";
            const string changed = "Đã cập nhật Hải Phòng";
            var body = Body(table, code, initial);
            // Count all rows, not just the valid code: a rejected overlong code
            // could otherwise be silently truncated and inserted unnoticed.
            var rowsBeforeRejectedRequests = await CountAsync("SELECT COUNT(*) FROM dbo." + table + ";");
            using (var forbidRead = await first.RequestAsync(HttpMethod.Get, path, writer.Token))
                Assert.Equal(403, forbidRead.Status);
            using (var forbidWrite = await first.RequestAsync(HttpMethod.Post, path, reader.Token, body))
                Assert.Equal(403, forbidWrite.Status);
            using (var forbidBoth = await second.RequestAsync(HttpMethod.Post, path, denied.Token, body))
                Assert.Equal(403, forbidBoth.Status);
            using (var tooLong = await first.RequestAsync(HttpMethod.Post, path, writer.Token,
                       Body(table, new string('X', 33), initial)))
                Assert.Equal(400, tooLong.Status);
            Assert.Equal(rowsBeforeRejectedRequests, await CountAsync("SELECT COUNT(*) FROM dbo." + table + ";"));
            Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo." + table + " WHERE Code=@code;", code));

            // Two different HTTP clients, one committed row, one real unique-code conflict.
            var a = first.RequestAsync(HttpMethod.Post, path, writer.Token, body);
            var b = second.RequestAsync(HttpMethod.Post, path, writer.Token, body);
            await Task.WhenAll(a,b);
            using var ra = await a;
            using var rb = await b;
            var replies = new[] { ra, rb };
            var success = Assert.Single(replies, x => x.Status == 200);
            var conflict = Assert.Single(replies, x => x.Status == 409);
            Assert.Equal(singular + ".DuplicateCode", conflict.ErrorCode);
            var id = success.Data.GetProperty("id").GetInt32();
            Assert.True(id > 0);
            await AssertRowAsync(table,key,code,id,initial,true,writer.UserId,false);
            var detail = path + "/" + id.ToString(CultureInfo.InvariantCulture);

            using (var denyGet = await first.RequestAsync(HttpMethod.Get, detail, writer.Token))
                Assert.Equal(403, denyGet.Status);
            using (var denyUpdate = await first.RequestAsync(HttpMethod.Put, detail, reader.Token, Body(table,code,changed)))
                Assert.Equal(403, denyUpdate.Status);
            using (var denyActive = await first.RequestAsync(HttpMethod.Put, detail + "/active", reader.Token, new { isActive = false }))
                Assert.Equal(403, denyActive.Status);
            var employeeLink = table == "Employees" ? (int?)inactiveUser : null;
            using (var update = await first.RequestAsync(HttpMethod.Put, detail, writer.Token, Body(table,code,changed,employeeLink)))
            {
                Assert.Equal(200, update.Status);
                Assert.Equal(changed, update.Data.GetProperty("name").GetString());
            }
            await AssertRowAsync(table,key,code,id,changed,true,writer.UserId,true,employeeLink);
            using (var get = await first.RequestAsync(HttpMethod.Get, detail, reader.Token))
            {
                Assert.Equal(200, get.Status);
                Assert.Equal(changed, get.Data.GetProperty("name").GetString());
                if (employeeLink.HasValue)
                    Assert.Equal(employeeLink.Value, get.Data.GetProperty("userId").GetInt32());
            }
            if (employeeLink.HasValue)
            {
                // Existing FK/unique behavior: inactive user may be linked;
                // a second link or a nonexistent UserId must still conflict.
                using var linked = await second.RequestAsync(HttpMethod.Post, path, writer.Token,
                    Body(table,"VPS106A-linked",changed,employeeLink));
                Assert.Equal(409, linked.Status);
                Assert.Equal("Employee.UserAlreadyLinked", linked.ErrorCode);
                using var absent = await second.RequestAsync(HttpMethod.Post, path, writer.Token,
                    Body(table,"VPS106A-absent",changed,int.MaxValue));
                Assert.Equal(409, absent.Status);
                Assert.Equal("Employee.UserNotFound", absent.ErrorCode);
                Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.Employees WHERE Code=@code;", "VPS106A-linked"));
                Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.Employees WHERE Code=@code;", "VPS106A-absent"));
            }

            var list = path + "?search=" + Uri.EscapeDataString(code) + "&pageNumber=1&pageSize=1&includeInactive=";
            using (var listed = await first.RequestAsync(HttpMethod.Get, list + "false", reader.Token))
            {
                Assert.Equal(200, listed.Status);
                Assert.Equal(id, Assert.Single(listed.Data.GetProperty("items").EnumerateArray()).GetProperty("id").GetInt32());
            }
            using (var deactivate = await first.RequestAsync(HttpMethod.Put, detail + "/active", writer.Token, new { isActive = false }))
                Assert.Equal(200, deactivate.Status);
            await AssertRowAsync(table,key,code,id,changed,false,writer.UserId,true,employeeLink);
            using (var hidden = await first.RequestAsync(HttpMethod.Get, list + "false", reader.Token))
            {
                Assert.Equal(200, hidden.Status);
                Assert.Empty(hidden.Data.GetProperty("items").EnumerateArray());
            }
            using (var included = await first.RequestAsync(HttpMethod.Get, list + "true", reader.Token))
            {
                Assert.Equal(200, included.Status);
                Assert.False(Assert.Single(included.Data.GetProperty("items").EnumerateArray()).GetProperty("isActive").GetBoolean());
            }
            using (var lookup = await first.RequestAsync(HttpMethod.Get, path + "/lookup?limit=25&search=" + Uri.EscapeDataString(code), reader.Token))
            {
                Assert.Equal(200, lookup.Status);
                Assert.Empty(lookup.Data.EnumerateArray());
            }
            using (var activate = await first.RequestAsync(HttpMethod.Put, detail + "/active", writer.Token, new { isActive = true }))
                Assert.Equal(200, activate.Status);
            await AssertRowAsync(table,key,code,id,changed,true,writer.UserId,true,employeeLink);
        }
        Assert.Equal(journalBefore, await CountAsync("SELECT COUNT(*) FROM dbo.WebOperationJournal;"));
    }
}
#endif
