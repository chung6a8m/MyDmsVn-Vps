using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using MyDmsVn.Server.Infrastructure.Identity;
using MyDmsVn.Server.Infrastructure.Operations;

namespace MyDmsVn.LocalSimulation;

internal sealed class SqlLifecycle
{
    private const string InstanceMarker = "MyDmsVn.LocalSimulationInstance";
    private const string SeedMarker = "MyDmsVn.LocalSimulationSeeded";
    private readonly LocalSimulationSettings _settings;

    public SqlLifecycle(LocalSimulationSettings settings) => _settings = settings;

    public async Task ExecuteAsync(string command)
    {
        switch (command)
        {
            case "create": await CreateAsync().ConfigureAwait(false); return;
            case "backup-verify": await BackupVerifyAsync().ConfigureAwait(false); return;
            case "provision": await ProvisionAsync().ConfigureAwait(false); return;
            case "verify": await VerifyAsync().ConfigureAwait(false); return;
            case "verify-exercise": await VerifyExerciseAsync().ConfigureAwait(false); return;
            case "reset-admission": await VerifyMarkerAsync().ConfigureAwait(false); return;
            default: throw new LocalSimulationBlockedException("LocalSimulation.CommandInvalid");
        }
    }

    private async Task VerifyExerciseAsync()
    {
        await VerifyMarkerAsync().ConfigureAwait(false);
        var saleText = Environment.GetEnvironmentVariable("MYDMSVN_LOCAL_SIM_PROOF_SALE_ID");
        var operationText = Environment.GetEnvironmentVariable("MYDMSVN_LOCAL_SIM_PROOF_OPERATION_ID");
        var note = Environment.GetEnvironmentVariable("MYDMSVN_LOCAL_SIM_PROOF_NOTE");
        if (!long.TryParse(saleText, NumberStyles.None, CultureInfo.InvariantCulture, out var saleId) || saleId <= 0 ||
            !Guid.TryParseExact(operationText, "D", out var operationId) ||
            note is null || !System.Text.RegularExpressions.Regex.IsMatch(note, @"\ALOCAL-VERIFY-[0-9a-f]{32}\z"))
            throw new LocalSimulationBlockedException("LocalSimulation.ProofInputInvalid");
        await using var oracle = new SqlConnection(_settings.BuildOracleConnectionString("127.0.0.1,11433").ConnectionString);
        await oracle.OpenAsync().ConfigureAwait(false);
        await using var command = new SqlCommand(@"
SELECT CASE WHEN
 (SELECT COUNT(*) FROM dbo.SalesReceipts WHERE SaleId=@sale AND Note=@note)=1 AND
 (SELECT COUNT(*) FROM dbo.SalesReceiptLines WHERE SaleId=@sale)=1 AND
 (SELECT COUNT(*) FROM dbo.WebOperationJournal WHERE OperationId=@operation AND ResultDocumentId=@sale AND Disposition=N'COMMITTED')=1
THEN 1 ELSE 0 END;", oracle);
        command.Parameters.Add("@sale", SqlDbType.BigInt).Value = saleId;
        command.Parameters.Add("@note", SqlDbType.NVarChar, 500).Value = note;
        command.Parameters.Add("@operation", SqlDbType.UniqueIdentifier).Value = operationId;
        if (Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture) != 1)
            throw new LocalSimulationBlockedException("LocalSimulation.ExerciseProofFailed");
    }

