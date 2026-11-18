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
    private const int RequiredSqlCaseCount = 58;
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
            try { await RunCli(fresh, 13, expectSuccess: true); }
            catch (InvalidOperationException)
            {
                // Compare independent SQL object catalog signatures, never row data.
                var reference = await DefinitionSignatureRows(baseline);
                var migrated = await DefinitionSignatureRows(fresh);
                Console.Error.WriteLine("VPS07B_FM_FRESH_DEFINITION_DIFF: missing=" +
                    reference.Except(migrated, StringComparer.Ordinal).Count() +
                    "; extra=" + migrated.Except(reference, StringComparer.Ordinal).Count());
                foreach (var item in reference.Except(migrated, StringComparer.Ordinal).Take(8))
                    Console.Error.WriteLine("VPS07B_MISSING: " + CatalogKey(item));
                foreach (var item in migrated.Except(reference, StringComparer.Ordinal).Take(8))
                    Console.Error.WriteLine("VPS07B_EXTRA: " + CatalogKey(item));
                const string journalIndexSql = @"SELECT TOP 1 CONCAT(name COLLATE Latin1_General_100_BIN2,N':',
type_desc COLLATE Latin1_General_100_BIN2,N':',is_unique,N':',is_primary_key)
FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SchemaVersions') AND index_id>0 ORDER BY index_id;";
                Console.Error.WriteLine("VPS07B_SOURCE_DBUP_INDEX=" + await Text(baseline, journalIndexSql));
                Console.Error.WriteLine("VPS07B_FRESH_JOURNAL_INDEX=" + await Text(fresh, journalIndexSql));
                const string moduleShape = @"SELECT CONCAT(DATALENGTH(definition),N':',
