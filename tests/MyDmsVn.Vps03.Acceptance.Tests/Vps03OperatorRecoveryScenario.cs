using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Contracts;
using MyDmsVn.Desktop.Application;
using MyDmsVn.Desktop.Infrastructure.Http;
using MyDmsVn.Server.Application.Operations;
using MyDmsVn.Server.Infrastructure.Operations;
using MyDmsVn.Server.Infrastructure.Persistence;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03OperatorRaceResult
{
    internal int Completed { get; set; }
    internal int Rejected { get; set; }
    internal int AuditRows { get; set; }
    internal int CancelledRows { get; set; }
    internal int BusinessRows { get; set; }
    internal bool ReleasedBeforeReread { get; set; }
    internal string FreshRereadState { get; set; } = "";
    internal bool ReleasedAfterReread { get; set; }
    internal bool ReleasedAfterConfirmation { get; set; }
}

internal sealed class Vps03RuntimeDecisionResult
{
    internal string ErrorCode { get; set; } = "";
    internal int AuditRows { get; set; }
    internal int CancelledRows { get; set; }
}

internal sealed class Vps03OperatorRecoveryScenario : IAsyncDisposable
{
    private readonly Vps03OwnedEnvironment _environment;
    private readonly Vps03OperatorPrincipal _operator;
    private readonly WindowsHttpPendingWriteStore _store;
    private readonly HttpPendingWriteRecord _pending;
    private readonly WebOperationTerminalIdentity _terminal;
    private bool _disposed;

    private Vps03OperatorRecoveryScenario(Vps03OwnedEnvironment environment,
        Vps03OperatorPrincipal principal, WindowsHttpPendingWriteStore store,
        HttpPendingWriteRecord pending, WebOperationTerminalIdentity terminal)
    {
        _environment = environment;
        _operator = principal;
        _store = store;
        _pending = pending;
        _terminal = terminal;
    }

