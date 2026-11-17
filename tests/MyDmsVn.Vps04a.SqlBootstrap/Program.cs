using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using MyDmsVn.Server.Application.Identity;
using MyDmsVn.Server.DbMigrator;
using MyDmsVn.Server.Infrastructure.Identity;
using MyDmsVn.Server.Infrastructure.Operations;

namespace MyDmsVn.Vps04a.SqlBootstrap;

// Disposable CI SQL preparation, never a production migrator or a reusable
// database setup entry point. Reuses the exact DbUp runner and scripts 001-013.
internal static class Program
{
    private const string Database = "MyDmsVnVps_Ci";
    private const string RuntimeLogin = "vps04a_ci_runtime";
    private const string OracleLogin = "vps04a_ci_oracle";
    private const string MigrationUser = "vps04a_ci_migration";
    private const string Writer = "vps04a_ci_writer";
    private const string Denied = "vps04a_ci_denied";
    private static string _stage = "admission";
    private static int _sqlErrorNumber;
    private static string _sqlInnerType = "none";

    private static string Require(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value : throw new InvalidOperationException("Missing protected CI configuration.");

    private static void Admit(SqlConnectionStringBuilder master)
    {
        if (Environment.GetEnvironmentVariable("CI") != "true" ||
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
            Environment.GetEnvironmentVariable("VPS04A_CI_EPHEMERAL_SQL") != "approved-container-only")
            throw new InvalidOperationException("Not an admitted ephemeral CI run.");
        if (!string.Equals(master.DataSource, "localhost,1433", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(master.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(master.UserID, "sa", StringComparison.OrdinalIgnoreCase) ||
            master.IntegratedSecurity || master.TrustServerCertificate ||
            master.Encrypt.ToString() != "True" || !Directory.Exists("deployment/sql") ||
            !File.Exists("src/MyDmsVn.Server.DbMigrator/Scripts/013_WebOperationTerminalResolution.sql"))
            throw new InvalidOperationException("CI source, SQL TLS, or loopback boundary not admitted.");
    }

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length != 1 ||
                (args[0] != "provision-isolated-ci" &&
                 args[0] != "arm-ledger-fault" &&
                 args[0] != "disarm-ledger-fault"))
                throw new InvalidOperationException("No operator or production mode is available.");
            // The CLI runs under the pinned .NET10 host SDK directory, but all
            // source SQL scripts must be loaded from the exact checked-out repo.
            Directory.SetCurrentDirectory(Require("GITHUB_WORKSPACE"));
            var admin = new SqlConnectionStringBuilder(Require("VPS04A_CI_SQL_ADMIN_CONNECTION_STRING"));
            Admit(admin);
            if (args[0] != "provision-isolated-ci")
            {
                _stage = "fault-injection-maintenance";
                return await RunEphemeralFaultMaintenanceAsync(admin, args[0]);
            }
            var runtimePassword = Password("VPS04A_CI_SQL_RUNTIME_PASSWORD");
            var oraclePassword = Password("VPS04A_CI_SQL_ORACLE_PASSWORD");
            var writerPassword = Password("VPS04A_LIVE_PASSWORD");
            var deniedPassword = Password("VPS04A_LIVE_DENIED_PASSWORD");

            _stage = "sql-readiness";
            await WaitForSqlAsync(admin.ConnectionString);
            _stage = "create-owned-database";
            using (var connection = new SqlConnection(admin.ConnectionString))
            {
                await connection.OpenAsync();
                using var command = new SqlCommand(
                    "IF DB_ID(N'" + Database + "') IS NOT NULL THROW 51450, 'Refuse existing database.',1; " +
                    "CREATE DATABASE [" + Database + "];", connection);
                command.CommandTimeout = 60;
                await command.ExecuteNonQueryAsync();
            }

            var maintenance = new SqlConnectionStringBuilder(admin.ConnectionString)
            {
                InitialCatalog = Database, Pooling = false
            }.ConnectionString;
            _stage = "original-dbup-exact13";
            var upgrade = new DatabaseMigrationRunner().MigrateWeb5(maintenance);
            if (!upgrade.Successful || upgrade.Scripts.Count() != 13)
                throw new InvalidOperationException("Original 13 migration scripts not applied.");

            _stage = "schema-fingerprint";
            var compatible = await new SqlWeb5SchemaCompatibilityChecker(maintenance)
                .CheckAsync(CancellationToken.None);
            if (!compatible.IsCompatible)
                throw new InvalidOperationException("Exact schema13 fingerprint not verified.");

            _stage = "create-isolated-logins";
            using (var connection = new SqlConnection(admin.ConnectionString))
            {
                await connection.OpenAsync();
                await ExecuteAsync(connection,
                    "CREATE LOGIN [" + RuntimeLogin + "] WITH PASSWORD='" + runtimePassword + "',CHECK_POLICY=ON; " +
                    "CREATE LOGIN [" + OracleLogin + "] WITH PASSWORD='" + oraclePassword + "',CHECK_POLICY=ON;");
            }
            _stage = "users-and-test-seeds";
            var ids = await SeedAsync(maintenance, writerPassword, deniedPassword);
            _stage = "reviewed-restricted-grants";
            using (var connection = new SqlConnection(maintenance))
            {
                await connection.OpenAsync();
                await ExecuteAsync(connection,
                    "CREATE USER [" + RuntimeLogin + "] FOR LOGIN [" + RuntimeLogin + "]; " +
                    "CREATE USER [" + OracleLogin + "] FOR LOGIN [" + OracleLogin + "]; " +
                    "CREATE USER [" + MigrationUser + "] WITHOUT LOGIN;");

                var baseGrants = File.ReadAllText("deployment/sql/Provision-MyDmsVnWebPrincipals.sql")
                    .Replace("$(WebRuntimeUser)", RuntimeLogin, StringComparison.Ordinal)
                    .Replace("$(MigrationUser)", MigrationUser, StringComparison.Ordinal);
                await ExecuteAsync(connection, baseGrants);
                var desktopGrants = File.ReadAllText("deployment/sql/Provision-MyDmsVnDesktopApiGrants.sql")
                    .Replace("$(WebRuntimeUser)", RuntimeLogin, StringComparison.Ordinal);
                await ExecuteAsync(connection, desktopGrants);

                await ExecuteAsync(connection, "GRANT SELECT ON dbo.WebAuthSessions TO [" + OracleLogin + "]; " +
                    "GRANT SELECT ON dbo.WebOperationJournal TO [" + OracleLogin + "]; " +
                    "GRANT SELECT ON dbo.SalesReceipts TO [" + OracleLogin + "]; " +
                    "GRANT SELECT ON dbo.SalesReceiptLines TO [" + OracleLogin + "]; " +
                    "GRANT SELECT ON dbo.GoodsReceipts TO [" + OracleLogin + "]; " +
                    "GRANT SELECT ON dbo.GoodsReceiptLines TO [" + OracleLogin + "]; " +
                    "GRANT SELECT ON dbo.StockLedger TO [" + OracleLogin + "]; " +
                    "GRANT SELECT ON dbo.StockValuationEntries TO [" + OracleLogin + "]; " +
                    "GRANT SELECT ON dbo.StockBalances TO [" + OracleLogin + "];");
            }

            var runtime = new SqlConnectionStringBuilder(admin.ConnectionString)
            {
                InitialCatalog = Database, UserID = RuntimeLogin, Password = runtimePassword, Pooling = false
            }.ConnectionString;
            var oracle = new SqlConnectionStringBuilder(admin.ConnectionString)
            {
                InitialCatalog = Database, UserID = OracleLogin, Password = oraclePassword, Pooling = false
            }.ConnectionString;
            _stage = "independent-runtime-principal-admission";
            try
            {
                await new SqlWebRuntimePrincipalValidator(
                    requireJournal: true, requireResolution: true, requireDesktopApi: true)
                    .ValidateAsync(runtime, CancellationToken.None);
            }
            catch (DesktopStartupException error) when (error.Code == "Security.WebRuntimePrincipalOverprivileged")
            {
                _stage = "runtime-overpriv:" + await DiagnoseOverprivilegeAsync(runtime);
                throw;
            }
            _stage = "superuser-binding";
            await new SqlSuperuserBindingValidator()
                .ValidateAsync(runtime, Writer, CancellationToken.None);

            _stage = "independent-read-only-oracle-admission";
            using (var connection = new SqlConnection(oracle))
            {
                await connection.OpenAsync();
                using var probe = new SqlCommand(@"
SELECT CASE WHEN
  ISNULL(HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','CONTROL'),0)<>0 OR
  ISNULL(IS_ROLEMEMBER('db_owner'),0)<>0 OR
  ISNULL(IS_ROLEMEMBER('db_datawriter'),0)<>0 OR
  ISNULL(HAS_PERMS_BY_NAME('dbo.Users','OBJECT','SELECT'),0)<>0 OR
  ISNULL(HAS_PERMS_BY_NAME('dbo.SalesReceipts','OBJECT','INSERT'),0)<>0 OR
  ISNULL(HAS_PERMS_BY_NAME('dbo.SalesReceipts','OBJECT','UPDATE'),0)<>0 OR
  ISNULL(HAS_PERMS_BY_NAME('dbo.SalesReceipts','OBJECT','DELETE'),0)<>0 OR
  ISNULL(HAS_PERMS_BY_NAME('dbo.WebOperationJournal','OBJECT','INSERT'),0)<>0 OR
  ISNULL(HAS_PERMS_BY_NAME('dbo.WebAuthSessions','OBJECT','SELECT'),0)<>1 OR
  ISNULL(HAS_PERMS_BY_NAME('dbo.WebOperationJournal','OBJECT','SELECT'),0)<>1 OR
  ISNULL(HAS_PERMS_BY_NAME('dbo.SalesReceipts','OBJECT','SELECT'),0)<>1 OR
  ISNULL(HAS_PERMS_BY_NAME('dbo.SalesReceiptLines','OBJECT','SELECT'),0)<>1 OR
  ISNULL(HAS_PERMS_BY_NAME('dbo.GoodsReceipts','OBJECT','SELECT'),0)<>1 OR
  ISNULL(HAS_PERMS_BY_NAME('dbo.GoodsReceiptLines','OBJECT','SELECT'),0)<>1 OR
  ISNULL(HAS_PERMS_BY_NAME('dbo.StockLedger','OBJECT','SELECT'),0)<>1 OR
  ISNULL(HAS_PERMS_BY_NAME('dbo.StockValuationEntries','OBJECT','SELECT'),0)<>1 OR
  ISNULL(HAS_PERMS_BY_NAME('dbo.StockBalances','OBJECT','SELECT'),0)<>1 OR
  EXISTS(
    SELECT 1 FROM sys.objects o
    CROSS APPLY sys.fn_my_permissions(
      QUOTENAME(OBJECT_SCHEMA_NAME(o.object_id))+N'.'+QUOTENAME(o.name),N'OBJECT') p
    WHERE o.is_ms_shipped=0 AND p.permission_name IN
      (N'SELECT',N'INSERT',N'UPDATE',N'DELETE',N'EXECUTE',N'ALTER',N'CONTROL')
      AND NOT (p.permission_name=N'SELECT' AND OBJECT_SCHEMA_NAME(o.object_id)=N'dbo'
        AND o.name IN(N'WebAuthSessions',N'WebOperationJournal',N'SalesReceipts',
          N'SalesReceiptLines',N'GoodsReceipts',N'GoodsReceiptLines',
          N'StockLedger',N'StockValuationEntries',N'StockBalances'))
  )
THEN 1 ELSE 0 END;", connection);
                if (Convert.ToInt32(await probe.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 0)
                {
                    _stage = "oracle-denied:" + await DiagnoseOracleAsync(connection);
                    throw new InvalidOperationException("Oracle exceeds the nine explicit CI-only read tables.");
                }
            }

            _stage = "emit-owned-run-configuration";
            var envFile = Require("GITHUB_ENV");
            File.AppendAllLines(envFile, new[]
            {
                "MYDMSVN_WEB_SQL_CONNECTION_STRING=" + runtime,
                "VPS04A_SQL_ORACLE_CONNECTION_STRING=" + oracle,
                "VPS04A_LIVE_CUSTOMER_ID=" + ids.Customer.ToString(CultureInfo.InvariantCulture),
                "VPS04A_LIVE_WAREHOUSE_ID=" + ids.Warehouse.ToString(CultureInfo.InvariantCulture),
                "VPS04A_LIVE_EMPLOYEE_ID=" + ids.Employee.ToString(CultureInfo.InvariantCulture),
                "VPS04A_LIVE_PRODUCT_ID=" + ids.Product.ToString(CultureInfo.InvariantCulture),
            });
            Console.WriteLine("PASS: isolated SQL schema13 (13/13 DbUp), runtime principal, nine-table CI-only read-only oracle and active fixtures.");
            return 0;
        }
        catch (Exception problem)
        {
            // Only fixed/allowlisted failure categories, never exception messages,
            // passwords, connection strings, SQL text or token material.
            var category = problem is DesktopStartupException error
                ? error.Code
                : problem is SqlException sqlError
                    ? "SQL." + sqlError.Number.ToString(CultureInfo.InvariantCulture)
                    : problem.GetType().Name;
            Console.Error.WriteLine("VPS04A-CI bootstrap REFUSED/FAILED at stage: " + _stage +
                "; safe code: " + category +
                "; SQL readiness number: " + _sqlErrorNumber.ToString(CultureInfo.InvariantCulture) +
                "; readiness inner type: " + _sqlInnerType + ". No acceptance is claimed.");
            return 1;
        }
    }

    private static async Task<string> DiagnoseOracleAsync(SqlConnection connection)
    {
        // The only visible output is a truncated database-object identifier and
        // operation name; no SQL text, login, connection string or row contents.
        using var command = new SqlCommand(@"
SELECT TOP 1 CONCAT(OBJECT_SCHEMA_NAME(o.object_id),N'_',o.name,N'_',p.permission_name)
FROM sys.objects o
CROSS APPLY sys.fn_my_permissions(
    QUOTENAME(OBJECT_SCHEMA_NAME(o.object_id))+N'.'+QUOTENAME(o.name),N'OBJECT') p
WHERE o.is_ms_shipped=0 AND p.permission_name IN
  (N'SELECT',N'INSERT',N'UPDATE',N'DELETE',N'EXECUTE',N'ALTER',N'CONTROL')
  AND NOT (p.permission_name=N'SELECT' AND OBJECT_SCHEMA_NAME(o.object_id)=N'dbo'
    AND o.name IN(N'WebAuthSessions',N'WebOperationJournal',N'SalesReceipts',
      N'SalesReceiptLines',N'GoodsReceipts',N'GoodsReceiptLines',
      N'StockLedger',N'StockValuationEntries',N'StockBalances'))
ORDER BY o.name,p.permission_name;", connection);
        var result=Convert.ToString(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(result))
            return "object:" + Regex.Replace(result,"[^A-Za-z0-9_]","").Substring(0,Math.Min(result.Length,80));
        return "non-object-permission";
    }

    // Fault injection is a distinct privileged *CI-only* phase, never a host
    // runtime permission. The trigger is absent during Kestrel startup and
    // removed after a single deliberately failed HTTP Post. All paths are
    // bound to the same ephemeral localhost, fresh disposable SQL database.
    private static async Task<int> RunEphemeralFaultMaintenanceAsync(
        SqlConnectionStringBuilder master, string action)
    {
        if (Environment.GetEnvironmentVariable("VPS04A_CI_FAULT_APPROVED") != "approved-ci-only")
            throw new InvalidOperationException("CI fault-injection gate is not explicitly admitted.");
        var maintenance = new SqlConnectionStringBuilder(master.ConnectionString)
        {
            InitialCatalog = Database,
            Pooling = false,
        }.ConnectionString;

        using var connection = new SqlConnection(maintenance);
        await connection.OpenAsync();

        if (action == "arm-ledger-fault")
        {
            if (!(await new SqlWeb5SchemaCompatibilityChecker(maintenance)
                    .CheckAsync(CancellationToken.None)).IsCompatible)
                throw new InvalidOperationException("Fault injection requires unmodified schema13.");

            if (await ScalarAsync(connection,
                "SELECT COUNT(*) FROM sys.triggers WHERE name=N'TR_Vps04aCiLedgerAbort';") != 0)
                throw new InvalidOperationException("CI fault trigger already present.");

            // Deliberately throws *after* the ledger INSERT; the UoW must roll
            // back the posted header, line COGS, ledger, balance and journal.
            await ExecuteAsync(connection, @"
CREATE TRIGGER dbo.TR_Vps04aCiLedgerAbort ON dbo.StockLedger
AFTER INSERT
AS
BEGIN
  SET NOCOUNT ON;
  IF EXISTS (SELECT 1 FROM inserted WHERE DocumentType = 'SalesReceipt')
    THROW 51488, 'Disposable CI rollback injection.', 1;
END;");
            Console.WriteLine("PASS: ephemeral ledger fault armed after original schema13 preflight.");
            return 0;
        }

        if (action == "disarm-ledger-fault")
        {
            if (await ScalarAsync(connection,
                "SELECT COUNT(*) FROM sys.triggers WHERE name=N'TR_Vps04aCiLedgerAbort';") != 1)
                throw new InvalidOperationException("Expected exactly one CI fault trigger.");
            await ExecuteAsync(connection, "DROP TRIGGER dbo.TR_Vps04aCiLedgerAbort;");
            if (!(await new SqlWeb5SchemaCompatibilityChecker(maintenance)
                    .CheckAsync(CancellationToken.None)).IsCompatible)
                throw new InvalidOperationException("Original schema13 fingerprint not restored.");
            Console.WriteLine("PASS: CI ledger fault removed; exact original schema13 fingerprint restored.");
            return 0;
        }
        throw new InvalidOperationException("Unsupported disposable fault maintenance command.");
    }

    private static async Task<int> ScalarAsync(SqlConnection connection, string sql)
    {
        using var query = new SqlCommand(sql, connection) { CommandTimeout = 30 };
        return Convert.ToInt32(await query.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    // This forensic probe only runs on denied CI admission, never as an alternate
    // authorization decision. Reports an allowlisted category/object name only.
    private static async Task<string> DiagnoseOverprivilegeAsync(string runtime)
    {
        try
        {
            using var connection = new SqlConnection(runtime);
            await connection.OpenAsync();
            async Task<string?> FirstAsync(string sql)
            {
                using var query = new SqlCommand(sql, connection) { CommandTimeout = 10 };
                return Convert.ToString(await query.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            }

            var identity = await FirstAsync("SELECT ORIGINAL_LOGIN();");
            if (identity != RuntimeLogin) return "unexpected-login";

            var database = await FirstAsync(@"
SELECT TOP 1 permission_name FROM sys.fn_my_permissions(NULL,N'DATABASE')
WHERE permission_name NOT IN(N'CONNECT',N'VIEW DEFINITION',
  N'VIEW ANY COLUMN ENCRYPTION KEY DEFINITION',N'VIEW ANY COLUMN MASTER KEY DEFINITION');");
            if (database != null) return "database:" + database;

            var schema = await FirstAsync(@"
SELECT TOP 1 permission_name FROM sys.schemas s
CROSS APPLY sys.fn_my_permissions(QUOTENAME(s.name),N'SCHEMA') p
WHERE p.permission_name IN(N'SELECT',N'INSERT',N'UPDATE',N'DELETE',N'EXECUTE',N'REFERENCES');");
            if (schema != null) return "schema:" + schema;

            var unexpected = await FirstAsync(@"
SELECT TOP 1 CONCAT(o.name,':',p.permission_name) FROM sys.objects o
CROSS APPLY sys.fn_my_permissions(
  QUOTENAME(OBJECT_SCHEMA_NAME(o.object_id))+N'.'+QUOTENAME(o.name),N'OBJECT') p
WHERE o.is_ms_shipped=0 AND p.permission_name IN
  (N'SELECT',N'INSERT',N'UPDATE',N'DELETE',N'EXECUTE',N'REFERENCES',N'ALTER',N'CONTROL',N'TAKE OWNERSHIP')
  AND NOT (OBJECT_SCHEMA_NAME(o.object_id)=N'dbo' AND (
    (p.permission_name=N'SELECT' AND o.name IN
      (N'Users',N'Roles',N'UserRoles',N'RolePermissions',N'UserPermissions',
       N'Products',N'Warehouses',N'Employees',N'Customers',N'GoodsReceipts',
       N'GoodsReceiptLines',N'SalesReceipts',N'SalesReceiptLines',N'StockValuationEntries',
       N'StockLedger',N'StockBalances',N'SchemaVersions',N'SystemSecuritySettings',
       N'WebAuthSessions',N'WebRefreshTokens',N'WebDeploymentState',N'WebOperationJournal',
       N'WebOperationTerminalAudit'))
    OR (p.permission_name=N'INSERT' AND o.name IN
      (N'Products',N'Warehouses',N'Employees',N'Customers',N'GoodsReceipts',
       N'GoodsReceiptLines',N'SalesReceipts',N'SalesReceiptLines',N'StockValuationEntries',
       N'StockLedger',N'StockBalances',N'WebAuthSessions',N'WebRefreshTokens',N'WebOperationJournal'))
    OR (p.permission_name=N'UPDATE' AND o.name IN
      (N'Products',N'Warehouses',N'Employees',N'Customers',N'Users',
       N'GoodsReceipts',N'GoodsReceiptNumberSequence',N'SalesReceipts',N'SalesReceiptLines',
       N'SalesReceiptNumberSequence',N'StockBalances',N'WebAuthSessions',N'WebRefreshTokens'))
    OR (p.permission_name=N'DELETE' AND o.name IN(N'GoodsReceiptLines',N'SalesReceiptLines'))
    OR (p.permission_name=N'EXECUTE' AND o.name IN
      (N'ApplySecurityAdministration',N'ReadSecurityPermissionImportTarget',
       N'ApplySecurityPermissionImport'))
  )) ORDER BY o.name,p.permission_name;");
            if (unexpected != null)
                return "object:" + Regex.Replace(unexpected, "[^A-Za-z0-9_:]", "").Substring(0, Math.Min(unexpected.Length, 90));

            var grant = await FirstAsync(
                "SELECT TOP 1 N'grant-option' FROM sys.database_permissions p " +
                "JOIN sys.user_token t ON t.principal_id=p.grantee_principal_id WHERE p.state='W';");
            if (grant != null) return "grant-option";

            return "other-restricted-grant";
        }
        catch
        {
            return "diagnostic-unavailable";
        }
    }

    private static string Password(string name)
    {
        var password = Require(name);
        if (!Regex.IsMatch(password, @"\A[A-Za-z0-9_@%-]{20,80}\z", RegexOptions.CultureInvariant) ||
            !password.Any(char.IsUpper) || !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit) || !password.Any(c => !char.IsLetterOrDigit(c)))
            throw new InvalidOperationException("Generated CI credential does not meet SQL policy.");
        return password;
    }

    private static async Task WaitForSqlAsync(string connection)
    {
        // Avoid a multi-minute silent stall on TLS or container startup failure.
        for (var attempt = 0; attempt < 18; attempt++)
        {
            try
            {
                using var sql = new SqlConnection(new SqlConnectionStringBuilder(connection)
                    { ConnectTimeout = 3 }.ConnectionString);
                await sql.OpenAsync();
                return;
            }
            catch (SqlException error)
            {
                _sqlErrorNumber = error.Number; // numeric classifier only; no message/connection.
                _sqlInnerType = error.InnerException?.GetType().Name ?? "none";
                if (attempt == 17) break;
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
        throw new InvalidOperationException("Ephemeral SQL Server did not become ready with verified TLS.");
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(int Customer, int Warehouse, int Employee, int Product)> SeedAsync(
        string connectionString, string writerPassword, string deniedPassword)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        var hasher = new BcryptPasswordHasher();
        var writerId = await InsertUserAsync(connection, transaction, Writer, writerPassword, hasher);
        await InsertUserAsync(connection, transaction, Denied, deniedPassword, hasher);

        using (var binding = new SqlCommand(
            "INSERT dbo.SystemSecuritySettings(Id,SuperuserUserId,BoundNormalizedUsername) VALUES (1,@id,@username);",
            connection, transaction))
        {
            binding.Parameters.Add("@id", SqlDbType.Int).Value = writerId;
            binding.Parameters.Add("@username", SqlDbType.NVarChar, 100).Value = UsernameNormalizer.Normalize(Writer);
            await binding.ExecuteNonQueryAsync();
        }

        async Task<int> NewId(string sql, string code)
        {
            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@code", SqlDbType.NVarChar, 32).Value = code;
            return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        var customer = await NewId("INSERT dbo.Customers(Code,Name) OUTPUT INSERTED.CustomerId VALUES (@code,N'CI Customer');", "CI-CUST");
        var warehouse = await NewId("INSERT dbo.Warehouses(Code,Name) OUTPUT INSERTED.WarehouseId VALUES (@code,N'CI Warehouse');", "CI-WH");
        var employee = await NewId("INSERT dbo.Employees(Code,Name) OUTPUT INSERTED.EmployeeId VALUES (@code,N'CI Employee');", "CI-EMP");
        var product = await NewId("INSERT dbo.Products(Code,Name,Unit) OUTPUT INSERTED.ProductId VALUES (@code,N'CI Product',N'EA');", "CI-PROD");
        transaction.Commit();
        return (customer, warehouse, employee, product);
    }

    private static async Task<int> InsertUserAsync(SqlConnection connection, SqlTransaction transaction,
        string username, string password, BcryptPasswordHasher hasher)
    {
        using var command = new SqlCommand(@"
INSERT dbo.Users (Username, NormalizedUsername, DisplayName, Source,
  PasswordHash, PasswordSalt, PasswordAlgorithm, IsActive)
OUTPUT INSERTED.UserId VALUES
  (@name,@normalized,@display,N'Local',@hash,N'',@algorithm,1);", connection, transaction);
        command.Parameters.Add("@name", SqlDbType.NVarChar, 100).Value = username;
        command.Parameters.Add("@normalized", SqlDbType.NVarChar, 100).Value = UsernameNormalizer.Normalize(username);
        command.Parameters.Add("@display", SqlDbType.NVarChar, 100).Value = username;
        command.Parameters.Add("@hash", SqlDbType.NVarChar, 255).Value = hasher.Hash(password);
        command.Parameters.Add("@algorithm", SqlDbType.NVarChar, 32).Value = hasher.Algorithm;
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
}
