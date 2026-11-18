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
    public static bool ShouldInjectFault(int version, int batch) =>
        State.Value is { Hang: false } run &&
        run.FaultVersion == version && run.FaultBatch == batch;
    public static bool ShouldSuspendForKill(int version, int batch) =>
        State.Value is { Hang: true } run &&
        run.FaultVersion == version && run.FaultBatch == batch;
    public static string ScriptAt(int version) =>
        version >= 1 && version <= Count ? ScriptNames[version-1] :
        throw new ArgumentOutOfRangeException(nameof(version));
    public static int VersionOf(string script)
    {
        for (var i = 0; i < Count; ++i)
            if (string.Equals(ScriptNames[i], script, StringComparison.Ordinal)) return i+1;
        throw new InvalidOperationException("VPS07B unknown historical SQL script.");
    }
    public static IDisposable Enter(bool fresh, int faultVersion = 0, int faultBatch = 0, bool hang = false)
    {
        if (State.Value != null) throw new InvalidOperationException("VPS07B nested run refused.");
        if (faultVersion is < 0 or > 13 || faultBatch is < 0 or > 9 ||
            (faultVersion == 0) != (faultBatch == 0))
            throw new InvalidOperationException("VPS07B invalid isolated CI fault request.");
        State.Value = new RunState(fresh, faultVersion, faultBatch, hang);
        return new Lease();
    }
    private sealed record RunState(bool Fresh, int FaultVersion, int FaultBatch, bool Hang);
    private sealed class Lease : IDisposable
    {
        public void Dispose() => State.Value = null;
    }
}
