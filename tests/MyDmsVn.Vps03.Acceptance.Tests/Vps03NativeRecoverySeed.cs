using System;
using System.Data;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using MyDmsVn.Desktop.Infrastructure.Http;
using MyDmsVn.Server.Application.Operations;
using MyDmsVn.Server.Infrastructure.Operations;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03NativeRecoverySeed : IDisposable
{
    private readonly WindowsHttpPendingWriteStore _store;
    private bool _disposed;

    private Vps03NativeRecoverySeed(WindowsHttpPendingWriteStore store,
        HttpPendingWriteRecord record)
    { _store = store; Record = record; }

    internal HttpPendingWriteRecord Record { get; }
    internal bool FenceReleased => _store.Load() == null;

    internal static async Task<Vps03NativeRecoverySeed> CreateAsync(
        Vps03OwnedEnvironment environment, CancellationToken token)
    {
        var store = new WindowsHttpPendingWriteStore(new HttpDesktopOptions(
            environment.Origin.GetLeftPart(UriPartial.Authority)));
        if (store.Load() != null) throw new InvalidOperationException("VPS03.NativeRecoverySlotNotClean");
        var identity = await new Vps03SqlOracle(environment.OracleConnectionString)
            .OperationIdentityAsync(environment.Username, token).ConfigureAwait(false);
        var fingerprint = new byte[32];
        using (var random = RandomNumberGenerator.Create()) random.GetBytes(fingerprint);
        var operationId = Guid.NewGuid();
        var record = new HttpPendingWriteRecord
        {
            OperationId = operationId.ToString("D"), ActorUserId = identity.ActorUserId,
            Protocol = "web5", Kind = "Sales.Create", Method = "POST",
            Route = "/api/v1/sales-receipts", TargetId = null,
            DeploymentEpoch = identity.DeploymentEpoch.ToString("D"),
            Fingerprint = BitConverter.ToString(fingerprint).Replace("-", "").ToLowerInvariant(),
            CreatedUtc = DateTime.UtcNow,
        };
        store.Save(record);
        Vps03OperatorPrincipal? principal = null;
        try
        {
            principal = await Vps03OperatorPrincipal.CreateAsync(
                environment.OracleConnectionString, token).ConfigureAwait(false);
            using (var connection = new SqlConnection(principal.ConnectionString))
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
                var terminal = new WebOperationTerminalIdentity(operationId, identity.DeploymentEpoch,
                    WebOperationKind.SalesCreate, null, fingerprint);
                await new SqlWebOperationResolutionMaintenance(
                    new Vps03SqlExecutionContext(connection, transaction))
                    .RecordNotExecutedAsync(terminal, identity.ActorUserId, Guid.NewGuid(),
                        "VPS03-GUI-UNKNOWN", "ORIGINAL_NOT_EXECUTED",
                        "issue7://native/unknown-recovery", token).ConfigureAwait(false);
                transaction.Commit();
            }
            await principal.DisposeAsync().ConfigureAwait(false);
            principal = null;
            return new Vps03NativeRecoverySeed(store, record);
        }
        catch
        {
            if (principal != null) await principal.DisposeAsync().ConfigureAwait(false);
            var pending = store.Load();
            if (pending != null) store.Clear(pending);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        var pending = _store.Load();
        if (pending != null) _store.Clear(pending);
        _disposed = true;
    }
}
