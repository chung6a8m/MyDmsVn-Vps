using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.Data.SqlClient;
using MyDmsVn.Server.FluentMigrator;
using MyDmsVn.Server.Infrastructure.Operations;

namespace MyDmsVn.Server.FluentMigrator.Cli;

/// <summary>
/// The only write entry point for VPS-07B. A separate non-pooled SQL session
/// owns the same exclusive app lock used by the historical DbUp migrator.
/// It remains open through the complete FluentMigrator run, preflight and
/// post-commit native admission. No web-host migration hooks exist.
/// </summary>
internal sealed class Vps07bGuardedSession : IDisposable
{
    private const string LegacyPrefix = "MyDmsVn.Server.DbMigrator.Scripts.";
    private const string LockName = "MyDmsVn.SchemaMigration";
    private const string JournalName = "dbo.FluentMigratorVersionInfo";

    private readonly SqlConnection _lock;
    private readonly int _target;
    private bool _ownsLock;
    private bool _admitted;

    public Vps07bGuardedSession(string connectionString, int target)
    {
        var cs = new SqlConnectionStringBuilder(connectionString) { Pooling = false };
        if (string.IsNullOrWhiteSpace(cs.InitialCatalog) ||
            new[] { "master", "model", "msdb", "tempdb" }.Any(db =>
                string.Equals(cs.InitialCatalog.Trim(), db, StringComparison.OrdinalIgnoreCase)))
            throw new Vps07bRefusal("Migration.SystemOrUnnamedDatabase");
        if (cs.TrustServerCertificate || cs.Encrypt.ToString() != "True")
            throw new Vps07bRefusal("Migration.VerifiedSqlTlsRequired");
        if (target != 10 && target != 12 && target != 13)
            throw new Vps07bRefusal("Migration.TargetUnsupported");
        _target = target;
        RunnerConnectionString = cs.ConnectionString;
        _lock = new SqlConnection(cs.ConnectionString);
    }

    public string RunnerConnectionString { get; }

    public AdmittedSource Admit()
    {
        _lock.Open();
        if (Scalar("SELECT ISNULL(IS_SRVROLEMEMBER(N'sysadmin'),0);") == 1 &&
            !IsEphemeralCiSql())
            throw new Vps07bRefusal("Migration.DedicatedControlPrincipalRequired");
        if (Scalar("SELECT ISNULL(HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CONTROL'),0);") != 1)
            throw new Vps07bRefusal("Migration.ControlPermissionRequired");
        var lockResult = Scalar(
            "DECLARE @result int; EXEC @result=sys.sp_getapplock " +
            "@Resource=N'" + LockName + "',@LockMode=N'Exclusive', " +
            "@LockOwner=N'Session',@LockTimeout=0; SELECT @result;");
        if (lockResult < 0)
            throw new Vps07bRefusal("Migration.AlreadyRunning");
        _ownsLock = true;

        var hasLegacy = Exists("dbo.SchemaVersions");
        var hasFm = Exists(JournalName);
        if (!hasLegacy && hasFm)
            throw new Vps07bRefusal("Migration.OrphanFluentMigratorJournal");

        int version;
        if (!hasLegacy)
        {
            // An empty journal is NOT a fresh database if any unknown object is
            // already present (including app tables, procedures or triggers).
            if (Scalar(@"SELECT COUNT(*) FROM sys.objects
WHERE is_ms_shipped=0 AND (parent_object_id=0 OR type IN (N'TR',N'TA'));") != 0 ||
                Scalar("SELECT COUNT(*) FROM sys.types WHERE is_user_defined=1;") != 0)
                throw new Vps07bRefusal("Migration.UnknownExistingObjects");
            var empty = new SqlSchemaCompatibilityChecker(_lock.ConnectionString)
                .CheckConnectionAsync(_lock, CancellationToken.None).GetAwaiter().GetResult();
            if (empty.Status != SchemaCompatibilityStatus.Empty)
                throw new Vps07bRefusal("Migration.UnknownEmptyDatabase");
            version = 0;
        }
        else
        {
            version = LegacyCountAndPrefix();
            if (version < 9 || version > 13)
                throw new Vps07bRefusal("Migration.UnsupportedHistoricalVersion");
            VerifyNative(version);
        }

        if (version > _target)
            throw new Vps07bRefusal("Migration.DowngradeForbidden");

        if (hasFm)
        {
            SqlFluentMigratorVersionTableProof.VerifyAsync(_lock, CancellationToken.None)
                .GetAwaiter().GetResult();
            VerifyDualJournal(version);
        }

        // Existing schemas are never upgraded or journal-bridged while live
        // writers may still be operating. This is the unchanged original
        // exact9/10/11 restricted-user, complete-drain, visibility validator.
        if (version != 0 && (version < _target || !hasFm))
        {
            // A just-closed DbUp/SQL client session may still appear during
            // the first DMV snapshot. The original full validator remains
            // the *only* approval oracle; never bypass or alter its SQL.
            // Keep our exclusive app lock and do not mutate any journal here.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    MyDmsVn.Server.DbMigrator.Schema9MaintenanceValidator.Validate(_lock);
                    break;
                }
                catch (MyDmsVn.Server.DbMigrator.DatabaseMigrationPreflightException ex)
                    when (ex.Code == "Migration.WritersNotDrained" && attempt < 3)
                {
                    System.Threading.Thread.Sleep(200);
                }
            }
        }

