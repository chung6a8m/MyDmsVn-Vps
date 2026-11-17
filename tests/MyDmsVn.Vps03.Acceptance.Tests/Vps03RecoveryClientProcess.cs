using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Contracts;
using MyDmsVn.Desktop.Application;
using MyDmsVn.Desktop.Infrastructure.Http;
using Newtonsoft.Json;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03RecoveryClientResult
{
    public string? Stage { get; set; }
    public string? State { get; set; }
    public bool FenceReleased { get; set; }
    public string? OperationId { get; set; }
    public string? Fingerprint { get; set; }
    public int BusinessRequests { get; set; }
    public int DroppedResponses { get; set; }
    public int StatusRequests { get; set; }
    public long SessionVersion { get; set; }
    public int ClientProcessId { get; set; }
    public string? FailureCode { get; set; }
}

internal sealed class Vps03FailClosedResult
{
    internal Vps03FailClosedResult(string @case, string state, bool fenceReleased)
    { Case = @case; State = state; FenceReleased = fenceReleased; }
    internal string Case { get; }
    internal string State { get; }
    internal bool FenceReleased { get; }
}

internal sealed class Vps03RecoveryClientProcess : IDisposable
{
    private const int MaximumExchangeBytes = 4096;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly Process _process;
    private readonly string _exchange;

    private Vps03RecoveryClientProcess(Process process, string exchange)
    { _process = process; _exchange = exchange; }

    internal static Vps03RecoveryClientProcess Start(string mode, Uri origin,
        Vps03RecoveryActor actor, string exchange, long saleId, string saleVersion)
    {
        if (mode != "post-lost-ack" && mode != "recover")
            throw new ArgumentOutOfRangeException(nameof(mode));
        ValidateExchange(exchange);
        var result = Path.Combine(exchange, "result.json");
        if (File.Exists(result)) File.Delete(result);
        var executable = Executable();
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.EnvironmentVariables.Clear();
        var inherited = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .ToDictionary(item => (string)item.Key, item => (string?)item.Value,
                StringComparer.OrdinalIgnoreCase);
        foreach (var item in Vps03OwnedEnvironment.DesktopChildEnvironment(
                     inherited, origin, "web5"))
            if (item.Value != null) start.EnvironmentVariables[item.Key] = item.Value;
        start.EnvironmentVariables["MYDMSVN_VPS03_RECOVERY_MODE"] = mode;
        start.EnvironmentVariables["MYDMSVN_VPS03_RECOVERY_ORIGIN"] = origin.GetLeftPart(UriPartial.Authority);
        start.EnvironmentVariables["MYDMSVN_VPS03_RECOVERY_USERNAME"] = actor.Username;
        start.EnvironmentVariables["MYDMSVN_VPS03_RECOVERY_PASSWORD"] = actor.Password;
        start.EnvironmentVariables["MYDMSVN_VPS03_RECOVERY_EXCHANGE"] = exchange;
        start.EnvironmentVariables["MYDMSVN_VPS03_RECOVERY_SALE_ID"] = saleId.ToString(CultureInfo.InvariantCulture);
        start.EnvironmentVariables["MYDMSVN_VPS03_RECOVERY_SALE_VERSION"] = saleVersion;
        var process = Process.Start(start) ?? throw new InvalidOperationException("VPS03.RecoveryChildStartFailed");
        return new Vps03RecoveryClientProcess(process, exchange);
    }

