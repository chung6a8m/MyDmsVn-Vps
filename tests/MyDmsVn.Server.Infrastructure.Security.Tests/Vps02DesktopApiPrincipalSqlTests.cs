using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using MyDmsVn.Server.DbMigrator;
using MyDmsVn.Server.Infrastructure.Operations;
using MyDmsVn.Testing.Sql;
using Xunit;

namespace MyDmsVn.Server.Infrastructure.IntegrationTests;

/// <summary>
/// VPS-02 SQL oracle. Runs ONLY with MYDMSVN_TEST_SQLSERVER_CONNECTION_STRING
/// targeting an operator-owned nonproduction SQL Server master database.
/// CI discovery does not constitute execution.
/// </summary>
public sealed class Vps02DesktopApiPrincipalSqlTests
{
    private static async Task<SqlTestDatabase> NewDatabaseAsync()
    {
        var database = await SqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(SqlTestDatabase.ConnectionStringEnvironmentVariable)!);
        Assert.True(new DatabaseMigrationRunner().MigrateWeb(database.ConnectionString).Successful);
        return database;
    }

    private static string ReadSql(string file)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "deployment", "sql", file);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(file);
    }

    private static string BaseScript() => ReadSql("Provision-MyDmsVnWebPrincipals.sql")
        .Replace("$(WebRuntimeUser)", "Vps02WebProbe")
        .Replace("$(MigrationUser)", "Vps02MigrationProbe");

    private static string OptinScript() => ReadSql("Provision-MyDmsVnDesktopApiGrants.sql")
        .Replace("$(WebRuntimeUser)", "Vps02WebProbe");

    private static async Task PrepareAsync(SqlConnection connection)
    {
        await connection.ExecuteAsync(
            "CREATE USER [Vps02WebProbe] WITHOUT LOGIN; CREATE USER [Vps02MigrationProbe] WITHOUT LOGIN;");
        await connection.ExecuteAsync(BaseScript());
        await connection.ExecuteAsync(OptinScript());
    }

    [SqlServerFact(SqlTestCategory.SecurityProvisioning)]
    public async Task Optin_rejects_an_unprovisioned_database_user_before_granting_any_role()
    {
        var db = await NewDatabaseAsync();
        try
        {
            using var connection = new SqlConnection(db.ConnectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync("CREATE USER [Vps02WebProbe] WITHOUT LOGIN;");
            var rejected = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(OptinScript()));
            Assert.Equal(51113, rejected.Number);
            Assert.Equal(0, await connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM sys.database_principals WHERE name=N'MyDmsVnDesktopApiRuntimeRole';"));
        }
        finally { await db.DisposeAsync(); }
    }

    [SqlServerFact(SqlTestCategory.SecurityProvisioning)]
    public async Task Explicit_optin_grants_catalog_writes_and_security_execute_but_not_direct_security_DML()
    {
        var db = await NewDatabaseAsync();
        try
        {
            using var connection = new SqlConnection(db.ConnectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "CREATE USER [Vps02WebProbe] WITHOUT LOGIN; CREATE USER [Vps02MigrationProbe] WITHOUT LOGIN;");
            await connection.ExecuteAsync(BaseScript());
            await connection.ExecuteAsync("EXECUTE AS USER=N'Vps02WebProbe';");
            try
            {
                await new SqlWebRuntimePrincipalValidator().ValidateConnectionAsync(connection, CancellationToken.None);
                var missing = await Assert.ThrowsAsync<DesktopStartupException>(() =>
                    new SqlWebRuntimePrincipalValidator(requireDesktopApi: true)
                        .ValidateConnectionAsync(connection, CancellationToken.None));
                Assert.Equal("Security.WebRuntimePrincipalUnderprivileged", missing.Code);
                Assert.Equal(0, await connection.QuerySingleAsync<int>(
                    "SELECT HAS_PERMS_BY_NAME(N'dbo.Products',N'OBJECT',N'INSERT');"));
            }
            finally { await connection.ExecuteAsync("REVERT;"); }

            await connection.ExecuteAsync(OptinScript());
            // Base provisioning MUST remain idempotent when the dedicated opt-in role exists.
            await connection.ExecuteAsync(BaseScript());
            await connection.ExecuteAsync("EXECUTE AS USER=N'Vps02WebProbe';");
            try
            {
                await new SqlWebRuntimePrincipalValidator(requireDesktopApi: true)
                    .ValidateConnectionAsync(connection, CancellationToken.None);
                var unexpected = await Assert.ThrowsAsync<DesktopStartupException>(() =>
                    new SqlWebRuntimePrincipalValidator().ValidateConnectionAsync(connection, CancellationToken.None));
                Assert.Equal("Security.WebRuntimePrincipalOverprivileged", unexpected.Code);

                foreach (var table in new[] { "Products", "Warehouses", "Employees", "Customers" })
                {
                    Assert.Equal(1, await connection.QuerySingleAsync<int>(
                        "SELECT HAS_PERMS_BY_NAME(N'dbo." + table + "',N'OBJECT',N'INSERT');"));
                    Assert.Equal(1, await connection.QuerySingleAsync<int>(
                        "SELECT HAS_PERMS_BY_NAME(N'dbo." + table + "',N'OBJECT',N'UPDATE');"));
                }
                foreach (var procedure in new[] { "ApplySecurityAdministration",
                    "ReadSecurityPermissionImportTarget", "ApplySecurityPermissionImport" })
                {
                    Assert.Equal(1, await connection.QuerySingleAsync<int>(
                        "SELECT HAS_PERMS_BY_NAME(N'dbo." + procedure + "',N'OBJECT',N'EXECUTE');"));
                }

                var id = await connection.QuerySingleAsync<int>(
                    "INSERT dbo.Products(Code,Name,Unit,IsActive) OUTPUT INSERTED.Id " +
                    "VALUES(N'VPS02-PROBE',N'VPS-02 Product',N'EA',1);");
                Assert.True(id > 0);
                await connection.ExecuteAsync("UPDATE dbo.Products SET Name=N'VPS-02 Updated' WHERE Id=@id;", new { id });
                Assert.Equal("VPS-02 Updated", await connection.QuerySingleAsync<string>(
                    "SELECT Name FROM dbo.Products WHERE Id=@id;", new { id }));

                foreach (var statement in new[]
                {
                    "INSERT dbo.Roles(RoleName,NormalizedRoleName) VALUES(N'Bypass',N'BYPASS');",
                    "UPDATE dbo.Users SET IsActive=0;",
                    "DELETE dbo.Products WHERE Id=-1;",
                    "SELECT TOP(1) * FROM dbo.SecurityAuditEvents;",
                    "UPDATE dbo.SchemaVersions SET Applied=SYSUTCDATETIME();",
                    "CREATE TABLE dbo.UnsafeVps02(Id int);"
                })
                    await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(statement));
            }
            finally { await connection.ExecuteAsync("REVERT;"); }
        }
        finally { await db.DisposeAsync(); }
    }

    [SqlServerFact(SqlTestCategory.SecurityProvisioning)]
    public async Task Extra_execute_on_unlisted_procedure_is_rejected_even_when_desktop_role_exists()
    {
        var db = await NewDatabaseAsync();
        try
        {
            using var connection = new SqlConnection(db.ConnectionString);
            await connection.OpenAsync();
            await PrepareAsync(connection);
            await connection.ExecuteAsync("CREATE PROCEDURE dbo.Vps02Unlisted AS SELECT 1 AS V;");
            await connection.ExecuteAsync(
                "GRANT EXECUTE ON OBJECT::dbo.Vps02Unlisted TO [MyDmsVnDesktopApiRuntimeRole];");
            await connection.ExecuteAsync("EXECUTE AS USER=N'Vps02WebProbe';");
            try
            {
                var ex = await Assert.ThrowsAsync<DesktopStartupException>(() =>
                    new SqlWebRuntimePrincipalValidator(requireDesktopApi: true)
                        .ValidateConnectionAsync(connection, CancellationToken.None));
                Assert.Equal("Security.WebRuntimePrincipalOverprivileged", ex.Code);
            }
            finally { await connection.ExecuteAsync("REVERT;"); }
        }
        finally { await db.DisposeAsync(); }
    }

    [SqlServerFact(SqlTestCategory.SecurityProvisioning)]
    public async Task Missing_required_catalog_update_is_rejected_when_optin_role_is_incomplete()
    {
        var db = await NewDatabaseAsync();
        try
        {
            using var connection = new SqlConnection(db.ConnectionString);
            await connection.OpenAsync();
            await PrepareAsync(connection);
            await connection.ExecuteAsync(
                "REVOKE UPDATE ON OBJECT::dbo.Products FROM [MyDmsVnDesktopApiRuntimeRole];");
            await connection.ExecuteAsync("EXECUTE AS USER=N'Vps02WebProbe';");
            try
            {
                var ex = await Assert.ThrowsAsync<DesktopStartupException>(() =>
                    new SqlWebRuntimePrincipalValidator(requireDesktopApi: true)
                        .ValidateConnectionAsync(connection, CancellationToken.None));
                Assert.Equal("Security.WebRuntimePrincipalUnderprivileged", ex.Code);
            }
            finally { await connection.ExecuteAsync("REVERT;"); }
        }
        finally { await db.DisposeAsync(); }
    }
}
