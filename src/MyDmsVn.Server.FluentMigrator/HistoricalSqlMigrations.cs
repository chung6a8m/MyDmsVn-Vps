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
        foreach (var sqlBatch in ImmutableSqlScript.Batches(ScriptName))
            Execute.Sql(sqlBatch);
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