    internal Vps03RecoveryClientResult WaitForResult(TimeSpan timeout)
    {
        var path = Path.Combine(_exchange, "result.json");
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > MaximumExchangeBytes)
                    throw new InvalidDataException("VPS03.RecoveryExchangeBounds");
                var result = JsonConvert.DeserializeObject<Vps03RecoveryClientResult>(
                    File.ReadAllText(path, Utf8), new JsonSerializerSettings
                    {
                        MissingMemberHandling = MissingMemberHandling.Error,
                        TypeNameHandling = TypeNameHandling.None,
                    }) ?? throw new InvalidDataException("VPS03.RecoveryResultMissing");
                if (result.Stage == "failed")
                    throw ChildFailure(result.FailureCode ?? "VPS03.RecoveryChildFailed");
                return result;
            }
            if (_process.HasExited) throw ChildFailure("VPS03.RecoveryChildExited");
            Thread.Sleep(25);
        }
        throw new TimeoutException("VPS03.RecoveryChildTimeout");
    }

    internal void KillAndWait(TimeSpan timeout)
    {
        if (!_process.HasExited) _process.Kill();
        if (!_process.WaitForExit(checked((int)timeout.TotalMilliseconds)))
            throw new TimeoutException("VPS03.RecoveryChildDrainTimeout");
    }

    internal bool WaitForExit(TimeSpan timeout) =>
        _process.WaitForExit(checked((int)timeout.TotalMilliseconds));

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill();
            _process.WaitForExit(5000);
        }
        _process.Dispose();
    }

    internal static string CreateExchange(string parent)
    {
        var path = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    internal static void DeleteExchange(string path)
    {
        ValidateExchange(path);
        if (Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length > 2)
            throw new InvalidDataException("VPS03.RecoveryCleanupFileCount");
        Directory.Delete(path, true);
    }

    private InvalidOperationException ChildFailure(string code)
    {
        var stdout = _process.StandardOutput.ReadToEnd();
        var stderr = _process.StandardError.ReadToEnd();
        return new InvalidOperationException(code + ":exit=" + (_process.HasExited ? _process.ExitCode : -1) +
            ",stdout=" + Sanitize(stdout) + ",stderr=" + Sanitize(stderr));
    }

    private static string Sanitize(string value) =>
        new string(value.Where(c => c >= 32 && c <= 126).Take(256).ToArray());

    private static string Executable()
    {
        var configuration = AppContext.BaseDirectory.IndexOf("\\Debug\\", StringComparison.OrdinalIgnoreCase) >= 0
            ? "Debug" : "Release";
        var path = Path.Combine(Vps03PendingStoreProcess.RepositoryRoot(), "tests",
            "MyDmsVn.Vps03.RecoveryClient", "bin", configuration, "net48",
            "MyDmsVn.Vps03.RecoveryClient.exe");
        if (!File.Exists(path)) throw new FileNotFoundException("VPS03.RecoveryClientMissing", path);
        return path;
    }

    private static void ValidateExchange(string path)
    {
        if (!Path.IsPathRooted(path) || !Directory.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("VPS03.RecoveryExchangeUnsafe");
        var full = Path.GetFullPath(path);
        var root = Path.Combine(Vps03PendingStoreProcess.RepositoryRoot(), "TestResults",
            "vps03-issue7", "recovery") + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("VPS03.RecoveryExchangeUnsafe");
    }
}

internal sealed class Vps03RecoveryScenario : IAsyncDisposable
{
    private readonly Vps03OwnedEnvironment _environment;
    private readonly Vps03RecoveryActor _actor;
    private readonly List<string> _exchanges = new();
    private readonly string _exchangeRoot;
    private readonly int _warehouseId;
    private readonly int _productId;
    private readonly long _receiptId;
    private readonly long _saleId;
    private readonly string _saleVersion;
    private bool _disposed;

    private Vps03RecoveryScenario(Vps03OwnedEnvironment environment, Vps03RecoveryActor actor,
        string exchangeRoot, int warehouseId, int productId, long receiptId, long saleId,
        string saleVersion)
    {
        _environment = environment;
        _actor = actor;
        _exchangeRoot = exchangeRoot;
        _warehouseId = warehouseId;
        _productId = productId;
        _receiptId = receiptId;
        _saleId = saleId;
        _saleVersion = saleVersion;
    }

    internal Vps03RecoverySnapshot AfterCommitSnapshot { get; private set; } = null!;
    internal string? PendingOperationId => Store().Load()?.OperationId;

    internal static async Task<Vps03RecoveryScenario> CreateAsync(CancellationToken token)
    {
        var environment = await Vps03OwnedEnvironment.CreateAsync(13, token).ConfigureAwait(false);
        try
        {
            var store = new WindowsHttpPendingWriteStore(new HttpDesktopOptions(
                environment.Origin.GetLeftPart(UriPartial.Authority)));
            if (store.Load() != null) throw new InvalidOperationException("VPS03.RecoveryPendingSlotNotClean");
            Ok(await environment.DesktopServices.GetRequiredService<IIdentityApiClient>()
                .LoginAsync(new LoginRequest(environment.Username, environment.Password), token));
            var suffix = Guid.NewGuid().ToString("N").Substring(0, 9);
            var product = Ok(await environment.DesktopServices.GetRequiredService<IProductApiClient>()
                .CreateAsync(new SaveProductRequest("R" + suffix, "Recovery " + suffix, "EA"), token));
            var warehouse = Ok(await environment.DesktopServices.GetRequiredService<IWarehouseApiClient>()
                .CreateAsync(new SaveWarehouseRequest("R" + suffix, "Recovery " + suffix, null), token));
            var employee = Ok(await environment.DesktopServices.GetRequiredService<IEmployeeApiClient>()
                .CreateAsync(new SaveEmployeeRequest("R" + suffix, "Recovery " + suffix, null, null), token));
            var customer = Ok(await environment.DesktopServices.GetRequiredService<ICustomerApiClient>()
                .CreateAsync(new SaveCustomerRequest("R" + suffix, "Recovery " + suffix, null, null, null), token));
            var goods = environment.DesktopServices.GetRequiredService<IGoodsReceiptApiClient>();
            var receipt = Ok(await goods.CreateDraftAsync(new SaveGoodsReceiptRequest(DateTime.Today,
                warehouse.Id, employee.Id, "issue 7 recovery seed",
                new[] { new SaveGoodsReceiptLineRequest(product.Id, 10m, 5m) }), token));
            Ok(await goods.PostAsync(new PostGoodsReceiptRequest(receipt.Id, receipt.Version), token));
            var session = environment.DesktopServices.GetRequiredService<IDesktopSession>();
            var sales = environment.DesktopServices.GetRequiredService<ISalesReceiptApiClient>()
                .ForSession(session.Version);
            var sale = Ok(await sales.CreateDraftAsync(new SaveSalesReceiptRequest(DateTime.Today,
                customer.Id, warehouse.Id, employee.Id, "issue 7 recovery sale",
                new[] { new SaveSalesReceiptLineRequest(0, product.Id, 2m, 12m, false) }), token));
            var actor = await environment.CreateRecoveryActorAsync(token).ConfigureAwait(false);
            var root = Path.Combine(Vps03PendingStoreProcess.RepositoryRoot(), "TestResults",
                "vps03-issue7", "recovery", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new Vps03RecoveryScenario(environment, actor, root, warehouse.Id,
                product.Id, receipt.Id, sale.SaleId, sale.Version);
        }
        catch
        {
            await environment.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal async Task<Vps03RecoveryClientResult> CommitAndLoseAckInChildAsync(CancellationToken token)
    {
        var result = await RunChildAsync("post-lost-ack", _actor, killAfterResult: true, token)
            .ConfigureAwait(false);
        AfterCommitSnapshot = await SnapshotAsync(token).ConfigureAwait(false);
        if (AfterCommitSnapshot.PostedSales != 1 || AfterCommitSnapshot.Ledger != 2 ||
            AfterCommitSnapshot.Valuation != 2 || AfterCommitSnapshot.Quantity != 8m ||
            AfterCommitSnapshot.Value != 40m || AfterCommitSnapshot.Cogs != 10m)
            throw new InvalidDataException("VPS03.RecoverySnapshotMismatch");
        return result;
    }

    internal Task<Vps03RecoveryClientResult> RecoverAsOriginalActorInChildAsync(CancellationToken token) =>
        RunChildAsync("recover", _actor, killAfterResult: false, token);

    internal Task<Vps03RecoveryClientResult> RecoverAsDifferentActorInChildAsync(CancellationToken token) =>
        RunChildAsync("recover", new Vps03RecoveryActor(0, _environment.Username, _environment.Password),
            killAfterResult: false, token);

    internal Task SetOriginalActorPostGrantAsync(bool granted, CancellationToken token) =>
        _environment.SetRecoveryPostGrantAsync(_actor.UserId, granted, token);

    internal Task<Vps03RecoverySnapshot> SnapshotAsync(CancellationToken token) =>
        new Vps03SqlOracle(_environment.OracleConnectionString).RecoverySnapshotAsync(
            _warehouseId, _productId, _receiptId, _saleId, token);

    internal Task<IReadOnlyList<Vps03FailClosedResult>> CheckFailClosedMatrixAsync(CancellationToken token) =>
        Vps03FailClosedMatrix.RunAsync(token);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        var store = Store();
        var pending = store.Load();
        if (pending != null) store.Clear(pending);
        foreach (var exchange in _exchanges) Vps03RecoveryClientProcess.DeleteExchange(exchange);
        if (Directory.Exists(_exchangeRoot)) Directory.Delete(_exchangeRoot, false);
        await _environment.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
    }

    private async Task<Vps03RecoveryClientResult> RunChildAsync(string mode, Vps03RecoveryActor actor,
        bool killAfterResult, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var exchange = Vps03RecoveryClientProcess.CreateExchange(_exchangeRoot);
        _exchanges.Add(exchange);
        using var child = Vps03RecoveryClientProcess.Start(mode, _environment.Origin, actor,
            exchange, _saleId, _saleVersion);
        var result = child.WaitForResult(TimeSpan.FromSeconds(30));
        if (killAfterResult) child.KillAndWait(TimeSpan.FromSeconds(15));
        else if (!child.WaitForExit(TimeSpan.FromSeconds(15)))
            throw new TimeoutException("VPS03.RecoveryClientDidNotExit");
        await Task.CompletedTask;
        return result;
    }

    private WindowsHttpPendingWriteStore Store() => new(new HttpDesktopOptions(
        _environment.Origin.GetLeftPart(UriPartial.Authority)));

    private static T Ok<T>(ApiResponse<T> response)
    {
        if (!response.IsSuccess || response.Data == null)
            throw new InvalidOperationException(response.Error?.Code ?? "VPS03.RecoverySeedFailed");
        return response.Data;
    }
}