    private async Task BackupVerifyAsync()
    {
        await VerifyMarkerAsync().ConfigureAwait(false);
        await using var master = new SqlConnection(_settings.AdminConnection.ConnectionString);
        await master.OpenAsync().ConfigureAwait(false);
        await using var command = new SqlCommand(@"
BACKUP DATABASE [MyDmsVnVps_LocalSimulation]
TO DISK=N'/var/opt/mssql/backup/local-sim-empty.bak'
WITH INIT,CHECKSUM;
RESTORE VERIFYONLY
FROM DISK=N'/var/opt/mssql/backup/local-sim-empty.bak'
WITH CHECKSUM;", master)
        {
            CommandTimeout = 180,
        };
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private async Task CreateAsync()
    {
        await using var master = new SqlConnection(_settings.AdminConnection.ConnectionString);
        await master.OpenAsync().ConfigureAwait(false);
        var lockResult = await ScalarIntAsync(master, @"
DECLARE @result int;
EXEC @result=sys.sp_getapplock @Resource=N'MyDmsVn.LocalSimulation.DatabaseCreate',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;
SELECT @result;").ConfigureAwait(false);
        if (lockResult < 0) throw new LocalSimulationBlockedException("LocalSimulation.DatabaseCreateBusy");
        var createdHere = false;
        try
        {
            if (await ScalarIntAsync(master, "SELECT CASE WHEN DB_ID(N'MyDmsVnVps_LocalSimulation') IS NULL THEN 1 ELSE 0 END;").ConfigureAwait(false) == 1)
            {
                await ExecuteAsync(master, "CREATE DATABASE [MyDmsVnVps_LocalSimulation];").ConfigureAwait(false);
                createdHere = true;
            }
        }
        finally { await ExecuteAsync(master, "EXEC sys.sp_releaseapplock @Resource=N'MyDmsVn.LocalSimulation.DatabaseCreate',@LockOwner=N'Session';").ConfigureAwait(false); }

        await using var database = new SqlConnection(DatabaseAdminConnection());
        await database.OpenAsync().ConfigureAwait(false);
        var marker = await ReadMarkerAsync(database, InstanceMarker).ConfigureAwait(false);
        if (marker is not null && marker != _settings.InstanceId.ToString("D"))
            throw new LocalSimulationBlockedException("LocalSimulation.DatabaseOwnershipMismatch");
        if (marker is null && !createdHere)
            throw new LocalSimulationBlockedException("LocalSimulation.ExistingDatabaseUnmarked");
        if (marker is null)
            await AddMarkerAsync(database, InstanceMarker, _settings.InstanceId.ToString("D")).ConfigureAwait(false);
        await EnsureMigrationPrincipalAsync(database).ConfigureAwait(false);
    }

    private async Task EnsureMigrationPrincipalAsync(SqlConnection database)
    {
        await using var master = new SqlConnection(_settings.AdminConnection.ConnectionString);
        await master.OpenAsync().ConfigureAwait(false);
        await ExecuteAsync(master, LoginSql(LocalSimulationSqlNames.MigrationLogin,
            _settings.MigrationPassword)).ConfigureAwait(false);
        await ExecuteAsync(database, $@"
IF USER_ID(N'{LocalSimulationSqlNames.MigrationLogin}') IS NULL
    CREATE USER [{LocalSimulationSqlNames.MigrationLogin}] FOR LOGIN [{LocalSimulationSqlNames.MigrationLogin}];
GRANT CONTROL TO [{LocalSimulationSqlNames.MigrationLogin}];").ConfigureAwait(false);
    }

    private async Task ProvisionAsync()
    {
        await VerifyMarkerAsync().ConfigureAwait(false);
        await using var database = new SqlConnection(DatabaseAdminConnection());
        await database.OpenAsync().ConfigureAwait(false);
        if (await ScalarIntAsync(database,
            "SELECT COUNT(*) FROM sys.tables WHERE name IN (N'SchemaVersions',N'FluentMigratorVersionInfo');").ConfigureAwait(false) != 2)
            throw new LocalSimulationBlockedException("LocalSimulation.Schema13Required");

        await EnsureLoginsAsync().ConfigureAwait(false);
        await EnsureUsersAndGrantsAsync(database).ConfigureAwait(false);
        if (await ReadMarkerAsync(database, SeedMarker).ConfigureAwait(false) is null)
            await SeedAsync(database).ConfigureAwait(false);
        await VerifyAsync().ConfigureAwait(false);
    }

    private async Task VerifyAsync()
    {
        await VerifyMarkerAsync().ConfigureAwait(false);
        var runtime = _settings.BuildRuntimeConnectionString("127.0.0.1,11433").ConnectionString;
        await new SqlWebRuntimePrincipalValidator(requireJournal: true, requireResolution: true, requireDesktopApi: true)
            .ValidateAsync(runtime, CancellationToken.None).ConfigureAwait(false);
        await new SqlSuperuserBindingValidator()
            .ValidateAsync(runtime, LocalSimulationSqlNames.WriterUsername, CancellationToken.None)
            .ConfigureAwait(false);

        await using var oracle = new SqlConnection(
            _settings.BuildOracleConnectionString("127.0.0.1,11433").ConnectionString);
        await oracle.OpenAsync().ConfigureAwait(false);
        var unsafePermission = await ScalarIntAsync(oracle, @"
SELECT CASE WHEN
 ISNULL(HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','CONTROL'),0)<>0 OR
 ISNULL(IS_ROLEMEMBER('db_owner'),0)<>0 OR
 ISNULL(IS_ROLEMEMBER('db_datawriter'),0)<>0 OR
 ISNULL(HAS_PERMS_BY_NAME('dbo.WebOperationJournal','OBJECT','SELECT'),0)<>1 OR
 ISNULL(HAS_PERMS_BY_NAME('dbo.SalesReceipts','OBJECT','INSERT'),0)<>0
 OR EXISTS(
   SELECT 1 FROM sys.database_role_members drm
   JOIN sys.database_principals member ON member.principal_id=drm.member_principal_id
   WHERE member.name=USER_NAME()
 )
 OR EXISTS(SELECT 1 FROM sys.fn_my_permissions(NULL,N'DATABASE') WHERE permission_name<>N'CONNECT')
 OR EXISTS(SELECT 1 FROM sys.fn_my_permissions(N'dbo',N'SCHEMA'))
 OR EXISTS(
   SELECT 1 FROM sys.objects o
   CROSS APPLY sys.fn_my_permissions(QUOTENAME(OBJECT_SCHEMA_NAME(o.object_id))+N'.'+QUOTENAME(o.name),N'OBJECT') p
   WHERE o.is_ms_shipped=0 AND (
     p.permission_name<>N'SELECT' OR o.name NOT IN
       (N'WebAuthSessions',N'WebOperationJournal',N'SalesReceipts',N'SalesReceiptLines',N'GoodsReceipts',N'GoodsReceiptLines',N'StockLedger',N'StockValuationEntries',N'StockBalances')
   )
 )
THEN 1 ELSE 0 END;").ConfigureAwait(false);
        if (unsafePermission != 0)
            throw new LocalSimulationBlockedException("LocalSimulation.OraclePermissionRefused");
    }

    private async Task VerifyMarkerAsync()
    {
        await using var database = new SqlConnection(DatabaseAdminConnection());
        await database.OpenAsync().ConfigureAwait(false);
        var marker = await ReadMarkerAsync(database, InstanceMarker).ConfigureAwait(false);
        if (marker != _settings.InstanceId.ToString("D"))
            throw new LocalSimulationBlockedException("LocalSimulation.DatabaseOwnershipMismatch");
    }

    private async Task EnsureLoginsAsync()
    {
        await using var master = new SqlConnection(_settings.AdminConnection.ConnectionString);
        await master.OpenAsync().ConfigureAwait(false);
        await ExecuteAsync(master, LoginSql(LocalSimulationSqlNames.RuntimeLogin, _settings.RuntimePassword) +
            LoginSql(LocalSimulationSqlNames.OracleLogin, _settings.OraclePassword)).ConfigureAwait(false);
    }

    private static string LoginSql(string login, string password)
    {
        var safePassword = password.Replace("'", "''", StringComparison.Ordinal);
        return $"IF SUSER_ID(N'{login}') IS NULL CREATE LOGIN [{login}] WITH PASSWORD=N'{safePassword}',CHECK_POLICY=ON;";
    }

    private async Task EnsureUsersAndGrantsAsync(SqlConnection database)
    {
        await ExecuteAsync(database, $@"
IF USER_ID(N'{LocalSimulationSqlNames.RuntimeLogin}') IS NULL CREATE USER [{LocalSimulationSqlNames.RuntimeLogin}] FOR LOGIN [{LocalSimulationSqlNames.RuntimeLogin}];
IF USER_ID(N'{LocalSimulationSqlNames.OracleLogin}') IS NULL CREATE USER [{LocalSimulationSqlNames.OracleLogin}] FOR LOGIN [{LocalSimulationSqlNames.OracleLogin}];
IF USER_ID(N'{LocalSimulationSqlNames.MigrationLogin}') IS NULL CREATE USER [{LocalSimulationSqlNames.MigrationLogin}] FOR LOGIN [{LocalSimulationSqlNames.MigrationLogin}];").ConfigureAwait(false);

        var root = FindRepositoryRoot();
        var grants = File.ReadAllText(Path.Combine(root, "deployment", "sql", "Provision-MyDmsVnWebPrincipals.sql"))
            .Replace("$(WebRuntimeUser)", LocalSimulationSqlNames.RuntimeLogin, StringComparison.Ordinal)
            .Replace("$(MigrationUser)", LocalSimulationSqlNames.MigrationLogin, StringComparison.Ordinal);
        await ExecuteAsync(database, grants).ConfigureAwait(false);
        var desktop = File.ReadAllText(Path.Combine(root, "deployment", "sql", "Provision-MyDmsVnDesktopApiGrants.sql"))
            .Replace("$(WebRuntimeUser)", LocalSimulationSqlNames.RuntimeLogin, StringComparison.Ordinal);
        await ExecuteAsync(database, desktop).ConfigureAwait(false);
        await ExecuteAsync(database, $@"
GRANT SELECT ON dbo.WebAuthSessions TO [{LocalSimulationSqlNames.OracleLogin}];
GRANT SELECT ON dbo.WebOperationJournal TO [{LocalSimulationSqlNames.OracleLogin}];
GRANT SELECT ON dbo.SalesReceipts TO [{LocalSimulationSqlNames.OracleLogin}];
GRANT SELECT ON dbo.SalesReceiptLines TO [{LocalSimulationSqlNames.OracleLogin}];
GRANT SELECT ON dbo.GoodsReceipts TO [{LocalSimulationSqlNames.OracleLogin}];
GRANT SELECT ON dbo.GoodsReceiptLines TO [{LocalSimulationSqlNames.OracleLogin}];
GRANT SELECT ON dbo.StockLedger TO [{LocalSimulationSqlNames.OracleLogin}];
GRANT SELECT ON dbo.StockValuationEntries TO [{LocalSimulationSqlNames.OracleLogin}];
GRANT SELECT ON dbo.StockBalances TO [{LocalSimulationSqlNames.OracleLogin}];").ConfigureAwait(false);
    }

    private async Task SeedAsync(SqlConnection database)
    {
        await using var transaction = (SqlTransaction)await database.BeginTransactionAsync().ConfigureAwait(false);
        var hasher = new BcryptPasswordHasher();
        var writerId = await InsertUserAsync(database, transaction, LocalSimulationSqlNames.WriterUsername,
            _settings.WriterPassword, hasher).ConfigureAwait(false);
        await InsertUserAsync(database, transaction, LocalSimulationSqlNames.DeniedUsername,
            _settings.DeniedPassword, hasher).ConfigureAwait(false);
        await using (var binding = new SqlCommand(
            "INSERT dbo.SystemSecuritySettings(Id,SuperuserUserId,BoundNormalizedUsername) VALUES (1,@id,@name);",
            database, transaction))
        {
            binding.Parameters.Add("@id", SqlDbType.Int).Value = writerId;
            binding.Parameters.Add("@name", SqlDbType.NVarChar, 100).Value = LocalSimulationSqlNames.WriterUsername.ToUpperInvariant();
            await binding.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        await ExecuteAsync(database, transaction, @"
INSERT dbo.Customers(Code,Name) VALUES (N'LOCAL-CUST',N'Local Simulation Customer');
INSERT dbo.Warehouses(Code,Name) VALUES (N'LOCAL-WH',N'Local Simulation Warehouse');
INSERT dbo.Employees(Code,Name) VALUES (N'LOCAL-EMP',N'Local Simulation Employee');
INSERT dbo.Products(Code,Name,Unit) VALUES (N'LOCAL-PROD',N'Local Simulation Product',N'EA');").ConfigureAwait(false);
        await AddMarkerAsync(database, transaction, SeedMarker, "synthetic-v1").ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
    }

    private static async Task<int> InsertUserAsync(SqlConnection connection, SqlTransaction transaction,
        string username, string password, BcryptPasswordHasher hasher)
    {
        await using var command = new SqlCommand(@"
INSERT dbo.Users(Username,NormalizedUsername,DisplayName,Source,PasswordHash,PasswordSalt,PasswordAlgorithm,IsActive)
OUTPUT INSERTED.UserId VALUES(@name,@normalized,@display,N'Local',@hash,N'',@algorithm,1);", connection, transaction);
        command.Parameters.Add("@name", SqlDbType.NVarChar, 100).Value = username;
        command.Parameters.Add("@normalized", SqlDbType.NVarChar, 100).Value = username.ToUpperInvariant();
        command.Parameters.Add("@display", SqlDbType.NVarChar, 100).Value = username;
        command.Parameters.Add("@hash", SqlDbType.NVarChar, 255).Value = hasher.Hash(password);
        command.Parameters.Add("@algorithm", SqlDbType.NVarChar, 32).Value = hasher.Algorithm;
        return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private string DatabaseAdminConnection() => new SqlConnectionStringBuilder(_settings.AdminConnection.ConnectionString)
    {
        InitialCatalog = LocalSimulationSqlNames.Database,
    }.ConnectionString;

    private static async Task<string?> ReadMarkerAsync(SqlConnection connection, string name)
    {
        await using var command = new SqlCommand(
            "SELECT CONVERT(nvarchar(128),value) FROM sys.extended_properties WHERE class=0 AND name=@name;", connection);
        command.Parameters.Add("@name", SqlDbType.NVarChar, 128).Value = name;
        var value = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return NormalizeMarkerValue(value);
    }

    internal static string? NormalizeMarkerValue(object? value) => value is null or DBNull
        ? null
        : Convert.ToString(value, CultureInfo.InvariantCulture);

    private static async Task AddMarkerAsync(SqlConnection connection, string name, string value)
    {
        await using var command = new SqlCommand(
            "EXEC sys.sp_addextendedproperty @name=@markerName,@value=@markerValue;", connection);
        command.Parameters.Add("@markerName", SqlDbType.NVarChar, 128).Value = name;
        command.Parameters.Add("@markerValue", SqlDbType.NVarChar, 128).Value = value;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task AddMarkerAsync(SqlConnection connection, SqlTransaction transaction, string name, string value)
    {
        await using var command = new SqlCommand(
            "EXEC sys.sp_addextendedproperty @name=@markerName,@value=@markerValue;", connection, transaction);
        command.Parameters.Add("@markerName", SqlDbType.NVarChar, 128).Value = name;
        command.Parameters.Add("@markerValue", SqlDbType.NVarChar, 128).Value = value;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<int> ScalarIntAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 30 };
        return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 90 };
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 90 };
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) &&
                Directory.Exists(Path.Combine(directory.FullName, "deployment", "sql")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new LocalSimulationBlockedException("LocalSimulation.RepositoryRootMissing");
    }
}
