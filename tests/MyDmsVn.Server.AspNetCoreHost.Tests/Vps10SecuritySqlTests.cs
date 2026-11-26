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

public sealed class Vps10SecuritySqlTests
{
    private const string Origin = "https://localhost:50443";
    private static string Required(string key) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } value
            ? value : throw new InvalidOperationException("Missing disposable Security CI environment: " + key);
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

    private static async Task<long> ScalarAsync(string sql, int actor, int role, Guid correlation)
    {
        using var connection = new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await connection.OpenAsync();
        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@actor", SqlDbType.Int).Value = actor;
        command.Parameters.Add("@role", SqlDbType.Int).Value = role;
        command.Parameters.Add("@correlation", SqlDbType.UniqueIdentifier).Value = correlation;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task NativeSecurityUsesRestrictedPrincipalsTrustedActorAndSqlAudit()
    {
        Assert.Equal("approved-container-only", Required("VPS04A_CI_EPHEMERAL_SQL"));
        Assert.Equal("approved-nonproduction-only", Required("VPS04A_LIVE_ALLOW_MUTATION"));
        Assert.Equal("approved-ci-only", Required("VPS04A_CI_FAULT_APPROVED"));
        Assert.Equal(Origin, Required("MYDMSVN_WEB_ORIGIN"));
        Assert.Equal("web5", Required("MYDMSVN_WEB_OPERATION_MODE"));
        Assert.Equal("enabled", Required("MYDMSVN_WEB_DESKTOP_API"));

        using var gateway = new Gateway();
        var reader = await gateway.LoginAsync("vps106b_ci_read");
        var manager = await gateway.LoginAsync("vps106b_ci_role");
        var denied = await gateway.LoginAsync(Required("VPS04A_LIVE_DENIED_USERNAME"));
        Assert.NotEqual(reader.UserId, manager.UserId);
        const string path = "api/v1/security/";
        using var catalog = await gateway.RequestAsync(HttpMethod.Get, path + "permissions/catalog", reader.Token);
        Assert.Equal(200, catalog.Status);
        using (var wrongCatalog = await gateway.RequestAsync(HttpMethod.Get, path + "permissions/catalog", manager.Token))
            Assert.Equal(403, wrongCatalog.Status);
        using (var wrongRoles = await gateway.RequestAsync(HttpMethod.Get, path + "roles", reader.Token))
            Assert.Equal(403, wrongRoles.Status);
        using (var wrongUsers = await gateway.RequestAsync(HttpMethod.Get, path + "users", manager.Token))
            Assert.Equal(403, wrongUsers.Status);
        using (var noRoles = await gateway.RequestAsync(HttpMethod.Get, path + "roles", denied.Token))
            Assert.Equal(403, noRoles.Status);
        using (var foundation = await gateway.RequestAsync(HttpMethod.Get, "api/v1/foundation/status?clientName=Desktop", denied.Token))
            Assert.Equal(200, foundation.Status);

        const string name = "VPS106B CI Role";
        var rejected = Guid.NewGuid();
        using (var noWrite = await gateway.RequestAsync(HttpMethod.Post, path + "mutations", reader.Token,
            new { target = 1, operation = 0, name, correlationId = rejected }))
            Assert.Equal(403, noWrite.Status);
        var spoofed = Guid.NewGuid();
        using (var spoof = await gateway.RequestAsync(HttpMethod.Post, path + "mutations", manager.Token,
            new { target = 1, operation = 0, name, actorUserId = reader.UserId, correlationId = spoofed }))
            Assert.Equal(400, spoof.Status);
        var escalation = Guid.NewGuid();
        using (var noSelfGrant = await gateway.RequestAsync(HttpMethod.Post, path + "mutations", manager.Token,
            new { target = 0, operation = 5, targetId = manager.UserId, expectedRevision = 0L,
                permissionKey = "Security.Users.Manage", decision = true, correlationId = escalation }))
            Assert.Equal(403, noSelfGrant.Status);
        foreach (var id in new[] { rejected, spoofed, escalation })
            Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM dbo.SecurityAuditEvents WHERE CorrelationId=@correlation;",
                manager.UserId, 0, id));
        Assert.Equal(0L, await ScalarAsync("SELECT COUNT(*) FROM dbo.Roles WHERE RoleName=N'VPS106B CI Role';",
            manager.UserId, 0, Guid.Empty));

        var createId = Guid.NewGuid();
        using (var created = await gateway.RequestAsync(HttpMethod.Post, path + "mutations", manager.Token,
            new { target = 1, operation = 0, name, correlationId = createId }))
            Assert.Equal(200, created.Status);
        using var roles = await gateway.RequestAsync(HttpMethod.Get, path + "roles", manager.Token);
        Assert.Equal(200, roles.Status);
        var role = Assert.Single(roles.Data.EnumerateArray(), row => row.GetProperty("name").GetString() == name);
        var roleId = role.GetProperty("roleId").GetInt32();
        var revision = role.GetProperty("assignmentRevision").GetInt64();
        Assert.True(roleId > 0);
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM dbo.Roles WHERE RoleId=@role AND CreatedByUserId=@actor;",
            manager.UserId, roleId, createId));
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM dbo.SecurityAuditEvents WHERE TargetType=N'Role' AND TargetId=@role AND ActorUserId=@actor AND Operation=N'Create' AND CorrelationId=@correlation;",
            manager.UserId, roleId, createId));

        var assignment = Guid.NewGuid();
        using var assigned = await gateway.RequestAsync(HttpMethod.Post, path + "mutations", manager.Token,
            new { target = 1, operation = 5, targetId = roleId, expectedRevision = revision,
                permissionKey = "Catalog.Products.Read", decision = true, correlationId = assignment });
        Assert.Equal(200, assigned.Status);
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM dbo.RolePermissions WHERE RoleId=@role AND PermissionKey=N'Catalog.Products.Read' AND CreatedByUserId=@actor;",
            manager.UserId, roleId, assignment));
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM dbo.SecurityAuditEvents WHERE TargetType=N'Role' AND TargetId=@role AND ActorUserId=@actor AND Operation=N'SetPermission' AND CorrelationId=@correlation;",
            manager.UserId, roleId, assignment));

        var json = JsonSerializer.Serialize(new { schemaVersion = 1, targetType = "Role", targetId = roleId,
            entries = new[] { new { permissionKey = "Catalog.Customers.Read", decision = "Allow" } } });
        var previewBody = new { targetType = 1, targetId = roleId, json };
        using (var noPreview = await gateway.RequestAsync(HttpMethod.Post, path + "import/preview", reader.Token, previewBody))
            Assert.Equal(403, noPreview.Status);
        using (var malformed = await gateway.RequestAsync(HttpMethod.Post, path + "import/preview", manager.Token,
            new { targetType = 1, targetId = roleId, json = "{" }))
            Assert.Equal(400, malformed.Status);
        using var preview = await gateway.RequestAsync(HttpMethod.Post, path + "import/preview", manager.Token, previewBody);
        Assert.Equal(200, preview.Status);
        var expectedTargetRevision = preview.Data.GetProperty("targetRevision").GetInt64();
        var expectedCatalogDigest = preview.Data.GetProperty("catalogDigest").GetString();
        Assert.Equal(revision + 1, expectedTargetRevision);
        Assert.False(string.IsNullOrWhiteSpace(expectedCatalogDigest));
        var applyBody = new { targetType = 1, targetId = roleId, json, expectedTargetRevision, expectedCatalogDigest };
        using (var noApply = await gateway.RequestAsync(HttpMethod.Post, path + "import/apply", reader.Token, applyBody))
            Assert.Equal(403, noApply.Status);
        using (var applied = await gateway.RequestAsync(HttpMethod.Post, path + "import/apply", manager.Token, applyBody))
        {
            Assert.Equal(200, applied.Status);
            Assert.Equal(expectedTargetRevision + 1, applied.Data.GetProperty("targetRevision").GetInt64());
            Assert.Equal(1, applied.Data.GetProperty("addedCount").GetInt32());
        }
        using (var stale = await gateway.RequestAsync(HttpMethod.Post, path + "import/apply", manager.Token, applyBody))
            Assert.Equal(409, stale.Status);

        Assert.Equal(expectedTargetRevision + 1, await ScalarAsync("SELECT AssignmentRevision FROM dbo.Roles WHERE RoleId=@role;",
            manager.UserId, roleId, Guid.Empty));
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM dbo.RolePermissions WHERE RoleId=@role AND PermissionKey=N'Catalog.Customers.Read' AND CreatedByUserId=@actor;",
            manager.UserId, roleId, Guid.Empty));
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT(*) FROM dbo.SecurityAuditEvents WHERE TargetType=N'Role' AND TargetId=@role AND ActorUserId=@actor AND Operation=N'PermissionImport';",
            manager.UserId, roleId, Guid.Empty));
        Assert.Equal(3L, await ScalarAsync("SELECT COUNT(*) FROM dbo.SecurityAuditEvents WHERE TargetType=N'Role' AND TargetId=@role AND ActorUserId=@actor;",
            manager.UserId, roleId, Guid.Empty));
        Assert.Equal(3L, await ScalarAsync("SELECT COUNT_BIG(*) FROM dbo.SecurityAuditEvents WHERE TargetType=N'Role' AND TargetId=@role;",
            manager.UserId, roleId, Guid.Empty));
        using var stillDenied = await gateway.RequestAsync(HttpMethod.Get, path + "users", manager.Token);
        Assert.Equal(403, stillDenied.Status);
    }

    [Fact]
    public async Task UserManagerDirectDenyAndImportAllowPreserveIndependentGrantsAndAudit()
    {
        Assert.Equal("approved-container-only", Required("VPS04A_CI_EPHEMERAL_SQL"));
        Assert.Equal("approved-nonproduction-only", Required("VPS04A_LIVE_ALLOW_MUTATION"));
        Assert.Equal("approved-ci-only", Required("VPS04A_CI_FAULT_APPROVED"));
        Assert.Equal(Origin, Required("MYDMSVN_WEB_ORIGIN"));
        Assert.Equal("web5", Required("MYDMSVN_WEB_OPERATION_MODE"));
        Assert.Equal("enabled", Required("MYDMSVN_WEB_DESKTOP_API"));

        using var gateway = new Gateway();
        var manager = await gateway.LoginAsync("vps106b_ci_user");
        const string path = "api/v1/security/";
        using (var roles = await gateway.RequestAsync(HttpMethod.Get, path + "roles", manager.Token))
            Assert.Equal(403, roles.Status);
        using (var catalog = await gateway.RequestAsync(HttpMethod.Get, path + "permissions/catalog", manager.Token))
            Assert.Equal(403, catalog.Status);
        using var users = await gateway.RequestAsync(HttpMethod.Get, path + "users", manager.Token);
        Assert.Equal(200, users.Status);
        var subject = Assert.Single(users.Data.EnumerateArray(),
            row => row.GetProperty("username").GetString() == "vps106b_ci_subject");
        var targetId = subject.GetProperty("userId").GetInt32();
        var revision = subject.GetProperty("assignmentRevision").GetInt64();
        Assert.NotEqual(manager.UserId, targetId);
        Assert.Equal(0L, await ScalarAsync("SELECT COUNT_BIG(*) FROM dbo.UserPermissions WHERE UserId=@role;",
            manager.UserId, targetId, Guid.Empty));
        Assert.Equal(0L, await ScalarAsync("SELECT COUNT_BIG(*) FROM dbo.SecurityAuditEvents WHERE TargetType=N'User' AND TargetId=@role;",
            manager.UserId, targetId, Guid.Empty));

        var correlation = Guid.NewGuid();
        using (var result = await gateway.RequestAsync(HttpMethod.Post, path + "mutations", manager.Token,
            new { target = 0, operation = 5, targetId, expectedRevision = revision,
                permissionKey = "Catalog.Products.Read", decision = false, correlationId = correlation }))
        {
            Assert.Equal(200, result.Status);
            Assert.Equal(revision + 1, result.Data.GetProperty("assignmentRevision").GetInt64());
            Assert.True(result.Data.GetProperty("changed").GetBoolean());
        }
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT_BIG(*) FROM dbo.UserPermissions WHERE UserId=@role AND PermissionKey=N'Catalog.Products.Read' AND Granted=0 AND CreatedByUserId=@actor;",
            manager.UserId, targetId, correlation));
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT_BIG(*) FROM dbo.SecurityAuditEvents WHERE TargetType=N'User' AND TargetId=@role AND ActorUserId=@actor AND Operation=N'SetPermission' AND CorrelationId=@correlation AND AfterState=N'Permission=Catalog.Products.Read;Decision=Deny';",
            manager.UserId, targetId, correlation));

        var json = JsonSerializer.Serialize(new { schemaVersion = 1, targetType = "User", targetId,
            entries = new[] { new { permissionKey = "Catalog.Customers.Read", decision = "Allow" } } });
        using var preview = await gateway.RequestAsync(HttpMethod.Post, path + "import/preview", manager.Token,
            new { targetType = 0, targetId, json });
        Assert.Equal(200, preview.Status);
        var expectedTargetRevision = preview.Data.GetProperty("targetRevision").GetInt64();
        var expectedCatalogDigest = preview.Data.GetProperty("catalogDigest").GetString();
        Assert.Equal(revision + 1, expectedTargetRevision);
        Assert.False(string.IsNullOrWhiteSpace(expectedCatalogDigest));
        Assert.Single(preview.Data.GetProperty("additions").EnumerateArray());
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT_BIG(*) FROM dbo.SecurityAuditEvents WHERE TargetType=N'User' AND TargetId=@role;",
            manager.UserId, targetId, Guid.Empty));

        var body = new { targetType = 0, targetId, json, expectedTargetRevision, expectedCatalogDigest };
        using (var applied = await gateway.RequestAsync(HttpMethod.Post, path + "import/apply", manager.Token, body))
        {
            Assert.Equal(200, applied.Status);
            Assert.Equal(revision + 2, applied.Data.GetProperty("targetRevision").GetInt64());
            Assert.Equal(1, applied.Data.GetProperty("addedCount").GetInt32());
        }
        using (var stale = await gateway.RequestAsync(HttpMethod.Post, path + "import/apply", manager.Token, body))
            Assert.Equal(409, stale.Status);
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT_BIG(*) FROM dbo.UserPermissions WHERE UserId=@role AND PermissionKey=N'Catalog.Customers.Read' AND Granted=1 AND CreatedByUserId=@actor;",
            manager.UserId, targetId, Guid.Empty));
        Assert.Equal(1L, await ScalarAsync("SELECT COUNT_BIG(*) FROM dbo.SecurityAuditEvents WHERE TargetType=N'User' AND TargetId=@role AND ActorUserId=@actor AND Operation=N'PermissionImport' AND AfterState=N'Permission=Catalog.Customers.Read;Decision=Allow';",
            manager.UserId, targetId, Guid.Empty));
        Assert.Equal(2L, await ScalarAsync("SELECT COUNT_BIG(*) FROM dbo.SecurityAuditEvents WHERE TargetType=N'User' AND TargetId=@role;",
            manager.UserId, targetId, Guid.Empty));
        var loggedIn = await gateway.LoginAsync("vps106b_ci_subject");
        Assert.Equal(targetId, loggedIn.UserId);
        using var allow = await gateway.RequestAsync(HttpMethod.Get, path + "permissions/check?key=Catalog.Customers.Read", loggedIn.Token);
        using var deny = await gateway.RequestAsync(HttpMethod.Get, path + "permissions/check?key=Catalog.Products.Read", loggedIn.Token);
        Assert.Equal(200, allow.Status);
        Assert.Equal(200, deny.Status);
        Assert.True(allow.Data.GetProperty("isAllowed").GetBoolean());
        Assert.False(deny.Data.GetProperty("isAllowed").GetBoolean());
    }
}
#endif