        // Historical 010 calculates COGS/valuation from existing stock. Run
        // its *unchanged read-only pre-DDL prefix* BEFORE any FM bridge write.
        // Otherwise a data-invalid exact9 database would acquire a new FM
        // journal despite 010 refusing. Temp tables are connection-local.
        if (version == 9 && _target >= 10)
            VerifySchema9ValuationReadiness();

        _admitted = true;
        return new AdmittedSource(version, version == 0, hasFm);
    }

    private bool IsEphemeralCiSql()
    {
        var connection = new SqlConnectionStringBuilder(RunnerConnectionString);
        return Environment.GetEnvironmentVariable("CI") == "true" &&
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" &&
            Environment.GetEnvironmentVariable("VPS07B_CI_EPHEMERAL_SQL") == "approved-container-only" &&
            string.Equals(connection.DataSource, "localhost,1433", StringComparison.OrdinalIgnoreCase) &&
            connection.InitialCatalog.StartsWith("MyDmsVnVps07bCi", StringComparison.Ordinal) &&
            !connection.IntegratedSecurity &&
            string.Equals(connection.UserID, "sa", StringComparison.OrdinalIgnoreCase) &&
            !connection.TrustServerCertificate && connection.Encrypt.ToString() == "True";
    }

    private void VerifySchema9ValuationReadiness()
    {
        var firstBatch = ImmutableSqlScript.Batches("010_DirectSalesAndValuation.sql")[0];
        const string firstPermanentDdl = "CREATE TABLE dbo.StockValuationEntries";
        var boundary = firstBatch.IndexOf(firstPermanentDdl, StringComparison.Ordinal);
        if (boundary < 0 || firstBatch.IndexOf(firstPermanentDdl, boundary + 1, StringComparison.Ordinal) >= 0)
            throw new Vps07bRefusal("Migration.ValuationOracleSourceChanged");
        var readOnlySql = firstBatch.Substring(0, boundary).TrimEnd();
        if (readOnlySql.Length < 2000 || readOnlySql.Contains("CREATE TABLE dbo.", StringComparison.Ordinal))
            throw new Vps07bRefusal("Migration.ValuationOracleUnsafe");
        try
        {
            using var command = new SqlCommand(readOnlySql, _lock) { CommandTimeout = 180 };
            command.ExecuteNonQuery();
        }
        catch (SqlException error) when (error.Number >= 51010 && error.Number <= 51016)
        {
            throw new Vps07bRefusal("Migration.DataReadinessRefused");
        }
    }

    public void Bridge(AdmittedSource admitted)
    {
        if (!_admitted) throw new Vps07bRefusal("Migration.NotAdmitted");
        if (admitted.HasFmJournal) return;

        // The entire bridge is one SQL transaction. Failure does not leave a
        // half-created journal nor a partially stamped history.
        using var tx = _lock.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            if (admitted.IsFresh)
                Execute(tx, @"CREATE TABLE dbo.SchemaVersions
(
    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SchemaVersions_Id PRIMARY KEY,
    ScriptName nvarchar(255) NOT NULL,
    Applied datetime NOT NULL
);");
            Execute(tx, @"CREATE TABLE dbo.FluentMigratorVersionInfo
(
    Version bigint NOT NULL,
    AppliedOn datetime NULL,
    Description nvarchar(1024) NULL
);
CREATE UNIQUE CLUSTERED INDEX UC_FluentMigratorVersion
ON dbo.FluentMigratorVersionInfo(Version);");
            for (var index = 1; index <= admitted.Version; index++)
            {
                using var cmd = new SqlCommand(
                    "INSERT dbo.FluentMigratorVersionInfo(Version,AppliedOn,Description) " +
                    "VALUES(@Version,GETUTCDATE(),@Description);", _lock, tx);
                cmd.Parameters.Add("@Version", SqlDbType.BigInt).Value = index;
                cmd.Parameters.Add("@Description", SqlDbType.NVarChar, 1024).Value =
                    Vps07bExecutionScope.ScriptAt(index);
                if (cmd.ExecuteNonQuery() != 1)
                    throw new Vps07bRefusal("Migration.JournalBridgeIncomplete");
            }
            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { /* fail closed; operator reconciliation required */ }
            throw;
        }
        SqlFluentMigratorVersionTableProof.VerifyAsync(_lock, CancellationToken.None)
            .GetAwaiter().GetResult();
        VerifyDualJournal(admitted.Version);
    }

    public void VerifyAfterRun(int target)
    {
        if (!_admitted || !_ownsLock) throw new Vps07bRefusal("Migration.NotAdmitted");
        var observed = LegacyCountAndPrefix();
        if (observed != target)
            throw new Vps07bRefusal("Migration.PostRunJournalCountMismatch");
        VerifyDualJournal(target);
        VerifyNative(target);
    }

    private void VerifyNative(int version)
    {
        SchemaCompatibilityResult proof = version switch
        {
            9 => SqlSchemaCompatibilityChecker.CheckExactSchema9Async(_lock, CancellationToken.None)
                .GetAwaiter().GetResult(),
            10 => new SqlSchemaCompatibilityChecker(_lock.ConnectionString)
                .CheckConnectionAsync(_lock, CancellationToken.None).GetAwaiter().GetResult(),
            11 => SqlWebSchema11CompatibilityChecker.CheckConnectionAsync(_lock, CancellationToken.None)
                .GetAwaiter().GetResult(),
            12 => SqlWebSchemaCompatibilityChecker.CheckConnectionAsync(_lock, CancellationToken.None)
                .GetAwaiter().GetResult(),
            13 => SqlWeb5SchemaCompatibilityChecker.CheckConnectionAsync(_lock, CancellationToken.None)
                .GetAwaiter().GetResult(),
            _ => throw new Vps07bRefusal("Migration.UnknownSchema")
        };
        if (!proof.IsCompatible)
            throw new Vps07bRefusal("Migration.NativeSchemaFingerprintRefused");
    }

    private int LegacyCountAndPrefix()
    {
        var version = 0;
        using var cmd = new SqlCommand("SELECT ScriptName FROM dbo.SchemaVersions ORDER BY Id;", _lock);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ++version;
            if (version > Vps07bExecutionScope.Count)
                throw new Vps07bRefusal("Migration.FutureLegacyJournal");
            var actual = reader.GetString(0);
            var expected = Vps07bExecutionScope.ScriptAt(version);
            if (actual != expected && actual != LegacyPrefix + expected)
                throw new Vps07bRefusal("Migration.UnknownOrReorderedLegacyJournal");
        }
        return version;
    }

    private void VerifyDualJournal(int count)
    {
        var seen = 0;
        using var cmd = new SqlCommand(
            "SELECT Version,Description,AppliedOn FROM dbo.FluentMigratorVersionInfo ORDER BY Version;", _lock);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ++seen;
            if (seen > count || reader.IsDBNull(1) || reader.IsDBNull(2) ||
                reader.GetInt64(0) != seen ||
                reader.GetString(1) != Vps07bExecutionScope.ScriptAt(seen))
                throw new Vps07bRefusal("Migration.UnknownFluentMigratorJournal");
        }
        if (seen != count)
            throw new Vps07bRefusal("Migration.FluentMigratorJournalGap");
    }

    private bool Exists(string table)
    {
        using var cmd = new SqlCommand("SELECT OBJECT_ID(N'" + table + "',N'U');", _lock);
        return cmd.ExecuteScalar() is not DBNull;
    }

    private int Scalar(string sql)
    {
        using var cmd = new SqlCommand(sql, _lock) { CommandTimeout = 30 };
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private void Execute(SqlTransaction tx, string sql)
    {
        using var cmd = new SqlCommand(sql, _lock, tx) { CommandTimeout = 30 };
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try
        {
            if (_ownsLock && _lock.State == ConnectionState.Open)
                Scalar("DECLARE @r int; EXEC @r=sys.sp_releaseapplock " +
                    "@Resource=N'" + LockName + "',@LockOwner=N'Session'; SELECT @r;");
        }
        finally { _lock.Dispose(); }
    }
}

internal sealed record AdmittedSource(int Version, bool IsFresh, bool HasFmJournal);