    internal static async Task<Vps03OperatorRecoveryScenario> CreateAsync(CancellationToken token)
    {
        var environment = await Vps03OwnedEnvironment.CreateAsync(13, token).ConfigureAwait(false);
        Vps03OperatorPrincipal? principal = null;
        try
        {
            var store = new WindowsHttpPendingWriteStore(new HttpDesktopOptions(
                environment.Origin.GetLeftPart(UriPartial.Authority)));
            if (store.Load() != null) throw new InvalidOperationException("VPS03.OperatorPendingSlotNotClean");
            var login = await environment.DesktopServices.GetRequiredService<IIdentityApiClient>()
                .LoginAsync(new LoginRequest(environment.Username, environment.Password), token)
                .ConfigureAwait(false);
            if (!login.IsSuccess || login.Data == null)
                throw new InvalidOperationException(login.Error?.Code ?? "VPS03.OperatorLoginFailed");
            var identity = await new Vps03SqlOracle(environment.OracleConnectionString)
                .OperationIdentityAsync(environment.Username, token).ConfigureAwait(false);
            if (login.Data.UserId != identity.ActorUserId)
                throw new InvalidDataException("VPS03.OperatorActorMismatch");
            var fingerprint = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(fingerprint);
            var operationId = Guid.NewGuid();
            var pending = new HttpPendingWriteRecord
            {
                OperationId = operationId.ToString("D"), ActorUserId = identity.ActorUserId,
                Protocol = "web5", Kind = "Sales.Create", Method = "POST",
                Route = "/api/v1/sales-receipts", TargetId = null,
                DeploymentEpoch = identity.DeploymentEpoch.ToString("D"),
                Fingerprint = Hex(fingerprint), CreatedUtc = DateTime.UtcNow,
            };
            store.Save(pending);
            principal = await Vps03OperatorPrincipal.CreateAsync(environment.OracleConnectionString, token)
                .ConfigureAwait(false);
            return new Vps03OperatorRecoveryScenario(environment, principal, store, pending,
                new WebOperationTerminalIdentity(operationId, identity.DeploymentEpoch,
                    WebOperationKind.SalesCreate, null, fingerprint));
        }
        catch
        {
            if (principal != null) await principal.DisposeAsync().ConfigureAwait(false);
            var store = new WindowsHttpPendingWriteStore(new HttpDesktopOptions(
                environment.Origin.GetLeftPart(UriPartial.Authority)));
            var pending = store.Load();
            if (pending != null) store.Clear(pending);
            await environment.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal async Task<Vps03OperatorRaceResult> RunConcurrentDecisionRaceAsync(CancellationToken token)
    {
        var first = DecideAsync(Guid.NewGuid(), token);
        var second = DecideAsync(Guid.NewGuid(), token);
        var outcomes = await Task.WhenAll(first, second).ConfigureAwait(false);
        var releasedBefore = _store.Load() == null;

        var inspection = await _operator.InspectAsync(_terminal, _pending.ActorUserId, token)
            .ConfigureAwait(false);
        if (inspection.Disposition != "CANCELLED" || inspection.Terminal == null)
            throw new InvalidDataException("VPS03.OperatorFreshRereadMismatch");
        var operations = _environment.DesktopServices.GetRequiredService<HttpDesktopOperations>();
        var reread = await operations.CheckAsync(token).ConfigureAwait(false);
        var releasedAfterReread = _store.Load() == null;
        var confirmed = await operations.AcceptCertifiedNotExecutedAsync(token).ConfigureAwait(false);
        var snapshot = await new Vps03SqlOracle(_environment.OracleConnectionString)
            .TerminalSnapshotAsync(_terminal.OperationId, token).ConfigureAwait(false);
        return new Vps03OperatorRaceResult
        {
            Completed = outcomes.Count(value => value == "completed"),
            Rejected = outcomes.Count(value => value == "rejected"),
            AuditRows = snapshot.AuditRows,
            CancelledRows = snapshot.CancelledRows,
            BusinessRows = snapshot.BusinessRows,
            ReleasedBeforeReread = releasedBefore,
            FreshRereadState = reread.State,
            ReleasedAfterReread = releasedAfterReread,
            ReleasedAfterConfirmation = confirmed.FenceReleased && _store.Load() == null,
        };
    }

    internal async Task<Vps03RuntimeDecisionResult> AttemptAsRuntimePrincipalAsync(CancellationToken token)
    {
        var errorCode = "";
        try
        {
            await RecordAsync(_environment.RuntimeConnectionString, Guid.NewGuid(), token)
                .ConfigureAwait(false);
        }
        catch (WebOperationException error) { errorCode = error.Code; }
        catch (SqlException error) when (error.Number == 229) { errorCode = "Auth.Forbidden"; }
        var snapshot = await new Vps03SqlOracle(_environment.OracleConnectionString)
            .TerminalSnapshotAsync(_terminal.OperationId, token).ConfigureAwait(false);
        return new Vps03RuntimeDecisionResult
        { ErrorCode = errorCode, AuditRows = snapshot.AuditRows, CancelledRows = snapshot.CancelledRows };
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        var pending = _store.Load();
        if (pending != null) _store.Clear(pending);
        await _operator.DisposeAsync().ConfigureAwait(false);
        await _environment.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
    }

    private async Task<string> DecideAsync(Guid decision, CancellationToken token)
    {
        try
        {
            await RecordAsync(_operator.ConnectionString, decision, token).ConfigureAwait(false);
            return "completed";
        }
        catch (WebOperationException error) when (error.Code == "Operation.ResolutionConflict")
        { return "rejected"; }
    }

    private async Task RecordAsync(string connectionString, Guid decision, CancellationToken token)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            var maintenance = new SqlWebOperationResolutionMaintenance(
                new Vps03SqlExecutionContext(connection, transaction));
            await maintenance.RecordNotExecutedAsync(_terminal, _pending.ActorUserId, decision,
                "VPS03-ISSUE7", "ORIGINAL_NOT_EXECUTED", "issue7://operator/race", token)
                .ConfigureAwait(false);
            transaction.Commit();
        }
        catch
        {
            if (transaction.Connection != null) transaction.Rollback();
            throw;
        }
    }

    private static string Hex(byte[] value) =>
        BitConverter.ToString(value).Replace("-", "").ToLowerInvariant();
}

internal sealed class Vps03OperatorPrincipal : IAsyncDisposable
{
    private readonly string _adminDatabase;
    private readonly string _adminMaster;
    private readonly byte[] _sid;
    private bool _disposed;

    private Vps03OperatorPrincipal(string adminDatabase, string adminMaster,
        string name, byte[] sid, string connectionString)
    { _adminDatabase = adminDatabase; _adminMaster = adminMaster; Name = name;
        _sid = sid; ConnectionString = connectionString; }

    internal string Name { get; }
    internal string ConnectionString { get; }

