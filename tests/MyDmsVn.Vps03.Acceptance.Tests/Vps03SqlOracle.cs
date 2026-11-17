using System.IO;
using Microsoft.Data.SqlClient;
using MyDmsVn.Server.Infrastructure.Operations;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03RuntimePrincipalSnapshot
{
    internal Vps03RuntimePrincipalSnapshot(bool hasDesktopRole, bool isSysAdmin,
        bool isDbOwner, bool hasUnexpectedWrite)
    {
        HasDesktopRole = hasDesktopRole;
        IsSysAdmin = isSysAdmin;
        IsDbOwner = isDbOwner;
        HasUnexpectedWrite = hasUnexpectedWrite;
    }

    internal bool HasDesktopRole { get; }
    internal bool IsSysAdmin { get; }
    internal bool IsDbOwner { get; }
    internal bool HasUnexpectedWrite { get; }
}

internal sealed class Vps03SqlOracle
{
    internal Vps03SqlOracle(string connectionString) => ConnectionString = connectionString;
    private string ConnectionString { get; }

    internal async Task<string> SchemaFingerprintAsync(CancellationToken token)
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);
        return await SqlSchemaDefinitionFingerprint.ReadAsync(connection, token).ConfigureAwait(false);
    }

    internal async Task<Vps03RuntimePrincipalSnapshot> RuntimePrincipalAsync(CancellationToken token)
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT
CONVERT(int,ISNULL(IS_ROLEMEMBER(N'MyDmsVnDesktopApiRuntimeRole'),0)),
CONVERT(int,ISNULL(IS_SRVROLEMEMBER(N'sysadmin'),0)),
CONVERT(int,ISNULL(IS_ROLEMEMBER(N'db_owner'),0)),
CONVERT(int,ISNULL(HAS_PERMS_BY_NAME(N'dbo.SchemaVersions',N'OBJECT',N'UPDATE'),0));";
        using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new InvalidDataException("VPS-03 runtime principal query returned no row.");
        return new Vps03RuntimePrincipalSnapshot(reader.GetInt32(0) == 1, reader.GetInt32(1) == 1,
            reader.GetInt32(2) == 1, reader.GetInt32(3) == 1);
    }

    internal static void ValidateRuntimePrincipal(Vps03RuntimePrincipalSnapshot snapshot)
    {
        if (!snapshot.HasDesktopRole || snapshot.IsSysAdmin || snapshot.IsDbOwner || snapshot.HasUnexpectedWrite)
            throw new InvalidDataException("VPS-03 HTTP runtime principal is missing its narrow role or has elevated permissions.");
    }

    internal async Task<Vps03OperationIdentity> OperationIdentityAsync(string username,
        CancellationToken token)
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("@username", username);
        command.CommandText = @"SELECT u.UserId,d.DeploymentEpoch
FROM dbo.Users u CROSS JOIN dbo.WebDeploymentState d
WHERE u.Username=@username AND d.Id=1;";
        using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new InvalidDataException("VPS-03 operation identity was not unique.");
        var result = new Vps03OperationIdentity(reader.GetInt32(0), reader.GetGuid(1));
        if (await reader.ReadAsync(token).ConfigureAwait(false))
            throw new InvalidDataException("VPS-03 operation identity was not unique.");
        return result;
    }

    internal async Task<Vps03TerminalSnapshot> TerminalSnapshotAsync(Guid operationId,
        CancellationToken token)
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("@operation", operationId);
        command.CommandText = @"SELECT
