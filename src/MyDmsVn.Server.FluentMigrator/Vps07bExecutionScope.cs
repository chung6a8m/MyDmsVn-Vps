using System;
using System.Threading;
namespace MyDmsVn.Server.FluentMigrator;

/// <summary>Only the guarded standalone CLI can activate this per-run lease.</summary>
public static class Vps07bExecutionScope
{
    private static readonly AsyncLocal<RunState?> State = new();
    private static readonly string[] ScriptNames =
    {
        "001_PersistenceFoundation.sql", "002_Identity.sql", "003_Catalog.sql",
        "004_Catalog_Nul_Constraints.sql", "005_GoodsReceipt.sql",
        "006_GoodsReceipt_Number_Sequence.sql", "007_Inventory_Stock.sql",
        "008_Security_Administration.sql", "009_Security_Permission_Import.sql",
        "010_DirectSalesAndValuation.sql", "011_WebAuthSessions.sql",
        "012_WebOperationJournal.sql", "013_WebOperationTerminalResolution.sql",
    };
    public static bool IsActive => State.Value != null;
    public static bool IsFresh => State.Value?.Fresh == true;
    public static int Count => ScriptNames.Length;
    public static string ScriptAt(int version) =>
        version >= 1 && version <= Count ? ScriptNames[version-1] :
        throw new ArgumentOutOfRangeException(nameof(version));
    public static int VersionOf(string script)
    {
        for (var i = 0; i < Count; ++i)
            if (string.Equals(ScriptNames[i], script, StringComparison.Ordinal)) return i+1;
        throw new InvalidOperationException("VPS07B unknown historical SQL script.");
    }
    public static IDisposable Enter(bool fresh)
    {
        if (State.Value != null) throw new InvalidOperationException("VPS07B nested run refused.");
        State.Value = new RunState(fresh);
        return new Lease();
    }
    private sealed record RunState(bool Fresh);
    private sealed class Lease : IDisposable
    {
        public void Dispose() => State.Value = null;
    }
}