    internal static async Task<Vps03OperatorPrincipal> CreateAsync(string adminDatabase,
        CancellationToken token)
    {
        var name = "Vps03Operator_" + Guid.NewGuid().ToString("N");
        var password = "Vps03Operator!" + Guid.NewGuid().ToString("N");
        var sid = Guid.NewGuid().ToByteArray();
        var adminBuilder = new SqlConnectionStringBuilder(adminDatabase);
        var database = adminBuilder.InitialCatalog;
        adminBuilder.InitialCatalog = "master";
        adminBuilder.Pooling = false;
        var master = adminBuilder.ConnectionString;
        using (var connection = new SqlConnection(master))
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE LOGIN " + Quote(name) + " WITH PASSWORD=N'" +
                password.Replace("'", "''") + "', SID=0x" +
                BitConverter.ToString(sid).Replace("-", "") + ", CHECK_POLICY=OFF;";
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        try
        {
            using var connection = new SqlConnection(adminDatabase);
            await connection.OpenAsync(token).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE USER " + Quote(name) + " FOR LOGIN " + Quote(name) +
                "; ALTER ROLE MyDmsVnWebOperationOperatorRole ADD MEMBER " + Quote(name) + ";";
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            var restricted = new SqlConnectionStringBuilder(adminDatabase)
            {
                IntegratedSecurity = false, Authentication = SqlAuthenticationMethod.NotSpecified,
                UserID = name, Password = password, InitialCatalog = database, Pooling = false,
            }.ConnectionString;
            await ValidateRestrictedAsync(restricted, token).ConfigureAwait(false);
            return new Vps03OperatorPrincipal(adminDatabase, master, name, sid, restricted);
        }
        catch
        {
            await DropLoginAsync(master, name, sid, token).ConfigureAwait(false);
            throw;
        }
    }

    internal async Task<WebOperationInspection> InspectAsync(WebOperationTerminalIdentity identity,
        int actor, CancellationToken token)
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();
        var result = await new SqlWebOperationResolutionMaintenance(
            new Vps03SqlExecutionContext(connection, transaction))
            .InspectAsync(identity.DeploymentEpoch, actor, identity.OperationId,
                identity.Fingerprint, token).ConfigureAwait(false);
        transaction.Commit();
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        SqlConnection.ClearAllPools();
        using (var connection = new SqlConnection(_adminDatabase))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "DROP USER " + Quote(Name) + ";";
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        await DropLoginAsync(_adminMaster, Name, _sid, CancellationToken.None).ConfigureAwait(false);
        _disposed = true;
    }

    private static async Task ValidateRestrictedAsync(string connectionString, CancellationToken token)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT CONVERT(int,ISNULL(IS_ROLEMEMBER(N'MyDmsVnWebOperationOperatorRole'),0)),
CONVERT(int,ISNULL(IS_ROLEMEMBER(N'MyDmsVnDesktopApiRuntimeRole'),0)),
CONVERT(int,ISNULL(IS_ROLEMEMBER(N'db_owner'),0)),
CONVERT(int,ISNULL(IS_SRVROLEMEMBER(N'sysadmin'),0));";
        using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false) || reader.GetInt32(0) != 1 ||
            reader.GetInt32(1) != 0 || reader.GetInt32(2) != 0 || reader.GetInt32(3) != 0)
            throw new InvalidDataException("VPS03.OperatorPrincipalNotRestricted");
    }

    private static async Task DropLoginAsync(string master, string name, byte[] sid,
        CancellationToken token)
    {
        SqlConnection.ClearAllPools();
        using var connection = new SqlConnection(master);
        await connection.OpenAsync(token).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@sid", sid);
        command.CommandText = "IF EXISTS(SELECT 1 FROM sys.server_principals WHERE name=@name AND sid<>@sid) " +
            "THROW 51998,'VPS-03 operator login identity changed.',1; " +
            "IF EXISTS(SELECT 1 FROM sys.server_principals WHERE name=@name) DROP LOGIN " + Quote(name) + ";";
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static string Quote(string value) => "[" + value.Replace("]", "]]" ) + "]";
}

internal sealed class Vps03SqlExecutionContext : ISqlExecutionContext
{
    internal Vps03SqlExecutionContext(SqlConnection connection, SqlTransaction transaction)
    { Connection = connection; Transaction = transaction; }
    public IDbConnection Connection { get; }
    public IDbTransaction Transaction { get; }
}
