using System;
using FluentMigrator;

namespace MyDmsVn.Server.FluentMigrator;

/// <summary>
/// VPS-07A: immutable versioned definitions only. The protected write-capable
/// SQL runner and DbUp journal bridge belong to separately gated VPS-07B.
/// </summary>
public abstract class ImmutableSqlMigration : Migration
{
    protected abstract string ScriptName { get; }

    public sealed override void Up()
    {
        if (!Vps07bExecutionScope.IsActive)
            throw new InvalidOperationException("VPS07B_UnguardedMigrationRefused");

        // Script 010 requires session context for a genuinely empty database.
        // Existing schema9..12 upgrades never receive this exemption.
        if (ScriptName == "010_DirectSalesAndValuation.sql" && Vps07bExecutionScope.IsFresh)
            Execute.Sql("EXEC sys.sp_set_session_context @key=N'MyDmsVn.FreshInstall', @value=1, @read_only=1;");

        // DbUp's GO splitter discards delimiter-adjacent leading/trailing CR/LF.
        // Preserve this exact stored-module definition behavior: untrimmed FM
        // batches append CRLF to CREATE PROCEDURE/TRIGGER definitions, changing
        // native schema fingerprints despite immutable original SQL source.
        // SQL Server stores CREATE PROCEDURE/TRIGGER exactly, including leading
        // blank lines. Strip only newline characters at batch boundaries,
        // never SQL tokens or the immutable imported source bytes.
        var version = Vps07bExecutionScope.VersionOf(ScriptName);
        var batchIndex = 0;
        foreach (var sqlBatch in ImmutableSqlScript.Batches(ScriptName))
        {
            Execute.Sql(sqlBatch.Trim('\r', '\n'));
            batchIndex++;
            // Only the separately admitted disposable CI runner can request
            // this deterministic failure. The original SQL blob is immutable.
            // FM must roll back this batch, its predecessors and both journals.
            if (Vps07bExecutionScope.ShouldInjectFault(version, batchIndex))
                Execute.Sql("THROW 51750, 'VPS07B isolated rollback oracle.', 1;");
            if (Vps07bExecutionScope.ShouldSuspendForKill(version, batchIndex))
                Execute.Sql("WAITFOR DELAY '00:00:20'; THROW 51751, 'VPS07B CI process-loss sentinel.', 1;");
        }

        // Both journals and the original business SQL are in one migration
        // transaction. VersionInfo is appended by the runner after Up().
        Execute.WithConnection((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
IF (SELECT COUNT_BIG(*) FROM dbo.SchemaVersions) <> @Previous
    THROW 51711, 'VPS07B journal prefix changed.', 1;
IF EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE ScriptName=@Name)
    THROW 51712, 'VPS07B replay refused.', 1;
INSERT dbo.SchemaVersions(ScriptName,Applied) VALUES(@Name,GETDATE());";
            var prior = command.CreateParameter();
            prior.ParameterName = "@Previous";
            prior.Value = version - 1;
            command.Parameters.Add(prior);
            var name = command.CreateParameter();
            name.ParameterName = "@Name";
            name.Value = "MyDmsVn.Server.DbMigrator.Scripts." + ScriptName;
            command.Parameters.Add(name);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("VPS07B journal append failed.");
        });
    }

    public sealed override void Down()
        => throw new NotSupportedException(
            "Historical migrations are forward-only; restore an independently verified backup.");
}

// Numeric order is explicit; no filename guessing and no code changes to 001-013.

[Migration(1, "001_PersistenceFoundation.sql")]
public sealed class Historical001 : ImmutableSqlMigration
{
    protected override string ScriptName => "001_PersistenceFoundation.sql";
}

[Migration(2, "002_Identity.sql")]
public sealed class Historical002 : ImmutableSqlMigration
{
    protected override string ScriptName => "002_Identity.sql";
}

[Migration(3, "003_Catalog.sql")]
public sealed class Historical003 : ImmutableSqlMigration
{
    protected override string ScriptName => "003_Catalog.sql";
}

[Migration(4, "004_Catalog_Nul_Constraints.sql")]
public sealed class Historical004 : ImmutableSqlMigration
{
    protected override string ScriptName => "004_Catalog_Nul_Constraints.sql";
}

[Migration(5, "005_GoodsReceipt.sql")]
public sealed class Historical005 : ImmutableSqlMigration
{
    protected override string ScriptName => "005_GoodsReceipt.sql";
}

[Migration(6, "006_GoodsReceipt_Number_Sequence.sql")]
public sealed class Historical006 : ImmutableSqlMigration
{
    protected override string ScriptName => "006_GoodsReceipt_Number_Sequence.sql";
}

[Migration(7, "007_Inventory_Stock.sql")]
public sealed class Historical007 : ImmutableSqlMigration
{
    protected override string ScriptName => "007_Inventory_Stock.sql";
}

[Migration(8, "008_Security_Administration.sql")]
public sealed class Historical008 : ImmutableSqlMigration
{
    protected override string ScriptName => "008_Security_Administration.sql";
}

[Migration(9, "009_Security_Permission_Import.sql")]
public sealed class Historical009 : ImmutableSqlMigration
{
    protected override string ScriptName => "009_Security_Permission_Import.sql";
}

[Migration(10, "010_DirectSalesAndValuation.sql")]
public sealed class Historical010 : ImmutableSqlMigration
{
    protected override string ScriptName => "010_DirectSalesAndValuation.sql";
}

[Migration(11, "011_WebAuthSessions.sql")]
public sealed class Historical011 : ImmutableSqlMigration
{
    protected override string ScriptName => "011_WebAuthSessions.sql";
}

[Migration(12, "012_WebOperationJournal.sql")]
public sealed class Historical012 : ImmutableSqlMigration
{
    protected override string ScriptName => "012_WebOperationJournal.sql";
}

[Migration(13, "013_WebOperationTerminalResolution.sql")]
public sealed class Historical013 : ImmutableSqlMigration
{
    protected override string ScriptName => "013_WebOperationTerminalResolution.sql";
}