UNICODE(LEFT(definition,1)),N':',UNICODE(RIGHT(definition,1)),N':',
UNICODE(RIGHT(RTRIM(definition),1)),N':',
LEN(definition),N':',
LEN(definition)-LEN(REPLACE(definition,CHAR(13),N'')),N':',
LEN(definition)-LEN(REPLACE(definition,CHAR(10),N'')))
FROM sys.sql_modules WHERE object_id=OBJECT_ID(N'dbo.ReadSecurityPermissionImportTarget');";
                Console.Error.WriteLine("VPS07B_SOURCE_MODULE_SHAPE=" + await Text(baseline, moduleShape));
                Console.Error.WriteLine("VPS07B_FRESH_MODULE_SHAPE=" + await Text(fresh, moduleShape));
                foreach (var module in new[] { "ApplySecurityAdministration", "ApplySecurityPermissionImport" })
                {
                    var select = "SELECT definition FROM sys.sql_modules WHERE object_id=OBJECT_ID(N'dbo." + module + "');";
                    var oldValue = await Text(baseline, select);
                    var newValue = await Text(fresh, select);
                    var at = 0;
                    while (at < oldValue.Length && at < newValue.Length && oldValue[at] == newValue[at]) at++;
                    var oldCode = at < oldValue.Length ? ((int)oldValue[at]).ToString(CultureInfo.InvariantCulture) : "END";
                    var newCode = at < newValue.Length ? ((int)newValue[at]).ToString(CultureInfo.InvariantCulture) : "END";
                    Console.Error.WriteLine("VPS07B_MODULE_DIFF_" + module + ": oldLen=" + oldValue.Length +
                        ";newLen=" + newValue.Length + ";at=" + at + ";oldChar=" + oldCode +
                        ";newChar=" + newCode + ";oldTail=" + (int)oldValue[^1] + ";newTail=" + (int)newValue[^1]);
                }
                throw;
            }
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
                var beforeData = await SourceData(db, sourceVersion);
                var beforeHistory = await LegacyJournalPrefix(db, sourceVersion);
                await RunCli(db, 13, expectSuccess: true);
                await Check(db, 13);
                await AssertStockAndCogs(db);
                Assert(beforeHistory == sourceVersion, "VPS07B_SOURCE_HISTORY_REWRITTEN");
                Assert(await SourceData(db, sourceVersion) == beforeData, "VPS07B_SOURCE_DATA_LOST");
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

            // Explicit target admission: a request for schema10/12 is not an
            // implicit request to run all 13 versions.
            foreach (var targetVersion in new[] { 10, 12 })
            {
                _stage = "FM-fresh-target-" + targetVersion;
                var db = await Create();
                var reference = await PrepareDbUp(targetVersion);
                await RunCli(db, targetVersion, expectSuccess: true);
                await Check(db, targetVersion);
                Assert(await DefinitionSignature(db) == await DefinitionSignature(reference),
                    "VPS07B_EXPLICIT_TARGET_SCHEMA_MISMATCH");
                Pass("fresh install explicit target schema" + targetVersion);
            }

            // A single real database goes through both intermediate targets
            // while source rows stay immutable and journals advance exactly.
            _stage = "schema9-to-10-to-12-to-13";
            var chained = await PrepareDbUp(9);
            var chainedRows = await SourceData(chained);
            foreach (var targetVersion in new[] { 10, 12, 13 })
            {
                await RunCli(chained, targetVersion, expectSuccess: true);
                await Check(chained, targetVersion);
                Assert(await SourceData(chained) == chainedRows,
                    "VPS07B_INCREMENTAL_SOURCE_ROW_DRIFT");
                Pass("incremental bridge to schema" + targetVersion);
            }
            Assert(await DefinitionSignature(chained) == baselineSignature,
                "VPS07B_INCREMENTAL_FINAL_DEFINITION_DRIFT");

            // Only versions 9..13 have approved native fingerprints.
            // An interrupted fresh prefix 1..8 must be refused, not resumed
            // by guessing an unproven intermediate schema.
            _stage = "fresh-partial-upgrade-fail-closed";
            var partialFresh = await Create();
            await RunCli(partialFresh, 13, expectSuccess: false,
                ciFault: "5:1", expectedCode: "sql=51750");
            Assert(await Count(partialFresh, "SELECT COUNT(*) FROM dbo.SchemaVersions;") == 4,
                "VPS07B_PARTIAL_FRESH_LEGACY_PREFIX_MISMATCH");
            Assert(await Count(partialFresh, "SELECT COUNT(*) FROM dbo.FluentMigratorVersionInfo;") == 4,
                "VPS07B_PARTIAL_FRESH_FM_PREFIX_MISMATCH");
            await RunCli(partialFresh, 13, expectSuccess: false,
                expectedCode: "Migration.UnsupportedHistoricalVersion");
            Assert(await Count(partialFresh, "SELECT COUNT(*) FROM dbo.SchemaVersions;") == 4 &&
                await Count(partialFresh, "SELECT COUNT(*) FROM dbo.FluentMigratorVersionInfo;") == 4,
                "VPS07B_PARTIAL_FRESH_RETRY_MUTATED_HISTORY");
            Pass("interrupted fresh SQL005: version4 remains fail-closed, replacement required");

            // Three actual SQL THROW failures, after first/middle/last SQL
            // batch, prove FM rolls back BOTH journals and all business DDL.
            foreach (var fault in new[] { (source: 9, script: 10, batch: 1),
                                           (source: 12, script: 13, batch: 5),
                                           (source: 12, script: 13, batch: 9) })
            {
                _stage = "rollback-fault-" + fault.script + "-" + fault.batch;
                var db = await PrepareDbUp(fault.source);
                var beforeLegacy = await JournalSignatureOfLegacy(db);
                var beforeRows = await SourceData(db, fault.source);
                await RunCli(db, 13, expectSuccess: false,
                    ciFault: fault.script + ":" + fault.batch, expectedCode: "sql=51750");
                Assert(await JournalSignatureOfLegacy(db) == beforeLegacy,
                    "VPS07B_ROLLBACK_CHANGED_LEGACY_JOURNAL");
                Assert(await Count(db,
                    "SELECT COUNT(*) FROM dbo.FluentMigratorVersionInfo;") == fault.source,
                    "VPS07B_ROLLBACK_ADVANCED_FM_VERSION");
                Assert(await SourceData(db, fault.source) == beforeRows,
                    "VPS07B_ROLLBACK_CHANGED_BUSINESS_ROWS");
                Assert(await Count(db, "SELECT CASE WHEN OBJECT_ID(N'dbo.WebOperationTerminalAudit',N'U') IS NULL THEN 0 ELSE 1 END;")
                    == 0, "VPS07B_ROLLBACK_LEFT_LATE_OBJECT");
                await RunCli(db, 13, expectSuccess: true);
                await Check(db, 13);
                await AssertStockAndCogs(db);
                Assert(await SourceData(db, fault.source) == beforeRows, "VPS07B_RETRY_CHANGED_SOURCE_ROWS");
                Assert(await DefinitionSignature(db) == baselineSignature,
                    "VPS07B_RETRY_DEFINITION_DRIFT");
                Pass("batch fault " + fault.script + ":" + fault.batch + " rollback and same-source retry");
            }

            _stage = "writer-maintenance-negative";
            var writersLive = await PrepareDbUp(12);
            var priorHistory = await JournalSignatureOfLegacy(writersLive);
            await ExecMaster("ALTER DATABASE [" + writersLive + "] SET MULTI_USER WITH ROLLBACK IMMEDIATE;");
            await RunCli(writersLive, 13, expectSuccess: false,
                expectedCode: "Migration.MaintenanceAccessRequired");
            Assert(await JournalSignatureOfLegacy(writersLive) == priorHistory,
                "VPS07B_LIVE_WRITER_CHANGED_JOURNAL");
            Assert(await Count(writersLive,
                "SELECT CASE WHEN OBJECT_ID(N'dbo.FluentMigratorVersionInfo',N'U') IS NULL THEN 0 ELSE 1 END;") == 0,
                "VPS07B_MAINTENANCE_REFUSAL_CREATED_JOURNAL");
            await ExecMaster("ALTER DATABASE [" + writersLive +
                "] SET RESTRICTED_USER WITH ROLLBACK IMMEDIATE;");
            using (var liveWriter = new SqlConnection(For(writersLive)))
            {
                await liveWriter.OpenAsync();
                await RunCli(writersLive, 13, expectSuccess: false,
                    expectedCode: "Migration.WritersNotDrained");
            }
            Assert(await JournalSignatureOfLegacy(writersLive) == priorHistory,
                "VPS07B_CONCURRENT_WRITER_MUTATED_HISTORY");
            Assert(await Count(writersLive,
                "SELECT CASE WHEN OBJECT_ID(N'dbo.FluentMigratorVersionInfo',N'U') IS NULL THEN 0 ELSE 1 END;") == 0,
                "VPS07B_CONCURRENT_WRITER_CREATED_FM_JOURNAL");
            Pass("missing RESTRICTED_USER or active writer refused before bridge");

            _stage = "killed-in-flight-transaction-and-retry";
            var interrupted = await PrepareDbUp(12);
            var interruptedData = await SourceData(interrupted, 12);
            var interruptedJournal = await JournalSignatureOfLegacy(interrupted);
            await RunCli(interrupted, 13, expectSuccess: false,
                ciFault: "13:5:hang", interruptAtWaitfor: true);
            Assert(await JournalSignatureOfLegacy(interrupted) == interruptedJournal,
                "VPS07B_PROCESS_LOSS_CHANGED_LEGACY_JOURNAL");
            Assert(await Count(interrupted,
                "SELECT COUNT(*) FROM dbo.FluentMigratorVersionInfo;") == 12,
                "VPS07B_PROCESS_LOSS_LEFT_FM_VERSION");
            Assert(await Count(interrupted,
                "SELECT CASE WHEN OBJECT_ID(N'dbo.WebOperationTerminalAudit',N'U') IS NULL THEN 0 ELSE 1 END;") == 0,
                "VPS07B_PROCESS_LOSS_LEFT_LATE_TABLE");
            Assert(await SourceData(interrupted, 12) == interruptedData,
                "VPS07B_PROCESS_LOSS_CHANGED_SOURCE");
            await RunCli(interrupted, 13, expectSuccess: true);
            await Check(interrupted, 13);
            await AssertStockAndCogs(interrupted);
            Assert(await SourceData(interrupted, 12) == interruptedData,
                "VPS07B_PROCESS_LOSS_RETRY_CHANGED_SOURCE");
            Pass("two real CLI processes overlap: second refused while first owns app lock");
            Pass("kill dotnet worker during SQL WAITFOR transaction; rollback and retry");

            _stage = "schema9-data-readiness-before-bridge";
            var badStock = await PrepareDbUp(9);
            await Exec(badStock, "UPDATE dbo.StockBalances SET Quantity=Quantity+1;");
            var badLegacy = await JournalSignatureOfLegacy(badStock);
            var badRows = await SourceData(badStock, 9);
            await RunCli(badStock, 13, expectSuccess: false,
                expectedCode: "Migration.DataReadinessRefused");
            Assert(await JournalSignatureOfLegacy(badStock) == badLegacy,
                "VPS07B_DATA_INVALID_CHANGED_HISTORY");
            Assert(await SourceData(badStock, 9) == badRows,
                "VPS07B_DATA_INVALID_MUTATED_ROWS");
            Assert(await Count(badStock,
                "SELECT CASE WHEN OBJECT_ID(N'dbo.FluentMigratorVersionInfo',N'U') IS NULL THEN 0 ELSE 1 END;") == 0,
                "VPS07B_DATA_INVALID_BRIDGED_BEFORE_READINESS");
            Pass("schema9 invalid stock/ledger source refused before FM journal creation");

            // A real SQL Server BACKUP ... WITH CHECKSUM, RESTORE VERIFYONLY,
            // RESTORE ... WITH REPLACE is rehearsed only on an owned disposable
            // database. This is NOT protected-staging or production evidence.
            _stage = "owned-backup-restore-rehearsal";
            var backupDb = await PrepareDbUp(12);
            var preBackupRows = await SourceData(backupDb, 12);
            var preBackupJournal = await JournalSignatureOfLegacy(backupDb);
            var preBackupSchema = await DefinitionSignature(backupDb);
            var backupPath = "/var/opt/mssql/data/" + backupDb + ".bak";
            await ExecMaster("BACKUP DATABASE [" + backupDb + "] TO DISK=N'" +
                backupPath + "' WITH INIT, FORMAT, CHECKSUM;");
            await ExecMaster("RESTORE VERIFYONLY FROM DISK=N'" + backupPath +
                "' WITH CHECKSUM;");
            await AwaitMaintenanceDrain(backupDb);
            await RunCli(backupDb, 13, expectSuccess: true);
            await Check(backupDb, 13);
            await AssertStockAndCogs(backupDb);
            Assert(await SourceData(backupDb, 12) == preBackupRows,
                "VPS07B_PRE_RESTORE_DATA_MISMATCH");
            await ExecMaster("ALTER DATABASE [" + backupDb +
                "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;");
            await ExecMaster("RESTORE DATABASE [" + backupDb +
                "] FROM DISK=N'" + backupPath + "' WITH REPLACE, RECOVERY, CHECKSUM;");
            await ExecMaster("ALTER DATABASE [" + backupDb +
                "] SET RESTRICTED_USER WITH ROLLBACK IMMEDIATE;");
            await AwaitMaintenanceDrain(backupDb);
            Assert(await JournalSignatureOfLegacy(backupDb) == preBackupJournal,
                "VPS07B_RESTORE_JOURNAL_MISMATCH");
            Assert(await SourceData(backupDb, 12) == preBackupRows,
                "VPS07B_RESTORE_DATA_MISMATCH");
            Assert(await DefinitionSignature(backupDb) == preBackupSchema,
                "VPS07B_RESTORE_SCHEMA_MISMATCH");
            Assert(await Count(backupDb,
                "SELECT CASE WHEN OBJECT_ID(N'dbo.FluentMigratorVersionInfo',N'U') IS NULL THEN 0 ELSE 1 END;") == 0,
                "VPS07B_RESTORE_STALE_FM_JOURNAL");
            await RunCli(backupDb, 13, expectSuccess: true);
            await Check(backupDb, 13);
            Assert(await SourceData(backupDb, 12) == preBackupRows,
                "VPS07B_POST_RESTORE_RETRY_DATA_MISMATCH");
            Assert(await DefinitionSignature(backupDb) == baselineSignature,
                "VPS07B_POST_RESTORE_RETRY_SCHEMA_MISMATCH");
            Pass("owned SQL12 backup/checksum/verify/restore/retry to SQL13");

            // Adversarial and concurrent admission probes use a dedicated,
            // already-upgraded disposable database. They never alter a user DB.
            var denied = baseline;
            var stable = await JournalSignature(denied);
            await Exec(denied, "UPDATE dbo.FluentMigratorVersionInfo SET Description=N'TAMPERED' WHERE Version=1;");
            await RunCli(denied, 13, expectSuccess: false,
                expectedCode: "Migration.UnknownFluentMigratorJournal");
            Assert(await Count(denied, "SELECT COUNT(*) FROM dbo.FluentMigratorVersionInfo;") == 13,
                "VPS07B_TAMPERED_JOURNAL_CHANGED");
            await Exec(denied, "UPDATE dbo.FluentMigratorVersionInfo SET Description=N'001_PersistenceFoundation.sql' WHERE Version=1;");
            Assert(await JournalSignature(denied) == stable, "VPS07B_TAMPER_NOT_RESTORED");
            Pass("tampered journal refused before mutation");

            await Exec(denied, "CREATE TABLE dbo.Vps07bUnknownObject(Id int NOT NULL);");
            await RunCli(denied, 13, expectSuccess: false,
                expectedCode: "Migration.NativeSchemaFingerprintRefused");
            await Exec(denied, "DROP TABLE dbo.Vps07bUnknownObject;");
            Assert(await JournalSignature(denied) == stable,
                "VPS07B_UNKNOWN_SCHEMA_CHANGED_HISTORY");
            Pass("unknown object refused before mutation");

            await Exec(denied, "GRANT SELECT ON dbo.Users TO public;");
            await RunCli(denied, 13, expectSuccess: false,
                expectedCode: "Migration.UnsafePublicGrantRefused");
            await Exec(denied, "REVOKE SELECT ON dbo.Users FROM public;");
            Assert(await JournalSignature(denied) == stable,
                "VPS07B_PUBLIC_BUSINESS_GRANT_MUTATED_HISTORY");
            Pass("unsafe public grant on business table refused before migration");

            await Exec(denied, "GRANT SELECT ON SCHEMA::dbo TO public;");
            await RunCli(denied, 13, expectSuccess: false,
                expectedCode: "Migration.UnsafePublicGrantRefused");
            await Exec(denied, "REVOKE SELECT ON SCHEMA::dbo FROM public;");
            Assert(await JournalSignature(denied) == stable,
                "VPS07B_PUBLIC_SCHEMA_GRANT_MUTATED_HISTORY");
            Pass("inherited PUBLIC schema SELECT refused before mutation");

            await Exec(denied, "GRANT SELECT TO public;");
            await RunCli(denied, 13, expectSuccess: false,
                expectedCode: "Migration.UnsafePublicGrantRefused");
            await Exec(denied, "REVOKE SELECT FROM public;");
            Assert(await JournalSignature(denied) == stable,
                "VPS07B_PUBLIC_DATABASE_GRANT_MUTATED_HISTORY");
            Pass("inherited PUBLIC database SELECT refused before mutation");

            // Forged versions, unexpected objects, mis-shaped journals and
            // future legacy history are refused without replay or trust bypass.
            _stage = "future-and-tampered-journals";
            await Exec(denied, @"INSERT dbo.FluentMigratorVersionInfo(Version,AppliedOn,Description)
VALUES(14,GETUTCDATE(),N'014_UnknownFuture.sql');");
            await RunCli(denied, 13, expectSuccess: false,
                expectedCode: "Migration.UnknownFluentMigratorJournal");
            Assert(await Count(denied, "SELECT COUNT(*) FROM dbo.FluentMigratorVersionInfo") == 14,
                "VPS07B_FUTURE_FM_ADMISSION_MUTATED");
            await Exec(denied, "DELETE dbo.FluentMigratorVersionInfo WHERE Version=14;");
            Assert(await JournalSignature(denied) == stable,
                "VPS07B_FUTURE_FM_CLEANUP_MISMATCH");
            Pass("future FM journal refused");

            await Exec(denied, @"INSERT dbo.SchemaVersions(ScriptName,Applied)
VALUES(N'MyDmsVn.Server.DbMigrator.Scripts.014_Unexpected.sql',GETDATE());");
            await RunCli(denied, 13, expectSuccess: false,
                expectedCode: "Migration.FutureLegacyJournal");
            await Exec(denied, "DELETE dbo.SchemaVersions WHERE ScriptName LIKE N'%014_Unexpected.sql';");
            Assert(await JournalSignature(denied) == stable,
                "VPS07B_FUTURE_DBUP_CLEANUP_MISMATCH");
            Pass("future DbUp journal refused");

            await Exec(denied, @"UPDATE dbo.SchemaVersions
SET ScriptName=N'001_PersistenceFoundation.sql'
WHERE Id=(SELECT MIN(Id) FROM dbo.SchemaVersions);");
            await RunCli(denied, 13, expectSuccess: false,
                expectedCode: "Migration.UnknownOrReorderedLegacyJournal");
            await Exec(denied, @"UPDATE dbo.SchemaVersions
SET ScriptName=N'MyDmsVn.Server.DbMigrator.Scripts.001_PersistenceFoundation.sql'
WHERE Id=(SELECT MIN(Id) FROM dbo.SchemaVersions);");
            Assert(await JournalSignature(denied) == stable,
                "VPS07B_NONCANONICAL_LEGACY_RESTORATION_MISMATCH");
            Pass("bare SQL script alias is not canonical DbUp journal provenance");

            await Exec(denied, "ALTER TABLE dbo.FluentMigratorVersionInfo ADD InjectedFlag bit NULL;");
            await RunCli(denied, 13, expectSuccess: false,
                expectedCode: "Migration.FluentMigratorJournalDefinitionMismatch");
            Assert(await Count(denied, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.FluentMigratorVersionInfo') AND name=N'InjectedFlag';") == 1,
                "VPS07B_BAD_FM_SHAPE_UNEXPECTED_MUTATION");
            await Exec(denied, "ALTER TABLE dbo.FluentMigratorVersionInfo DROP COLUMN InjectedFlag;");
            Assert(await JournalSignature(denied) == stable,
                "VPS07B_BAD_FM_SHAPE_CLEANUP_MISMATCH");
            await Exec(denied, "GRANT SELECT ON dbo.FluentMigratorVersionInfo TO public;");
            await RunCli(denied, 13, expectSuccess: false,
                expectedCode: "Migration.UnsafePublicGrantRefused");
            await Exec(denied, "REVOKE SELECT ON dbo.FluentMigratorVersionInfo FROM public;");
            Assert(await JournalSignature(denied) == stable,
                "VPS07B_PUBLIC_JOURNAL_GRANT_CLEANUP_MISMATCH");
            Pass("tampered FM table definition and public journal grant refused");

            var bogus = await Create();
            await Exec(bogus, "CREATE VIEW dbo.FluentMigratorVersionInfo AS SELECT CAST(1 AS bigint) AS Version;");
            await RunCli(bogus, 13, expectSuccess: false,
                expectedCode: "Migration.UnknownExistingObjects");
            Assert(await Count(bogus,
                "SELECT CASE WHEN OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL THEN 0 ELSE 1 END;") == 0,
                "VPS07B_BOGUS_VIEW_CREATED_LEGACY_JOURNAL");
            Pass("FM journal name spoofed by view refused");

            var orphan = await Create();
            await Exec(orphan, @"CREATE TABLE dbo.FluentMigratorVersionInfo
(Version bigint NOT NULL, AppliedOn datetime NULL, Description nvarchar(1024) NULL);
CREATE UNIQUE CLUSTERED INDEX UC_FluentMigratorVersion
ON dbo.FluentMigratorVersionInfo(Version);");
            await RunCli(orphan, 13, expectSuccess: false,
                expectedCode: "Migration.OrphanFluentMigratorJournal");
            Assert(await Count(orphan,
                "SELECT CASE WHEN OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL THEN 0 ELSE 1 END;") == 0,
                "VPS07B_ORPHAN_JOURNAL_CREATED_LEGACY");
            Pass("FM journal without DbUp provenance refused");

            // A second durable database-level proof must survive the bridge:
            // deleting the FM table after cutover is NOT a legitimate
            // first-time DbUp source and must never auto-restamp 1..13.
            _stage = "missing-after-bridge-and-marker-tamper";
            var missingFm = await PrepareDbUp(12);
            await RunCli(missingFm, 13, expectSuccess: true);
            await Check(missingFm, 13);
            var retained = await SourceData(missingFm, 12);
            var retainedLegacy = await JournalSignatureOfLegacy(missingFm);
            await Exec(missingFm, "DROP TABLE dbo.FluentMigratorVersionInfo;");
            await RunCli(missingFm, 13, expectSuccess: false,
                expectedCode: "Migration.MissingFluentMigratorJournalAfterBridge");
            Assert(await Count(missingFm,
                "SELECT CASE WHEN OBJECT_ID(N'dbo.FluentMigratorVersionInfo',N'U') IS NULL THEN 0 ELSE 1 END;") == 0,
                "VPS07B_MISSING_FM_JOURNAL_RECREATED");
            Assert(await JournalSignatureOfLegacy(missingFm) == retainedLegacy &&
                await SourceData(missingFm, 12) == retained,
                "VPS07B_MISSING_FM_JOURNAL_MUTATED_BUSINESS");
            Pass("previously bridged database with deleted FM journal refused");

            await Exec(denied, @"EXEC sys.sp_updateextendedproperty
@name=N'MyDmsVn.FluentMigratorBridge',@value=N'forged';");
            await RunCli(denied, 13, expectSuccess: false,
                expectedCode: "Migration.TamperedFluentMigratorBridgeMarker");
            await Exec(denied, @"EXEC sys.sp_updateextendedproperty
@name=N'MyDmsVn.FluentMigratorBridge',@value=N'vps07b-v1';");
            Assert(await JournalSignature(denied) == stable,
                "VPS07B_TAMPERED_MARKER_RESTORATION_FAILED");
            Pass("forged durable cutover marker refused");

            await Exec(denied, @"EXEC sys.sp_dropextendedproperty
@name=N'MyDmsVn.FluentMigratorBridge';");
            await RunCli(denied, 13, expectSuccess: false,
                expectedCode: "Migration.MissingFluentMigratorBridgeMarker");
            await Exec(denied, @"EXEC sys.sp_addextendedproperty
@name=N'MyDmsVn.FluentMigratorBridge',@value=N'vps07b-v1';");
            Assert(await JournalSignature(denied) == stable,
                "VPS07B_MISSING_MARKER_RESTORATION_FAILED");
            Pass("missing marker with present FM journal refused");

            // Separate session owns the app lock. Runner must fail immediately.
            using (var locked = new SqlConnection(For(denied)))
            {
                await locked.OpenAsync();
                using var acquire = new SqlCommand(@"DECLARE @r int;
EXEC @r=sys.sp_getapplock @Resource=N'MyDmsVn.SchemaMigration',
 @LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0; SELECT @r;", locked);
                Assert(Convert.ToInt32(await acquire.ExecuteScalarAsync(), CultureInfo.InvariantCulture) >= 0,
                    "VPS07B_ORACLE_LOCK_FAILED");
                await RunCli(denied, 13, expectSuccess: false,
                    expectedCode: "Migration.AlreadyRunning");
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
            await RunCli(partial, 13, expectSuccess: false,
                expectedCode: "Migration.NativeSchemaFingerprintRefused");
            Assert(await JournalSignatureOfLegacy(partial) == partialBefore,
                "VPS07B_PARTIAL_SCHEMA_CHANGED_JOURNAL");
            Pass("partial schema rejected with legacy journal unchanged");

            _stage = "dedicated-control-principal-positive";
            var controlledDb = await PrepareDbUp(12);
            var migrationLogin = "vps07b_ci_control_" + Stamp;
            var migrationPassword = "CiDedicated@9" + Guid.NewGuid().ToString("N");
            await ExecMaster("CREATE LOGIN [" + migrationLogin + "] WITH PASSWORD=N'" +
                migrationPassword + "',CHECK_POLICY=ON;" +
                "GRANT VIEW SERVER STATE TO [" + migrationLogin + "];" +
                "GRANT VIEW ANY DEFINITION TO [" + migrationLogin + "];" +
                "GRANT VIEW SERVER PERFORMANCE STATE TO [" + migrationLogin + "];");
            await Exec(controlledDb, "CREATE USER [" + migrationLogin + "] FOR LOGIN [" +
                migrationLogin + "]; GRANT CONTROL ON DATABASE::[" + controlledDb + "] TO [" +
                migrationLogin + "]; ALTER ROLE db_owner ADD MEMBER [" + migrationLogin + "];");
            // RESTRICTED_USER admits db_owner membership. The dedicated
            // migration login is not sysadmin or used by the HTTP runtime.
            var controlledConnection = new SqlConnectionStringBuilder(For(controlledDb))
            {
                UserID = migrationLogin,
                Password = migrationPassword
            }.ConnectionString;
            var controlledData = await SourceData(controlledDb, 12);
            await RunCli(controlledDb, 13, expectSuccess: true,
                connectionOverride: controlledConnection);
            await Check(controlledDb, 13);
            await AssertStockAndCogs(controlledDb);
            Assert(await SourceData(controlledDb, 12) == controlledData,
                "VPS07B_CONTROL_IDENTITY_CHANGED_BUSINESS_DATA");
            var committedHistory = await JournalSignature(controlledDb);
            await RunCli(controlledDb, 13, expectSuccess: true,
                connectionOverride: controlledConnection);
            Assert(await JournalSignature(controlledDb) == committedHistory,
                "VPS07B_CONTROL_IDENTITY_NOOP_REWROTE_JOURNAL");
            await Exec(controlledDb, "ALTER ROLE db_owner DROP MEMBER [" +
                migrationLogin + "]; DROP USER [" + migrationLogin + "];");
            await ExecMaster("DROP LOGIN [" + migrationLogin + "];");
            Pass("dedicated CONTROL SQL login restricted upgrade and idempotent no-op");

            _stage = "dedicated-migration-principal-control-negative";
            var underprivileged = await Create();
            var deniedLogin = "vps07b_ci_denied_" + Stamp;
            var deniedPassword = "CiNoControl@9" + Guid.NewGuid().ToString("N");
            await ExecMaster("CREATE LOGIN [" + deniedLogin + "] WITH PASSWORD=N'" +
                deniedPassword + "',CHECK_POLICY=ON;");
            await Exec(underprivileged, "CREATE USER [" + deniedLogin +
                "] FOR LOGIN [" + deniedLogin + "]; GRANT CONNECT TO [" + deniedLogin + "];");
            var withoutControl = new SqlConnectionStringBuilder(For(underprivileged))
            {
                UserID = deniedLogin,
                Password = deniedPassword
            }.ConnectionString;
            await RunCli(underprivileged, 13, expectSuccess: false,
                expectedCode: "Migration.ControlPermissionRequired", connectionOverride: withoutControl);
            Assert(await Count(underprivileged,
                "SELECT CASE WHEN OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL THEN 0 ELSE 1 END;") == 0,
                "VPS07B_UNPRIVILEGED_CREATED_LEGACY_JOURNAL");
            Assert(await Count(underprivileged,
                "SELECT CASE WHEN OBJECT_ID(N'dbo.FluentMigratorVersionInfo',N'U') IS NULL THEN 0 ELSE 1 END;") == 0,
                "VPS07B_UNPRIVILEGED_CREATED_FM_JOURNAL");
            await Exec(underprivileged, "DROP USER [" + deniedLogin + "];");
            await ExecMaster("DROP LOGIN [" + deniedLogin + "];");
            await RunCli(denied, 13, expectSuccess: false,
                expectedCode: "Migration.DedicatedControlPrincipalRequired",
                forceOutsideCi: true);
            Assert(await JournalSignature(denied) == stable,
                "VPS07B_PRODUCTION_SYSADMIN_REFUSAL_MUTATED");
            Pass("migration identity without CONTROL and sysadmin outside ephemeral CI refused");

            // Explicitly test no operator approval even with valid disposable SQL.
            await RunCli(denied, 13, expectSuccess: false, approved: false,
                expectedCode: "Migration.OperatorApprovalRequired");
            Assert(await JournalSignature(denied) == stable, "VPS07B_UNAPPROVED_MUTATED");
            Pass("operator consent missing refused");

            Assert(_executed == RequiredSqlCaseCount && _passed == RequiredSqlCaseCount,
                "VPS07B_REQUIRED_CASE_COUNT_MISMATCH");
            Console.WriteLine("VPS07B_REAL_SQL_PASS: discovered=" + RequiredSqlCaseCount +
                "; executed=" + _executed + "; passed=" +
                _passed + "; failed=0; skipped=0; SQL2022 verified TLS; ephemeral.");
            return 0;
        }
        catch (Exception ex)
        {
            // Test-only diagnostics print safe fixed-category names, never
            // connection strings, SQL source text, login or any row data.
            Console.Error.WriteLine("VPS07B_REAL_SQL_FAILED: executed=" + _executed +
                "; passed=" + _passed + "; failed=1; skipped=0; type=" +
                ex.GetType().Name + "; stage=" + _stage + "; sql=" + (ex is SqlException q ? q.Number.ToString(CultureInfo.InvariantCulture) : "none") + "; sqlLine=" + (ex is SqlException lineError ? lineError.LineNumber.ToString(CultureInfo.InvariantCulture) : "none") + "; code=" + SafeCode(ex.Message) + "; cli=" +
                (ex is InvalidOperationException && ex.Message.StartsWith("VPS07B_CLI_FAILED_", StringComparison.Ordinal)
                    ? ex.Message.Substring(0, Math.Min(190, ex.Message.Length)) : "none"));
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
SET @ledger=SCOPE_IDENTITY();");
        // Use separate SQL commands. SQL Server binds column names in both IF
        // branches at compile time: mentioning InventoryValue on schema9 fails
        // with SQL207 even when the conditional branch would not be executed.
        if (await Count(db, "SELECT CASE WHEN COL_LENGTH(N'dbo.StockBalances',N'InventoryValue') IS NULL THEN 0 ELSE 1 END;") == 0)
            await Exec(db, @"INSERT dbo.StockBalances(WarehouseId,ProductId,Quantity)
SELECT WarehouseId,ProductId,CAST(4 AS decimal(18,4))
FROM dbo.Warehouses CROSS JOIN dbo.Products;");
        else
            await Exec(db, @"INSERT dbo.StockBalances(WarehouseId,ProductId,Quantity,InventoryValue)
SELECT WarehouseId,ProductId,CAST(4 AS decimal(18,4)),CAST(10 AS decimal(28,4))
FROM dbo.Warehouses CROSS JOIN dbo.Products;
INSERT dbo.StockValuationEntries(LedgerId,ValueChange)
SELECT LedgerId,CAST(10 AS decimal(28,4)) FROM dbo.StockLedger;");

        if (await Count(db, "SELECT CASE WHEN OBJECT_ID(N'dbo.SalesReceipts',N'U') IS NULL THEN 0 ELSE 1 END;") == 1)
            await Exec(db, @"
DECLARE @actor int=(SELECT UserId FROM dbo.Users WHERE Username=N'ci-operator');
DECLARE @product int=(SELECT ProductId FROM dbo.Products WHERE Code=N'CI-PROD');
DECLARE @warehouse int=(SELECT WarehouseId FROM dbo.Warehouses WHERE Code=N'CI-WH');
DECLARE @employee int=(SELECT EmployeeId FROM dbo.Employees WHERE Code=N'CI-EMP');
DECLARE @customer int=(SELECT CustomerId FROM dbo.Customers WHERE Code=N'CI-CUST');
DECLARE @posted datetime2(7)=SYSUTCDATETIME();
DECLARE @sale bigint,@line bigint,@ledger bigint;
INSERT dbo.SalesReceipts(SaleNo,SaleDate,WarehouseId,EmployeeId,CustomerId,
 Status,CreatedByUserId,PostedAtUtc,PostedByUserId)
VALUES(N'CI-SALE',CONVERT(date,@posted),@warehouse,@employee,@customer,
 'Posted',@actor,@posted,@actor);
SET @sale=SCOPE_IDENTITY();
INSERT dbo.SalesReceiptLines(SaleId,[LineNo],ProductId,Qty,Unit,IsGift,
 LineAmount,PostedUnitCost,CostAmount,UnitPrice,CreatedByUserId)
VALUES(@sale,1,@product,CAST(1 AS decimal(18,4)),N'EA',0,
 CAST(7 AS decimal(28,4)),CAST(2.5 AS decimal(28,8)),CAST(2.5 AS decimal(28,4)),
 CAST(7 AS decimal(19,4)),@actor);
SET @line=SCOPE_IDENTITY();
INSERT dbo.StockLedger(WarehouseId,ProductId,DocumentType,DocumentId,DocumentLineId,
 QuantityChange,PostedAtUtc,PostedByUserId)
VALUES(@warehouse,@product,'SalesReceipt',@sale,@line,
 CAST(-1 AS decimal(18,4)),@posted,@actor);
SET @ledger=SCOPE_IDENTITY();
INSERT dbo.StockValuationEntries(LedgerId,ValueChange)
VALUES(@ledger,CAST(-2.5 AS decimal(28,4)));
UPDATE dbo.StockBalances
SET Quantity=Quantity-CAST(1 AS decimal(18,4)),
 InventoryValue=InventoryValue-CAST(2.5 AS decimal(28,4))
WHERE WarehouseId=@warehouse AND ProductId=@product;");

        if (await Count(db, "SELECT CASE WHEN OBJECT_ID(N'dbo.WebAuthSessions',N'U') IS NULL THEN 0 ELSE 1 END;") == 1)
            await Exec(db, @"
DECLARE @actor int = (SELECT UserId FROM dbo.Users WHERE Username=N'ci-operator');
DECLARE @sid uniqueidentifier=NEWID();
DECLARE @start datetime2(7)=DATEADD(hour,-1,SYSUTCDATETIME());
INSERT dbo.WebAuthSessions(SessionId,UserId,CreatedAtUtc,AbsoluteExpiresAtUtc,IdleExpiresAtUtc)
VALUES(@sid,@actor,@start,DATEADD(day,20,@start),DATEADD(day,7,@start));
INSERT dbo.WebRefreshTokens(TokenHash,SessionId,IssuedAtUtc,ExpiresAtUtc)
VALUES(HASHBYTES('SHA2_256',CONVERT(varbinary(16),@sid)),@sid,
 @start,DATEADD(day,10,@start));");

        if (await Count(db, "SELECT CASE WHEN OBJECT_ID(N'dbo.WebOperationJournal',N'U') IS NULL THEN 0 ELSE 1 END;") == 1)
            await Exec(db, @"
DECLARE @epoch uniqueidentifier=(SELECT DeploymentEpoch FROM dbo.WebDeploymentState WHERE Id=1);
DECLARE @actor int=(SELECT UserId FROM dbo.Users WHERE Username=N'ci-operator');
DECLARE @op uniqueidentifier=NEWID(), @now datetime2(7)=SYSUTCDATETIME();
INSERT dbo.WebOperationJournal(ActorUserId,OperationId,DeploymentEpoch,ContractVersion,
 CodecVersion,OperationKind,RequestTargetId,RequestFingerprint,ResultDocumentId,
 Disposition,ResultBytes,CreatedAtUtc,CompletedAtUtc,ResultExpiresAtUtc)
VALUES(@actor,@op,@epoch,1,1,1,NULL,
 HASHBYTES('SHA2_256',CONVERT(varbinary(16),@op)),1,
 'COMMITTED',0x01,@now,@now,DATEADD(day,7,@now));");

        if (await Count(db, "SELECT CASE WHEN OBJECT_ID(N'dbo.WebOperationTerminalAudit',N'U') IS NULL THEN 0 ELSE 1 END;") == 1)
            await Exec(db, @"
DECLARE @epoch uniqueidentifier=(SELECT DeploymentEpoch FROM dbo.WebDeploymentState WHERE Id=1);
DECLARE @actor int=(SELECT UserId FROM dbo.Users WHERE Username=N'ci-operator');
DECLARE @op uniqueidentifier=NEWID(),@decision uniqueidentifier=NEWID();
DECLARE @now datetime2(7)=SYSUTCDATETIME();
DECLARE @fingerprint binary(32)=HASHBYTES('SHA2_256',CONVERT(varbinary(16),@op));
INSERT dbo.WebOperationTerminalAudit(ActorUserId,OperationId,DeploymentEpoch,
 OperationKind,RequestTargetId,RequestFingerprint,DecisionId,CaseId,ReasonCode,
 EvidenceReference,OperatorDatabaseUser,OperatorLogin,DecidedAtUtc)
VALUES(@actor,@op,@epoch,1,NULL,@fingerprint,@decision,
 'CI_CASE_07B','CI_NOT_EXECUTED','urn:ci:vps07b',USER_NAME(),ORIGINAL_LOGIN(),@now);
INSERT dbo.WebOperationJournal(ActorUserId,OperationId,DeploymentEpoch,ContractVersion,
 CodecVersion,OperationKind,RequestTargetId,RequestFingerprint,ResultDocumentId,
 Disposition,ResultBytes,CreatedAtUtc,CompletedAtUtc,ResultExpiresAtUtc)
VALUES(@actor,@op,@epoch,1,1,1,NULL,@fingerprint,NULL,'CANCELLED',
 NULL,@now,@now,NULL);");
    }

    private static async Task<string> SourceData(string db, int preservedVersion = 9)
    {
        var data = await Text(db, @"SELECT CONCAT(
(SELECT Code,Name,Unit FROM dbo.Products ORDER BY ProductId FOR JSON PATH),
(SELECT Code,Name FROM dbo.Customers ORDER BY CustomerId FOR JSON PATH),
(SELECT Code,Name FROM dbo.Warehouses ORDER BY WarehouseId FOR JSON PATH),
(SELECT Code,Name FROM dbo.Employees ORDER BY EmployeeId FOR JSON PATH),
(SELECT CONVERT(varchar(32),Quantity) AS Qty FROM dbo.StockBalances ORDER BY WarehouseId,ProductId FOR JSON PATH),
(SELECT ReceiptNo,Status FROM dbo.GoodsReceipts ORDER BY ReceiptId FOR JSON PATH),
(SELECT Quantity,UnitCost FROM dbo.GoodsReceiptLines ORDER BY ReceiptLineId FOR JSON PATH),
(SELECT DocumentType,QuantityChange FROM dbo.StockLedger ORDER BY LedgerId FOR JSON PATH),
(SELECT UserId,Username,IsActive FROM dbo.Users ORDER BY UserId FOR JSON PATH),
(SELECT RoleId,RoleName,IsActive FROM dbo.Roles ORDER BY RoleId FOR JSON PATH),
(SELECT RoleId,PermissionKey FROM dbo.RolePermissions ORDER BY RolePermissionId FOR JSON PATH),
(SELECT UserId,PermissionKey,Granted FROM dbo.UserPermissions ORDER BY UserPermissionId FOR JSON PATH)
);");
        if (preservedVersion >= 10)
            data += await Text(db, @"SELECT CONCAT(
 (SELECT WarehouseId,ProductId,InventoryValue FROM dbo.StockBalances
   ORDER BY WarehouseId,ProductId FOR JSON PATH),
 (SELECT LedgerId,ValueChange FROM dbo.StockValuationEntries
   ORDER BY LedgerId FOR JSON PATH),
 (SELECT SaleId,SaleNo,SaleDate,Status,PostedAtUtc,PostedByUserId
   FROM dbo.SalesReceipts ORDER BY SaleId FOR JSON PATH),
 (SELECT SaleLineId,SaleId,Qty,IsGift,LineAmount,PostedUnitCost,CostAmount,UnitPrice
   FROM dbo.SalesReceiptLines ORDER BY SaleLineId FOR JSON PATH));");
        if (preservedVersion >= 11)
            data += await Text(db, @"SELECT CONCAT(
 (SELECT SessionId,UserId,CreatedAtUtc,AbsoluteExpiresAtUtc,IdleExpiresAtUtc,RevokedAtUtc
   FROM dbo.WebAuthSessions ORDER BY SessionId FOR JSON PATH),
 (SELECT TokenHash,SessionId,IssuedAtUtc,ExpiresAtUtc,ConsumedAtUtc,ReplacedByTokenHash,RevokedAtUtc
   FROM dbo.WebRefreshTokens ORDER BY TokenHash FOR JSON PATH));");
        if (preservedVersion >= 12)
            data += await Text(db, @"SELECT CONCAT(
 (SELECT Id,DeploymentEpoch,RotatedAtUtc FROM dbo.WebDeploymentState ORDER BY Id FOR JSON PATH),
 (SELECT ActorUserId,OperationId,DeploymentEpoch,OperationKind,RequestFingerprint,
     Disposition,ResultBytes,ResultDocumentId,CreatedAtUtc,CompletedAtUtc,ResultExpiresAtUtc
   FROM dbo.WebOperationJournal ORDER BY ActorUserId,OperationId FOR JSON PATH));");
        if (preservedVersion >= 13)
            data += await Text(db, @"SELECT
 (SELECT ActorUserId,OperationId,DeploymentEpoch,DecisionId,CaseId,ReasonCode,
   EvidenceReference,OperatorDatabaseUser,OperatorLogin,DecidedAtUtc
  FROM dbo.WebOperationTerminalAudit ORDER BY ActorUserId,OperationId FOR JSON PATH);");
        return Hash(data);
    }

    private static async Task AssertStockAndCogs(string db)
    {
        // Independent reconciliation, not the application UoW. Decimal sums,
        // posted-line COGS and both stock balances must agree after migration.
        var compatible = await Count(db, @"
SELECT CASE WHEN EXISTS(
 SELECT 1 FROM dbo.StockBalances b
 OUTER APPLY (SELECT SUM(CONVERT(decimal(38,4),QuantityChange)) Qty
   FROM dbo.StockLedger e WHERE e.WarehouseId=b.WarehouseId AND e.ProductId=b.ProductId) q
 OUTER APPLY (SELECT SUM(CONVERT(decimal(38,4),v.ValueChange)) Value
   FROM dbo.StockLedger e JOIN dbo.StockValuationEntries v ON v.LedgerId=e.LedgerId
   WHERE e.WarehouseId=b.WarehouseId AND e.ProductId=b.ProductId) val
 WHERE b.Quantity<>ISNULL(q.Qty,0) OR b.InventoryValue<>ISNULL(val.Value,0)
) OR EXISTS(
 SELECT 1 FROM dbo.SalesReceiptLines l JOIN dbo.SalesReceipts h ON h.SaleId=l.SaleId
 WHERE h.Status='Posted' AND (l.PostedUnitCost IS NULL OR l.CostAmount IS NULL
   OR l.CostAmount<>ROUND(l.PostedUnitCost*l.Qty,4))
) THEN 0 ELSE 1 END;");
        Assert(compatible == 1, "VPS07B_INDEPENDENT_STOCK_COGS_RECONCILIATION_FAILED");
    }

    private static async Task<string> DefinitionSignature(string db) =>
        Hash(string.Join("\n", await DefinitionSignatureRows(db)));

    private static async Task<List<string>> DefinitionSignatureRows(string db)
    {
        // Independent SQL catalog oracle: exclude FM journal only, then compare
        // live object definitions, column/index signatures and routines against
        // a separately installed original DbUp13 reference database.
        var signatures = new List<string>();
        using var c = new SqlConnection(For(db));
        await c.OpenAsync();
        using var command = new SqlCommand(@"
SELECT CONCAT(N'O|',SCHEMA_NAME(schema_id),N'|',name,N'|',type) COLLATE Latin1_General_100_BIN2 AS Sig
FROM sys.objects WHERE is_ms_shipped=0 AND name<>N'FluentMigratorVersionInfo'
UNION ALL SELECT CONCAT(N'C|',OBJECT_SCHEMA_NAME(object_id),N'|',OBJECT_NAME(object_id),
 N'|',column_id,N'|',name,N'|',TYPE_NAME(user_type_id),N'|',max_length,
 N'|',precision,N'|',scale,N'|',is_nullable) COLLATE Latin1_General_100_BIN2
FROM sys.columns WHERE object_id<>ISNULL(OBJECT_ID(N'dbo.FluentMigratorVersionInfo'),-1)
 AND OBJECTPROPERTY(object_id,'IsMSShipped')=0
UNION ALL SELECT CONCAT(N'M|',OBJECT_SCHEMA_NAME(object_id),N'|',OBJECT_NAME(object_id),
 N'|',CONVERT(varchar(64),HASHBYTES('SHA2_256',definition),2)) COLLATE Latin1_General_100_BIN2
FROM sys.sql_modules WHERE OBJECTPROPERTY(object_id,'IsMSShipped')=0
UNION ALL SELECT CONCAT(N'I|',OBJECT_SCHEMA_NAME(object_id),N'|',OBJECT_NAME(object_id),
 N'|',name,N'|',is_unique,N'|',is_primary_key,N'|',has_filter) COLLATE Latin1_General_100_BIN2
FROM sys.indexes WHERE OBJECTPROPERTY(object_id,'IsMSShipped')=0
 AND object_id<>ISNULL(OBJECT_ID(N'dbo.FluentMigratorVersionInfo'),-1)
UNION ALL SELECT CONCAT(N'T|',name,N'|',is_disabled,N'|',is_instead_of_trigger) COLLATE Latin1_General_100_BIN2
FROM sys.triggers WHERE is_ms_shipped=0
UNION ALL SELECT CONCAT(N'R|',p.name,N'|',p.type_desc) COLLATE Latin1_General_100_BIN2
FROM sys.database_principals p WHERE p.type=N'R' AND p.name LIKE N'MyDmsVn%'
UNION ALL SELECT CONCAT(N'P|',USER_NAME(p.grantee_principal_id),N'|',p.permission_name,
 N'|',p.state_desc,N'|',SCHEMA_NAME(o.schema_id),N'.',o.name)
 COLLATE Latin1_General_100_BIN2
FROM sys.database_permissions p
JOIN sys.objects o ON p.class=1 AND p.major_id=o.object_id
WHERE p.grantee_principal_id=DATABASE_PRINCIPAL_ID(N'MyDmsVnWebOperationOperatorRole')
ORDER BY Sig;", c);
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            signatures.Add(reader.GetString(0));
        return signatures;
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
            Assert(name == "MyDmsVn.Server.DbMigrator.Scripts." + Scripts[index],
                "VPS07B_LEGACY_JOURNAL_UNKNOWN");
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

    private static async Task RunCli(string db, int version, bool expectSuccess, bool approved = true,
        string? ciFault = null, string? expectedCode = null, string? connectionOverride = null,
        bool interruptAtWaitfor = false, bool forceOutsideCi = false)
    {
        var psi = new ProcessStartInfo("dotnet") { RedirectStandardError = true,
            RedirectStandardOutput = true, UseShellExecute = false };
        psi.ArgumentList.Add(_cli);
        psi.ArgumentList.Add("migrate");
        psi.ArgumentList.Add("schema" + version.ToString(CultureInfo.InvariantCulture));
        if (connectionOverride != null)
        {
            var check = new SqlConnectionStringBuilder(connectionOverride);
            Assert(Databases.Contains(db, StringComparer.Ordinal) &&
                check.InitialCatalog == db &&
                string.Equals(check.DataSource, "localhost,1433", StringComparison.OrdinalIgnoreCase) &&
                check.Encrypt.ToString() == "True" && !check.TrustServerCertificate,
                "VPS07B_CI_CONNECTION_OVERRIDE_OUTSIDE_OWNED_SQL");
        }
        psi.Environment["VPS07B_SQL_CONNECTION_STRING"] = connectionOverride ?? For(db);
        psi.Environment["VPS07B_OPERATOR_APPROVAL"] =
            approved ? "approved-maintenance-and-verified-backup" : "";
        if (forceOutsideCi)
        {
            // Exercise the production principal guard in a disposable SQL
            // database; never use this override for an actual write test.
            Assert(!expectSuccess && ciFault == null, "VPS07B_NONCI_PROBE_UNSAFE");
            psi.Environment["CI"] = "false";
            psi.Environment["GITHUB_ACTIONS"] = "false";
            psi.Environment["VPS07B_CI_EPHEMERAL_SQL"] = "";
        }
        if (!string.IsNullOrEmpty(ciFault))
            psi.Environment["VPS07B_CI_FAULT"] = ciFault;
        else
            psi.Environment.Remove("VPS07B_CI_FAULT");
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("VPS07B_CHILD_NOT_STARTED");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var limit = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            if (interruptAtWaitfor)
            {
                Assert(!string.IsNullOrEmpty(ciFault) && ciFault.EndsWith(":hang", StringComparison.Ordinal),
                    "VPS07B_PROCESS_LOSS_REQUIRES_HANG_SENTINEL");
                var sawInFlightSql = false;
                for (var attempt = 0; attempt < 50; attempt++)
                {
                    Assert(!process.HasExited, "VPS07B_WORKER_EXITED_BEFORE_SQL_WAIT");
                    if (await Count(db, @"SELECT COUNT(*) FROM sys.dm_exec_requests r
WHERE r.session_id<>@@SPID AND r.database_id=DB_ID()
AND (r.wait_type LIKE N'WAITFOR%' OR r.command=N'WAITFOR');") > 0)
                    {
                        sawInFlightSql = true;
                        break;
                    }
                    await Task.Delay(300);
                }
                Assert(sawInFlightSql, "VPS07B_NO_IN_FLIGHT_SQL_WAIT_WAS_OBSERVED");
                // A second REAL CLI process must fail to take the same
                // app lock while the first process remains inside SQL.
                await RunCli(db, version, expectSuccess: false,
                    expectedCode: "Migration.AlreadyRunning");
                Assert(!process.HasExited, "VPS07B_FIRST_MIGRATOR_EXITED_DURING_RACE");
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("VPS07B_CHILD_TIMEOUT");
        }
        var output = await stdout;
        var diagnostic = await stderr;
        if (expectSuccess)
        {
            Assert(process.ExitCode == 0 && output.Contains("VPS07B_SQL_VERIFIED:",StringComparison.Ordinal),
                "VPS07B_CLI_FAILED_" + process.ExitCode + "_" + SafeCode(diagnostic) + "_" +
                System.Text.RegularExpressions.Regex.Match(diagnostic,
                    @"S=[0-9]+;R=[0-9]+;T=[0-9]+;SelfT=[0-9]+;L=[0-9]+").Value + "_" +
                new string((diagnostic.Split('\n').FirstOrDefault(x =>
                    x.StartsWith("Migration.Failed: stage=", StringComparison.Ordinal)) ?? "")
                    .Where(c => char.IsLetterOrDigit(c) || c == '=' || c == ';' ||
                        c == '.' || c == ' ' || c == '_').Take(160).ToArray()));
        }
        else
        {
            Assert(process.ExitCode != 0 && !output.Contains("VPS07B_SQL_VERIFIED:",StringComparison.Ordinal),
                "VPS07B_CLI_SHOULD_REFUSE");
            if (!string.IsNullOrEmpty(expectedCode))
                Assert(diagnostic.Contains(expectedCode, StringComparison.Ordinal),
                    "VPS07B_WRONG_REFUSAL_CATEGORY_" + SafeCode(diagnostic));
        }
    }

    private static async Task<string> JournalSignature(string db) =>
        Hash(await Text(db, @"SELECT CONCAT(
 (SELECT Id,ScriptName,CONVERT(varchar(33),Applied,126) AS Applied
   FROM dbo.SchemaVersions ORDER BY Id FOR JSON PATH),
 (SELECT Version,Description,CONVERT(varchar(33),AppliedOn,126) AS AppliedOn
   FROM dbo.FluentMigratorVersionInfo ORDER BY Version FOR JSON PATH),
 (SELECT CONVERT(nvarchar(32),value) AS BridgeMarker FROM sys.extended_properties
   WHERE class=0 AND major_id=0 AND minor_id=0
     AND name=N'MyDmsVn.FluentMigratorBridge' FOR JSON PATH));"));

    private static async Task<string> JournalSignatureOfLegacy(string db) =>
        Hash(await Text(db, @"SELECT Id,ScriptName,CONVERT(varchar(33),Applied,126) AS Applied
   FROM dbo.SchemaVersions ORDER BY Id FOR JSON PATH;"));

    private static async Task AwaitMaintenanceDrain(string db)
    {
        // Real SQL BACKUP/RESTORE may briefly retain engine lock/transaction
        // state. Never waive the original writer-drain preflight or kill users.
        // Observe a fully clear database DMV boundary before invoking CLI.
        for (var attempt = 0; attempt < 75; attempt++)
        {
            var busy = await Count(db, @"
SELECT
 (SELECT COUNT(*) FROM sys.dm_exec_sessions s
  WHERE s.is_user_process=1 AND s.session_id<>@@SPID AND s.database_id=DB_ID())
+(SELECT COUNT(*) FROM sys.dm_exec_requests r
  WHERE r.session_id<>@@SPID AND r.database_id=DB_ID())
+(SELECT COUNT(*) FROM sys.dm_tran_locks l
  WHERE l.request_session_id<>@@SPID AND l.resource_database_id=DB_ID())
+(SELECT COUNT(*) FROM sys.dm_tran_database_transactions t
  WHERE t.database_id=DB_ID() AND t.database_transaction_type<>3
  AND t.transaction_id<>(SELECT transaction_id FROM sys.dm_tran_current_transaction));");
            if (busy == 0) return;
            await Task.Delay(200);
        }
        throw new InvalidOperationException("VPS07B_SQL_BACKUP_RESTORE_NOT_DRAINED");
    }

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

    private static string CatalogKey(string signature)
    {
        var parts = signature.Split('|');
        var key = string.Join(".", parts.Take(Math.Min(3, parts.Length)));
        var safe = new string(key.Where(c => char.IsLetterOrDigit(c) || c == '.' || c == '_').Take(70).ToArray());
        return safe + ":" + Hash(signature).Substring(0, 12);
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
