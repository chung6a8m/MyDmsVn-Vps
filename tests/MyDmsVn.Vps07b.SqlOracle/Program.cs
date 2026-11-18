using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DbUp;
using Microsoft.Data.SqlClient;

namespace MyDmsVn.Vps07b.SqlOracle;

/// <summary>
/// Independently prepared DbUp snapshots + read-only direct SQL oracle, against
/// throwaway SQL2022 on localhost with verified TLS. Never targets user SQL.
/// Every case explicitly executes, never silently skips on missing DB.
/// </summary>
internal static class Program
{
    private const string Prefix = "MyDmsVnVps07bCi";
    private static readonly string[] Scripts = {
        "001_PersistenceFoundation.sql", "002_Identity.sql", "003_Catalog.sql",
        "004_Catalog_Nul_Constraints.sql", "005_GoodsReceipt.sql",
        "006_GoodsReceipt_Number_Sequence.sql", "007_Inventory_Stock.sql",
        "008_Security_Administration.sql", "009_Security_Permission_Import.sql",
        "010_DirectSalesAndValuation.sql", "011_WebAuthSessions.sql",
        "012_WebOperationJournal.sql", "013_WebOperationTerminalResolution.sql"
    };

    private static readonly string Stamp = Guid.NewGuid().ToString("N").Substring(0, 8);
    private static string _master = "";
    private static string _cli = "";
    private static string _stage = "admission";
    private static int _passed;
    private static int _executed;
    private static readonly List<string> Databases = new();

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length != 1 || args[0] != "all" ||
                Environment.GetEnvironmentVariable("CI") != "true" ||
                Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
                Environment.GetEnvironmentVariable("VPS07B_CI_EPHEMERAL_SQL") != "approved-container-only")
                throw new InvalidOperationException("VPS07B_CI_SCOPE_REQUIRED");
            _cli = Required("VPS07B_CLI_DLL");
            if (!File.Exists(_cli))
                throw new InvalidOperationException("VPS07B_CLI_DLL_MISSING");
            var b = new SqlConnectionStringBuilder(Required("VPS07B_CI_SQL_ADMIN_CONNECTION_STRING"));
            if (!string.Equals(b.DataSource, "localhost,1433", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(b.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase) ||
                b.Encrypt.ToString() != "True" || b.TrustServerCertificate ||
                b.IntegratedSecurity || b.UserID != "sa" || b.Pooling)
                throw new InvalidOperationException("VPS07B_LOCAL_SQL_TLS_NOT_VERIFIED");
            _master = b.ConnectionString;

            _stage = "SQL-TLS-readiness";
            await WaitForSql();
            _stage = "DbUp13-source-bootstrap";
            var baseline = await PrepareDbUp(13);
            _stage = "DbUp13-independent-object-oracle";
            var baselineSignature = await DefinitionSignature(baseline);

            _stage = "FM13-fresh-create";
            var fresh = await Create();
            _stage = "FM13-fresh-migrate";
            await RunCli(fresh, 13, expectSuccess: true);
            await Check(fresh, 13);
            Assert(await DefinitionSignature(fresh) == baselineSignature,
                "VPS07B_FRESH_SCHEMA_DRIFT_FROM_INDEPENDENT_DBUP13");
            Pass("fresh-to-13 vs DbUp13, 2 journals, independent object inventory");

            var original = await JournalSignature(fresh);
            await RunCli(fresh, 13, expectSuccess: true);
            Assert(await JournalSignature(fresh) == original,
                "VPS07B_NOOP_REWROTE_JOURNAL");
            Pass("schema13 idempotent no-op");

            foreach (var sourceVersion in new[] { 9, 10, 11, 12, 13 })
            {
                _stage = "DbUp" + sourceVersion + "-source-bootstrap";
                var db = sourceVersion == 13 ? baseline : await PrepareDbUp(sourceVersion);
                _stage = "FM" + sourceVersion + "-to-13";
                var beforeData = await SourceData(db);
                var beforeHistory = await LegacyJournalPrefix(db, sourceVersion);
                await RunCli(db, 13, expectSuccess: true);
                await Check(db, 13);
                Assert(beforeHistory == sourceVersion, "VPS07B_SOURCE_HISTORY_REWRITTEN");
                Assert(await SourceData(db) == beforeData, "VPS07B_SOURCE_DATA_LOST");
                if (sourceVersion == 9)
                {
                    var valuation = await Text(db, @"SELECT CONCAT(
 (SELECT CONVERT(varchar(32),InventoryValue) FROM dbo.StockBalances),
 (SELECT CONVERT(varchar(32),ValueChange) FROM dbo.StockValuationEntries));");
                    Assert(valuation == "10.000010.0000", "VPS07B_SCHEMA9_VALUATION_RECONCILIATION_FAILED");
                }
                Assert(await DefinitionSignature(db) == baselineSignature,
                    "VPS07B_UPGRADE_DEFINITION_DRIFT");
                Pass("exact" + sourceVersion + " to 13; source rows, schema and both journals");
            }

            // Adversarial and concurrent admission probes use a dedicated,
            // already-upgraded disposable database. They never alter a user DB.
            var denied = baseline;
            var stable = await JournalSignature(denied);
            await Exec(denied, "UPDATE dbo.FluentMigratorVersionInfo SET Description=N'TAMPERED' WHERE Version=1;");
            await RunCli(denied, 13, expectSuccess: false);
            Assert(await Count(denied, "SELECT COUNT(*) FROM dbo.FluentMigratorVersionInfo;") == 13,
                "VPS07B_TAMPERED_JOURNAL_CHANGED");
            await Exec(denied, "UPDATE dbo.FluentMigratorVersionInfo SET Description=N'001_PersistenceFoundation.sql' WHERE Version=1;");
            Assert(await JournalSignature(denied) == stable, "VPS07B_TAMPER_NOT_RESTORED");
            Pass("tampered journal refused before mutation");

            await Exec(denied, "CREATE TABLE dbo.Vps07bUnknownObject(Id int NOT NULL);");
            await RunCli(denied, 13, expectSuccess: false);
            await Exec(denied, "DROP TABLE dbo.Vps07bUnknownObject;");
            Assert(await JournalSignature(denied) == stable,
                "VPS07B_UNKNOWN_SCHEMA_CHANGED_HISTORY");
            Pass("unknown object refused before mutation");

            // Separate session owns the app lock. Runner must fail immediately.
            using (var locked = new SqlConnection(For(denied)))
            {
                await locked.OpenAsync();
                using var acquire = new SqlCommand(@"DECLARE @r int;
EXEC @r=sys.sp_getapplock @Resource=N'MyDmsVn.SchemaMigration',
 @LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0; SELECT @r;", locked);
                Assert(Convert.ToInt32(await acquire.ExecuteScalarAsync(), CultureInfo.InvariantCulture) >= 0,
                    "VPS07B_ORACLE_LOCK_FAILED");
                await RunCli(denied, 13, expectSuccess: false);
                using var release = new SqlCommand(@"DECLARE @r int;
EXEC @r=sys.sp_releaseapplock @Resource=N'MyDmsVn.SchemaMigration',
 @LockOwner=N'Session'; SELECT @r;", locked);
                Assert(Convert.ToInt32(await release.ExecuteScalarAsync(), CultureInfo.InvariantCulture) >= 0,
                    "VPS07B_ORACLE_UNLOCK_FAILED");
            }
            Assert(await JournalSignature(denied) == stable, "VPS07B_CONCURRENT_LOCK_CHANGED_HISTORY");
            Pass("simultaneous migrator app lock refusal");

            var partial = await PrepareDbUp(12);
            await Exec(partial, "DROP PROCEDURE dbo.ApplySecurityPermissionImport;");
            var partialBefore = await JournalSignatureOfLegacy(partial);
            await RunCli(partial, 13, expectSuccess: false);
            Assert(await JournalSignatureOfLegacy(partial) == partialBefore,
                "VPS07B_PARTIAL_SCHEMA_CHANGED_JOURNAL");
            Pass("partial schema rejected with legacy journal unchanged");

            // Explicitly test no operator approval even with valid disposable SQL.
            await RunCli(denied, 13, expectSuccess: false, approved: false);
            Assert(await JournalSignature(denied) == stable, "VPS07B_UNAPPROVED_MUTATED");
            Pass("operator consent missing refused");

            Console.WriteLine("VPS07B_REAL_SQL_PASS: executed=" + _executed + "; passed=" +
                _passed + "; failed=0; skipped=0; SQL2022 verified TLS; ephemeral.");
            return 0;
        }
        catch (Exception ex)
        {
            // Test-only diagnostics print safe fixed-category names, never
            // connection strings, SQL source text, login or any row data.
            Console.Error.WriteLine("VPS07B_REAL_SQL_FAILED: executed=" + _executed +
                "; passed=" + _passed + "; failed=1; skipped=0; type=" +
                ex.GetType().Name + "; stage=" + _stage + "; sql=" + (ex is SqlException q ? q.Number.ToString(CultureInfo.InvariantCulture) : "none") + "; sqlLine=" + (ex is SqlException lineError ? lineError.LineNumber.ToString(CultureInfo.InvariantCulture) : "none") + "; code=" + SafeCode(ex.Message));
            return 1;
        }
        finally
        {
            // Disposable Docker DB cleanup; error handling does not print secrets.
            foreach (var db in Databases.AsEnumerable().Reverse())
            {
                try { await Drop(db); }
                catch { Console.Error.WriteLine("VPS07B_CLEANUP_REQUIRES_CONTAINER_REMOVAL"); }
            }
        }
    }

    private static async Task<string> PrepareDbUp(int version)
    {
        var db = await Create();
        _stage = "DbUp" + version + "-restrict";
        await ExecMaster("ALTER DATABASE [" + db + "] SET RESTRICTED_USER WITH ROLLBACK IMMEDIATE;");
        _stage = "DbUp" + version + "-apply-SQL";
        var upgrader = DeployChanges.To.SqlDatabase(For(db))
            .WithScriptsEmbeddedInAssembly(typeof(Program).Assembly, resource =>
            {
                const string token = ".Scripts.";
                var at = resource.LastIndexOf(token, StringComparison.Ordinal);
                if (at < 0) return false;
                var name = resource.Substring(at + token.Length);
                var index = Array.IndexOf(Scripts, name);
                return index >= 0 && index < version;
            })
            .JournalToSqlTable("dbo", "SchemaVersions")
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build();
        var result = upgrader.PerformUpgrade();
        Assert(result.Successful && result.Scripts.Count() == version,
            "VPS07B_DBUP_SOURCE_BOOTSTRAP_FAILED_" + version);
        _stage = "DbUp" + version + "-seed";
        await Seed(db);
        Pass("original DbUp schema" + version + " snapshot and source seed");
        return db;
    }

    private static async Task Seed(string db)
    {
        // Real posted goods receipt, stock ledger and valuation. On schema9
        // valuation is computed by migration 010; on schema10+ it is pre-seeded.
        // The oracle must reconcile four units at 2.5 each (=10), not just
        // empty-table upgrades.
        await Exec(db, @"
DECLARE @actor int, @product int, @warehouse int, @employee int,
 @receipt bigint, @receiptLine bigint, @ledger bigint;
DECLARE @posted datetime2(7) = SYSUTCDATETIME();
INSERT dbo.Users(Username,NormalizedUsername,DisplayName,Source,
 PasswordHash,PasswordSalt,PasswordAlgorithm,IsActive)
VALUES(N'ci-operator',N'CI-OPERATOR',N'Operator retained',N'Local',
 N'ci-inert-hash',N'',N'None',1);
SET @actor=SCOPE_IDENTITY();
INSERT dbo.Products(Code,Name,Unit) VALUES(N'CI-PROD',N'Product retained',N'EA');
SET @product=SCOPE_IDENTITY();
INSERT dbo.Warehouses(Code,Name) VALUES(N'CI-WH',N'Warehouse retained');
SET @warehouse=SCOPE_IDENTITY();
INSERT dbo.Customers(Code,Name) VALUES(N'CI-CUST',N'Customer retained');
INSERT dbo.Employees(Code,Name) VALUES(N'CI-EMP',N'Employee retained');
SET @employee=SCOPE_IDENTITY();
INSERT dbo.GoodsReceipts(ReceiptNo,ReceiptDate,WarehouseId,EmployeeId,
 Status,CreatedByUserId,PostedAtUtc,PostedByUserId)
VALUES(N'CI-RECEIPT',CONVERT(date,@posted),@warehouse,@employee,
 'Posted',@actor,@posted,@actor);
SET @receipt=SCOPE_IDENTITY();
INSERT dbo.GoodsReceiptLines(ReceiptId,[LineNo],ProductId,Quantity,UnitCost,CreatedByUserId)
VALUES(@receipt,1,@product,CAST(4 AS decimal(18,4)),CAST(2.5 AS decimal(19,4)),@actor);
SET @receiptLine=SCOPE_IDENTITY();
INSERT dbo.StockLedger(WarehouseId,ProductId,DocumentType,DocumentId,DocumentLineId,
 QuantityChange,PostedAtUtc,PostedByUserId)
VALUES(@warehouse,@product,'GoodsReceipt',@receipt,@receiptLine,
 CAST(4 AS decimal(18,4)),@posted,@actor);
SET @ledger=SCOPE_IDENTITY();
IF COL_LENGTH(N'dbo.StockBalances',N'InventoryValue') IS NULL
    INSERT dbo.StockBalances(WarehouseId,ProductId,Quantity)
    VALUES(@warehouse,@product,CAST(4 AS decimal(18,4)));
ELSE
BEGIN
    INSERT dbo.StockBalances(WarehouseId,ProductId,Quantity,InventoryValue)
    VALUES(@warehouse,@product,CAST(4 AS decimal(18,4)),CAST(10 AS decimal(28,4)));
    INSERT dbo.StockValuationEntries(LedgerId,ValueChange)
    VALUES(@ledger,CAST(10 AS decimal(28,4)));
END;");
    }

    private static async Task<string> SourceData(string db)
    {
        var data = await Text(db, @"SELECT CONCAT(
(SELECT Code,Name,Unit FROM dbo.Products ORDER BY ProductId FOR JSON PATH),
(SELECT Code,Name FROM dbo.Customers ORDER BY CustomerId FOR JSON PATH),
(SELECT Code,Name FROM dbo.Warehouses ORDER BY WarehouseId FOR JSON PATH),
(SELECT Code,Name FROM dbo.Employees ORDER BY EmployeeId FOR JSON PATH),
(SELECT CONVERT(varchar(32),Quantity) AS Qty FROM dbo.StockBalances ORDER BY WarehouseId,ProductId FOR JSON PATH),
(SELECT ReceiptNo,Status FROM dbo.GoodsReceipts ORDER BY ReceiptId FOR JSON PATH),
(SELECT Quantity,UnitCost FROM dbo.GoodsReceiptLines ORDER BY ReceiptLineId FOR JSON PATH),
(SELECT DocumentType,QuantityChange FROM dbo.StockLedger ORDER BY LedgerId FOR JSON PATH)
);");
        return Hash(data);
    }

    private static async Task<string> DefinitionSignature(string db)
    {
        // Independent SQL catalog oracle: exclude FM journal only, then compare
        // live object definitions, column/index signatures and routines against
        // a separately installed original DbUp13 reference database.
        var signatures = new List<string>();
        using var c = new SqlConnection(For(db));
        await c.OpenAsync();
        using var command = new SqlCommand(@"
SELECT CONCAT(N'O|',SCHEMA_NAME(schema_id),N'|',name,N'|',type) AS Sig
FROM sys.objects WHERE is_ms_shipped=0 AND name<>N'FluentMigratorVersionInfo'
UNION ALL SELECT CONCAT(N'C|',OBJECT_SCHEMA_NAME(object_id),N'|',OBJECT_NAME(object_id),
 N'|',column_id,N'|',name,N'|',TYPE_NAME(user_type_id),N'|',max_length,
 N'|',precision,N'|',scale,N'|',is_nullable)
FROM sys.columns WHERE object_id<>ISNULL(OBJECT_ID(N'dbo.FluentMigratorVersionInfo'),-1)
 AND OBJECTPROPERTY(object_id,'IsMSShipped')=0
UNION ALL SELECT CONCAT(N'M|',OBJECT_SCHEMA_NAME(object_id),N'|',OBJECT_NAME(object_id),
 N'|',CONVERT(varchar(64),HASHBYTES('SHA2_256',definition),2))
FROM sys.sql_modules WHERE OBJECTPROPERTY(object_id,'IsMSShipped')=0
UNION ALL SELECT CONCAT(N'I|',OBJECT_SCHEMA_NAME(object_id),N'|',OBJECT_NAME(object_id),
 N'|',name,N'|',is_unique,N'|',is_primary_key,N'|',has_filter)
FROM sys.indexes WHERE OBJECTPROPERTY(object_id,'IsMSShipped')=0
 AND object_id<>ISNULL(OBJECT_ID(N'dbo.FluentMigratorVersionInfo'),-1)
UNION ALL SELECT CONCAT(N'T|',name,N'|',is_disabled,N'|',is_instead_of_trigger)
FROM sys.triggers WHERE is_ms_shipped=0
ORDER BY Sig;", c);
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            signatures.Add(reader.GetString(0));
        return Hash(string.Join("\n", signatures));
    }

    private static async Task<int> LegacyJournalPrefix(string db, int expected)
    {
        using var c = new SqlConnection(For(db)); await c.OpenAsync();
        using var cmd = new SqlCommand("SELECT ScriptName FROM dbo.SchemaVersions ORDER BY Id;", c);
        using var reader = await cmd.ExecuteReaderAsync();
        var index = 0;
        while (await reader.ReadAsync())
        {
            Assert(index < expected, "VPS07B_LEGACY_JOURNAL_TOO_LONG");
            var name = reader.GetString(0);
            Assert(name.EndsWith(".Scripts." + Scripts[index], StringComparison.Ordinal) ||
                name == Scripts[index], "VPS07B_LEGACY_JOURNAL_UNKNOWN");
            index++;
        }
        Assert(index == expected, "VPS07B_LEGACY_JOURNAL_COUNT");
        return index;
    }

    private static async Task Check(string db, int expected)
    {
        Assert(await LegacyJournalPrefix(db, expected) == expected, "VPS07B_JOURNAL_MISSING");
        using var c = new SqlConnection(For(db)); await c.OpenAsync();
        using var cmd = new SqlCommand(
            "SELECT Version,Description FROM dbo.FluentMigratorVersionInfo ORDER BY Version;", c);
        using var reader = await cmd.ExecuteReaderAsync();
        var index = 0;
        while (await reader.ReadAsync())
        {
            Assert(index < expected && reader.GetInt64(0) == index+1 &&
                reader.GetString(1) == Scripts[index], "VPS07B_FM_JOURNAL_MISMATCH");
            index++;
        }
        Assert(index == expected, "VPS07B_FM_JOURNAL_COUNT");
    }

    private static async Task RunCli(string db, int version, bool expectSuccess, bool approved = true)
    {
        var psi = new ProcessStartInfo("dotnet") { RedirectStandardError = true,
            RedirectStandardOutput = true, UseShellExecute = false };
        psi.ArgumentList.Add(_cli);
        psi.ArgumentList.Add("migrate");
        psi.ArgumentList.Add("schema" + version.ToString(CultureInfo.InvariantCulture));
        psi.Environment["VPS07B_SQL_CONNECTION_STRING"] = For(db);
        psi.Environment["VPS07B_OPERATOR_APPROVAL"] =
            approved ? "approved-maintenance-and-verified-backup" : "";
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("VPS07B_CHILD_NOT_STARTED");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var limit = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(120));
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree:true); throw new InvalidOperationException("VPS07B_CHILD_TIMEOUT"); }
        var output = await stdout;
        var diagnostic = await stderr;
        if (expectSuccess)
        {
            Assert(process.ExitCode == 0 && output.Contains("VPS07B_SQL_VERIFIED:",StringComparison.Ordinal),
                "VPS07B_CLI_FAILED_" + process.ExitCode + "_" + SafeCode(diagnostic));
        }
        else
        {
            Assert(process.ExitCode != 0 && !output.Contains("VPS07B_SQL_VERIFIED:",StringComparison.Ordinal),
                "VPS07B_CLI_SHOULD_REFUSE");
        }
    }

    private static async Task<string> JournalSignature(string db) =>
        Hash(await Text(db, @"SELECT CONCAT(
 (SELECT Id,ScriptName,CONVERT(varchar(33),Applied,126) AS Applied
   FROM dbo.SchemaVersions ORDER BY Id FOR JSON PATH),
 (SELECT Version,Description,CONVERT(varchar(33),AppliedOn,126) AS AppliedOn
   FROM dbo.FluentMigratorVersionInfo ORDER BY Version FOR JSON PATH));"));

    private static async Task<string> JournalSignatureOfLegacy(string db) =>
        Hash(await Text(db, @"SELECT Id,ScriptName,CONVERT(varchar(33),Applied,126) AS Applied
   FROM dbo.SchemaVersions ORDER BY Id FOR JSON PATH;"));

    private static async Task<string> Text(string db, string sql)
    {
        using var c = new SqlConnection(For(db)); await c.OpenAsync();
        using var command = new SqlCommand(sql, c);
        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? "";
    }

    private static async Task<int> Count(string db, string sql)
    {
        using var c = new SqlConnection(For(db)); await c.OpenAsync();
        using var command = new SqlCommand(sql, c);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    private static async Task Exec(string db, string sql)
    {
        using var c = new SqlConnection(For(db)); await c.OpenAsync();
        using var command = new SqlCommand(sql, c) { CommandTimeout = 90 };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecMaster(string sql)
    {
        using var c = new SqlConnection(_master); await c.OpenAsync();
        using var command = new SqlCommand(sql, c) { CommandTimeout = 90 };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> Create()
    {
        var database = Prefix + Stamp + Databases.Count.ToString(CultureInfo.InvariantCulture);
        Assert(database.Length < 100, "VPS07B_DBNAME_INVALID");
        await ExecMaster("IF DB_ID(N'" + database + "') IS NOT NULL THROW 51720,'Refuse existing database',1; CREATE DATABASE [" + database + "];");
        Databases.Add(database);
        return database;
    }

    private static async Task Drop(string database)
    {
        await ExecMaster("ALTER DATABASE [" + database + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + database + "];");
    }

    private static string For(string database)
    {
        Assert(Databases.Contains(database, StringComparer.Ordinal), "VPS07B_NOT_OWNED_DB");
        return new SqlConnectionStringBuilder(_master) { InitialCatalog=database, Pooling=false }.ConnectionString;
    }

    private static async Task WaitForSql()
    {
        for(var attempt=0; attempt<60; attempt++)
        {
            try { using var c=new SqlConnection(_master); await c.OpenAsync(); return; }
            catch (SqlException) { await Task.Delay(2000); }
        }
        throw new InvalidOperationException("VPS07B_SQL_TLS_LISTENER_UNAVAILABLE");
    }

    private static string Required(string key) => Environment.GetEnvironmentVariable(key)
        is { Length: > 0 } value ? value : throw new InvalidOperationException("VPS07B_CONFIG_MISSING");
    private static string SafeCode(string input)
    {
        var known = new[] { "Migration.", "VPS07B_", "Schema." };
        foreach (var word in known)
        {
            var i=input.IndexOf(word,StringComparison.Ordinal);
            if (i>=0)
            {
                var code=new string(input.Substring(i).TakeWhile(c =>
                    char.IsLetterOrDigit(c) || c=='.' || c=='_').Take(70).ToArray());
                return code;
            }
        }
        return "UNCLASSIFIED";
    }
    private static void Assert(bool condition, string code)
    {
        if(!condition) throw new InvalidOperationException(code);
    }
    private static void Pass(string caseName)
    {
        _executed++; _passed++;
        Console.WriteLine("VPS07B_CASE_PASS: " + caseName);
    }
}