(SELECT COUNT(*) FROM dbo.WebOperationTerminalAudit WHERE OperationId=@operation),
(SELECT COUNT(*) FROM dbo.WebOperationJournal WHERE OperationId=@operation AND Disposition=N'CANCELLED'),
(SELECT COUNT(*) FROM dbo.SalesReceipts)+(SELECT COUNT(*) FROM dbo.GoodsReceipts)+
(SELECT COUNT(*) FROM dbo.StockLedger)+(SELECT COUNT(*) FROM dbo.StockValuationEntries);";
        using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new InvalidDataException("VPS-03 terminal snapshot returned no row.");
        return new Vps03TerminalSnapshot(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    }

    internal async Task<Vps03RecoverySnapshot> RecoverySnapshotAsync(int warehouseId, int productId,
        long receiptId, long saleId, CancellationToken token)
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("@warehouse", warehouseId);
        command.Parameters.AddWithValue("@product", productId);
        command.Parameters.AddWithValue("@receipt", receiptId);
        command.Parameters.AddWithValue("@sale", saleId);
        command.CommandText = @"SELECT
(SELECT COUNT(*) FROM dbo.StockLedger WHERE WarehouseId=@warehouse AND ProductId=@product),
(SELECT COUNT(*) FROM dbo.StockValuationEntries v JOIN dbo.StockLedger l ON l.LedgerId=v.LedgerId WHERE l.WarehouseId=@warehouse AND l.ProductId=@product),
(SELECT Quantity FROM dbo.StockBalances WHERE WarehouseId=@warehouse AND ProductId=@product),
(SELECT InventoryValue FROM dbo.StockBalances WHERE WarehouseId=@warehouse AND ProductId=@product),
COALESCE((SELECT SUM(CostAmount) FROM dbo.SalesReceiptLines WHERE SaleId=@sale),0),
(SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE ReceiptId=@receipt AND Status=N'Posted'),
(SELECT COUNT(*) FROM dbo.SalesReceipts WHERE SaleId=@sale AND Status=N'Posted');";
        using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new InvalidDataException("VPS-03 recovery snapshot returned no row.");
        return new Vps03RecoverySnapshot(reader.GetInt32(0), reader.GetInt32(1),
            reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4),
            reader.GetInt32(5), reader.GetInt32(6));
    }
}

internal sealed class Vps03OperationIdentity
{
    internal Vps03OperationIdentity(int actorUserId, Guid deploymentEpoch)
    { ActorUserId = actorUserId; DeploymentEpoch = deploymentEpoch; }
    internal int ActorUserId { get; }
    internal Guid DeploymentEpoch { get; }
}

internal sealed class Vps03TerminalSnapshot
{
    internal Vps03TerminalSnapshot(int auditRows, int cancelledRows, int businessRows)
    { AuditRows = auditRows; CancelledRows = cancelledRows; BusinessRows = businessRows; }
    internal int AuditRows { get; }
    internal int CancelledRows { get; }
    internal int BusinessRows { get; }
}

internal sealed class Vps03RecoverySnapshot : System.IEquatable<Vps03RecoverySnapshot>
{
    internal Vps03RecoverySnapshot(int ledger, int valuation, decimal quantity,
        decimal value, decimal cogs, int postedReceipts, int postedSales)
    { Ledger = ledger; Valuation = valuation; Quantity = quantity; Value = value;
        Cogs = cogs; PostedReceipts = postedReceipts; PostedSales = postedSales; }
    internal int Ledger { get; }
    internal int Valuation { get; }
    internal decimal Quantity { get; }
    internal decimal Value { get; }
    internal decimal Cogs { get; }
    internal int PostedReceipts { get; }
    internal int PostedSales { get; }
    public bool Equals(Vps03RecoverySnapshot? other) => other != null &&
        Ledger == other.Ledger && Valuation == other.Valuation && Quantity == other.Quantity &&
        Value == other.Value && Cogs == other.Cogs && PostedReceipts == other.PostedReceipts &&
        PostedSales == other.PostedSales;
    public override bool Equals(object? obj) => Equals(obj as Vps03RecoverySnapshot);
    public override int GetHashCode() => Ledger ^ Valuation ^ Quantity.GetHashCode() ^
        Value.GetHashCode() ^ Cogs.GetHashCode() ^ PostedReceipts ^ PostedSales;
    public override string ToString() => $"ledger={Ledger},valuation={Valuation},qty={Quantity},value={Value},cogs={Cogs},receipts={PostedReceipts},sales={PostedSales}";
}
