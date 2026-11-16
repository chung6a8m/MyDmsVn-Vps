using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using MyDmsVn.Server.DbMigrator;
using MyDmsVn.Testing.Sql;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using HttpScenario = MyDmsVn.Server.Infrastructure.IntegrationTests.WebAuthHttpSqlTests.Scenario;

namespace MyDmsVn.Server.Infrastructure.IntegrationTests;

public sealed class Vps02DesktopApiHttpSqlTests
{
    [SqlServerTheory(SqlTestCategory.SecurityProvisioning)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public Task Restricted_SQL_pipeline_serves_all_catalog_CRUD_list_get_lookup_and_active(int schema) => Owned(schema, async test =>
    {
        await test.Admin.ExecuteAsync(
            "INSERT dbo.UserPermissions(UserId,PermissionKey,Granted) VALUES" +
            "(@actor,N'Catalog.Products.Read',1),(@actor,N'Catalog.Products.Write',1)," +
            "(@actor,N'Catalog.Warehouses.Read',1),(@actor,N'Catalog.Warehouses.Write',1)," +
            "(@actor,N'Catalog.Employees.Read',1),(@actor,N'Catalog.Employees.Write',1)," +
            "(@actor,N'Catalog.Customers.Read',1),(@actor,N'Catalog.Customers.Write',1);",
            new { actor = test.Alice });
        var login = await test.Login("alice");
        foreach (var catalog in new[]
        {
            new { Route = "products", Table = "Products", Key = "ProductId", Body = "{\"code\":\"VPS02-P\",\"name\":\"HTTP Product\",\"unit\":\"EA\"}" },
            new { Route = "warehouses", Table = "Warehouses", Key = "WarehouseId", Body = "{\"code\":\"VPS02-W\",\"name\":\"HTTP Warehouse\",\"address\":null}" },
            new { Route = "employees", Table = "Employees", Key = "EmployeeId", Body = "{\"code\":\"VPS02-E\",\"name\":\"HTTP Employee\",\"phone\":null,\"userId\":null}" },
            new { Route = "customers", Table = "Customers", Key = "CustomerId", Body = "{\"code\":\"VPS02-C\",\"name\":\"HTTP Customer\",\"address\":null,\"phone\":null,\"taxCode\":null}" },
        })
        {
            var otherBody = JObject.Parse(catalog.Body);
            otherBody["code"] = "ZZZ-" + catalog.Route.Substring(0, 1).ToUpperInvariant();
            otherBody["name"] = "Other " + catalog.Route;
            var created = await test.Request(HttpMethod.Post, "catalog/" + catalog.Route,
                catalog.Body, null, login.Access);
            Assert.Equal(200, created.Status);
            var id = (int)created.Json["data"]!["id"]!;
            Assert.Equal(200, (await test.Request(HttpMethod.Post, "catalog/" + catalog.Route,
                otherBody.ToString(Formatting.None), null, login.Access)).Status);
            var fetched = await test.Get("catalog/" + catalog.Route + "/" + id, login.Access);
            Assert.Equal(200, fetched.Status);
            Assert.Equal(id, (int)fetched.Json["data"]!["id"]!);
            Assert.StartsWith("HTTP ", (string)fetched.Json["data"]!["name"]!);
            var filtered = await test.Get("catalog/" + catalog.Route + "?search=VPS02&pageNumber=1&pageSize=25&includeInactive=false", login.Access);
            AssertPage(filtered, 1, 25, 1, id);
            var firstPage = await test.Get("catalog/" + catalog.Route + "?pageNumber=1&pageSize=1&includeInactive=true", login.Access);
            var secondPage = await test.Get("catalog/" + catalog.Route + "?pageNumber=2&pageSize=1&includeInactive=true", login.Access);
            AssertPage(firstPage, 1, 1, 2, id);
            AssertPage(secondPage, 2, 1, 2, (int)secondPage.Json["data"]!["items"]![0]!["id"]!);
            Assert.NotEqual(id, (int)secondPage.Json["data"]!["items"]![0]!["id"]!);
            var lookup = await test.Get("catalog/" + catalog.Route + "/lookup?search=VPS02&limit=25", login.Access);
            Assert.Equal(id, (int)Assert.Single((JArray)lookup.Json["data"]!)["id"]!);
            var updated = await test.Request(HttpMethod.Put, "catalog/" + catalog.Route + "/" + id,
                catalog.Body.Replace("HTTP ", "Updated "), null, login.Access);
            Assert.Equal(200, updated.Status);
            Assert.StartsWith("Updated ", (string)updated.Json["data"]!["name"]!);
            Assert.Equal(200, (await test.Request(HttpMethod.Put, "catalog/" + catalog.Route + "/" + id + "/active",
                "{\"isActive\":false}", null, login.Access)).Status);
            Assert.False(await test.Admin.QuerySingleAsync<bool>(
                "SELECT IsActive FROM dbo." + catalog.Table + " WHERE " + catalog.Key + "=@id;", new { id }));
            AssertPage(await test.Get("catalog/" + catalog.Route + "?search=VPS02&pageNumber=1&pageSize=25&includeInactive=false", login.Access), 1, 25, 0);
            var inactive = await test.Get("catalog/" + catalog.Route + "?search=VPS02&pageNumber=1&pageSize=25&includeInactive=true", login.Access);
            AssertPage(inactive, 1, 25, 1, id);
            Assert.False((bool)inactive.Json["data"]!["items"]![0]!["isActive"]!);
        }

        var raceBody = "{\"code\":\"VPS02-RACE\",\"name\":\"Race Product\",\"unit\":\"EA\"}";
        var race = await Task.WhenAll(
            test.Request(HttpMethod.Post, "catalog/products", raceBody, null, login.Access),
            test.Request(HttpMethod.Post, "catalog/products", raceBody, null, login.Access));
        Assert.Single(race, reply => reply.Status == 200);
        Assert.Single(race, reply => reply.Status == 409);
        Assert.Equal(1, await test.Admin.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.Products WHERE Code=N'VPS02-RACE';"));
    });

    [SqlServerTheory(SqlTestCategory.SecurityProvisioning)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public Task Restricted_SQL_pipeline_serves_security_permissions_mutation_import_conflict_rollback_and_audit(int schema) => Owned(schema, async test =>
    {
        await test.Admin.ExecuteAsync(
            "INSERT dbo.UserPermissions(UserId,PermissionKey,Granted) VALUES" +
            "(@actor,N'Security.Users.Manage',1),(@actor,N'Security.Roles.Manage',1)," +
            "(@actor,N'Security.Permissions.Read',1);", new { actor = test.Alice });
        var alice = await test.Login("alice");
        var bob = await test.Login("bob");

        var allowed = await test.Get("security/permissions/check?key=Security.Users.Manage", alice.Access);
        var deniedDecision = await test.Get("security/permissions/check?key=Security.Users.Manage", bob.Access);
        Assert.Equal("Security.Users.Manage", (string)allowed.Json["data"]!["permissionKey"]!);
        Assert.True((bool)allowed.Json["data"]!["isAllowed"]!);
        Assert.False((bool)deniedDecision.Json["data"]!["isAllowed"]!);
        var catalog = await test.Get("security/permissions/catalog", alice.Access);
        Assert.Contains(catalog.Json["data"]!["items"]!, item =>
            (string?)item["key"] == "Security.Users.Manage" && (string?)item["status"] == "Active");
        var users = await test.Get("security/users", alice.Access);
        Assert.Contains(users.Json["data"]!, user => (string?)user["username"] == "alice");
        Assert.Contains(users.Json["data"]!, user => (string?)user["username"] == "bob");
        Assert.Equal(200, (await test.Get("security/roles", alice.Access)).Status);
        Assert.Equal(403, (await test.Get("security/roles", bob.Access)).Status);

        var before = await test.Admin.QuerySingleAsync<int>("SELECT COUNT(*) FROM dbo.Roles;");
        var invalid = await test.Request(HttpMethod.Post, "security/mutations",
            "{\"target\":1,\"operation\":0,\"name\":\"Invalid\",\"isActive\":\"true\"}", null, alice.Access);
        Assert.Equal(400, invalid.Status);
        Assert.Equal(before, await test.Admin.QuerySingleAsync<int>("SELECT COUNT(*) FROM dbo.Roles;"));

        var created = await test.Request(HttpMethod.Post, "security/mutations",
            "{\"target\":1,\"operation\":0,\"name\":\"VPS02 HTTP Role\"}", null, alice.Access);
        Assert.Equal(200, created.Status);
        var roleId = await test.Admin.QuerySingleAsync<int>(
            "SELECT RoleId FROM dbo.Roles WHERE NormalizedRoleName=N'VPS02 HTTP ROLE';");
        var roleRevision = (long)created.Json["data"]!["assignmentRevision"]!;
        var roles = await test.Get("security/roles", alice.Access);
        Assert.Contains(roles.Json["data"]!, role => (int?)role["roleId"] == roleId &&
            (string?)role["name"] == "VPS02 HTTP Role" && (long?)role["assignmentRevision"] == roleRevision);

        var deniedMutation = await test.Request(HttpMethod.Post, "security/mutations",
            "{\"target\":1,\"operation\":0,\"name\":\"Denied role\"}", null, bob.Access);
        Assert.Equal(403, deniedMutation.Status);
        Assert.Equal(0, await test.Admin.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.Roles WHERE NormalizedRoleName=N'DENIED ROLE';"));

        var stale = await test.Request(HttpMethod.Post, "security/mutations",
            new JObject { ["target"] = 1, ["operation"] = 2, ["targetId"] = roleId,
                ["isActive"] = false, ["expectedRevision"] = roleRevision + 1 }.ToString(Formatting.None), null, alice.Access);
        Assert.Equal(409, stale.Status);
        Assert.True(await test.Admin.QuerySingleAsync<bool>("SELECT IsActive FROM dbo.Roles WHERE RoleId=@roleId;", new { roleId }));

        var importJson = ImportJson(roleId);
        var preview = await test.Request(HttpMethod.Post, "security/import/preview",
            new JObject { ["targetType"] = 1, ["targetId"] = roleId, ["json"] = importJson }.ToString(Formatting.None), null, alice.Access);
        Assert.Equal(200, preview.Status);
        Assert.Equal(0, await test.Admin.QuerySingleAsync<int>("SELECT COUNT(*) FROM dbo.RolePermissions WHERE RoleId=@roleId;", new { roleId }));
        var previewData = preview.Json["data"]!;
        var applyBody = new JObject { ["targetType"] = 1, ["targetId"] = roleId, ["json"] = importJson,
            ["expectedTargetRevision"] = previewData["targetRevision"]!.DeepClone(),
            ["expectedCatalogDigest"] = previewData["catalogDigest"]!.DeepClone() };
        Assert.Equal(403, (await test.Request(HttpMethod.Post, "security/import/preview",
            new JObject { ["targetType"] = 1, ["targetId"] = roleId, ["json"] = importJson }.ToString(Formatting.None), null, bob.Access)).Status);
        Assert.Equal(403, (await test.Request(HttpMethod.Post, "security/import/apply",
            applyBody.ToString(Formatting.None), null, bob.Access)).Status);
        Assert.Equal(0, await test.Admin.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.RolePermissions WHERE RoleId=@roleId;", new { roleId }));
        var concurrent = await Task.WhenAll(
            test.Request(HttpMethod.Post, "security/import/apply", applyBody.ToString(Formatting.None), null, alice.Access),
            test.Request(HttpMethod.Post, "security/import/apply", applyBody.ToString(Formatting.None), null, alice.Access));
        Assert.Single(concurrent, reply => reply.Status == 200);
        Assert.Single(concurrent, reply => reply.Status == 409);
        Assert.Equal(1, await test.Admin.QuerySingleAsync<int>("SELECT COUNT(*) FROM dbo.RolePermissions WHERE RoleId=@roleId;", new { roleId }));
        Assert.Equal(1, await test.Admin.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.SecurityAuditEvents WHERE TargetType=N'Role' AND TargetId=@roleId AND Operation=N'PermissionImport';", new { roleId }));

        var rollbackRole = await test.Admin.QuerySingleAsync<int>(
            "INSERT dbo.Roles(RoleName,NormalizedRoleName) OUTPUT INSERTED.RoleId VALUES(N'Rollback role',N'ROLLBACK ROLE');");
        var rollbackJson = ImportJson(rollbackRole);
        var rollbackPreview = await test.Request(HttpMethod.Post, "security/import/preview",
            new JObject { ["targetType"] = 1, ["targetId"] = rollbackRole, ["json"] = rollbackJson }.ToString(Formatting.None), null, alice.Access);
        var rollbackData = rollbackPreview.Json["data"]!;
        var rollbackRevision = await test.Admin.QuerySingleAsync<long>(
            "SELECT AssignmentRevision FROM dbo.Roles WHERE RoleId=@rollbackRole;", new { rollbackRole });
        var rollbackAudit = await test.Admin.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.SecurityAuditEvents WHERE TargetType=N'Role' AND TargetId=@rollbackRole;", new { rollbackRole });
        await test.Admin.ExecuteAsync("CREATE TRIGGER dbo.Vps02RejectAudit ON dbo.SecurityAuditEvents AFTER INSERT AS THROW 51998,'Injected VPS-02 audit failure.',1;");
        var rollback = await test.Request(HttpMethod.Post, "security/import/apply",
            new JObject { ["targetType"] = 1, ["targetId"] = rollbackRole, ["json"] = rollbackJson,
                ["expectedTargetRevision"] = rollbackData["targetRevision"]!.DeepClone(),
                ["expectedCatalogDigest"] = rollbackData["catalogDigest"]!.DeepClone() }.ToString(Formatting.None), null, alice.Access);
        Assert.Equal(500, rollback.Status);
        Assert.DoesNotContain("Injected VPS-02 audit failure", rollback.Body);
        Assert.Equal(0, await test.Admin.QuerySingleAsync<int>("SELECT COUNT(*) FROM dbo.RolePermissions WHERE RoleId=@rollbackRole;", new { rollbackRole }));
        Assert.Equal(rollbackRevision, await test.Admin.QuerySingleAsync<long>(
            "SELECT AssignmentRevision FROM dbo.Roles WHERE RoleId=@rollbackRole;", new { rollbackRole }));
        Assert.Equal(rollbackAudit, await test.Admin.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.SecurityAuditEvents WHERE TargetType=N'Role' AND TargetId=@rollbackRole;", new { rollbackRole }));
        await test.Admin.ExecuteAsync("DROP TRIGGER dbo.Vps02RejectAudit;");
        Assert.Equal(1, await test.Admin.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.SecurityAuditEvents WHERE TargetType=N'Role' AND TargetId=@roleId AND Operation=N'PermissionImport';", new { roleId }));
    });

    [SqlServerFact(SqlTestCategory.SecurityProvisioning)]
    public async Task Disabled_mode_rejects_new_routes_while_original_SQL_backed_routes_continue()
    {
        var database = await SqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(SqlTestDatabase.ConnectionStringEnvironmentVariable)!);
        try
        {
            Assert.True(FrozenWeb11Migration.Install(database.ConnectionString).Successful);
            using var test = await HttpScenario.Create(database.ConnectionString);
            await test.Admin.ExecuteAsync("INSERT dbo.UserPermissions(UserId,PermissionKey,Granted) VALUES(@actor,N'Catalog.Products.Read',1);", new { actor = test.Alice });
            var login = await test.Login("alice");
            Assert.Equal(404, (await test.Get("catalog/products", login.Access)).Status);
            Assert.Equal(200, (await test.Get("catalog/products/lookup?limit=25", login.Access)).Status);
        }
        finally { await database.DisposeAsync(); }
    }

    private static async Task Owned(int schema, Func<HttpScenario, Task> body)
    {
        var database = await SqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(SqlTestDatabase.ConnectionStringEnvironmentVariable)!);
        Exception? failure = null;
        try
        {
            var migration = schema switch
            {
                11 => FrozenWeb11Migration.Install(database.ConnectionString),
                12 => new DatabaseMigrationRunner().MigrateWeb(database.ConnectionString),
                13 => new DatabaseMigrationRunner().MigrateWeb5(database.ConnectionString),
                _ => throw new ArgumentOutOfRangeException(nameof(schema)),
            };
            Assert.True(migration.Successful);
            using var scenario = await HttpScenario.Create(database.ConnectionString,
                web4Enabled: schema >= 12, web5Enabled: schema >= 13, desktopApiEnabled: true);
            await body(scenario);
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            try { await database.DisposeAsync(); }
            catch (Exception cleanup) when (failure != null) { throw new AggregateException(failure, cleanup); }
        }
    }

    private static string ImportJson(int roleId) => new JObject
    {
        ["schemaVersion"] = 1, ["targetType"] = "Role", ["targetId"] = roleId,
        ["entries"] = new JArray(new JObject
            { ["permissionKey"] = "Catalog.Products.Read", ["decision"] = "Allow" })
    }.ToString(Formatting.None);

    private static void AssertPage(WebAuthHttpSqlTests.Reply reply, int pageNumber, int pageSize,
        long totalCount, params int[] ids)
    {
        Assert.Equal(200, reply.Status);
        var data = reply.Json["data"]!;
        Assert.Equal(pageNumber, (int)data["pageNumber"]!);
        Assert.Equal(pageSize, (int)data["pageSize"]!);
        Assert.Equal(totalCount, (long)data["totalCount"]!);
        Assert.Equal(ids, data["items"]!.Select(item => (int)item["id"]!).ToArray());
    }
}
