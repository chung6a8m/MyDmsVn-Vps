using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using MyDmsVn.Desktop.Infrastructure.Http;
using Newtonsoft.Json;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03PendingStoreLoadResult
{
    public string? OperationId { get; set; }
    public string? Fingerprint { get; set; }
    public string? FailureCode { get; set; }
}

internal sealed class Vps03PendingStoreProcess : IDisposable
{
    private const int MaximumExchangeBytes = 4096;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly Process _process;
    private readonly string _origin;
    private readonly string _exchange;
    private readonly string _intent;

    private Vps03PendingStoreProcess(Process process, string origin, string exchange, string intent)
    {
        _process = process;
        _origin = origin;
        _exchange = exchange;
        _intent = intent;
    }

    internal int IntentCount
    {
        get
        {
            var directory = Path.GetDirectoryName(_intent)!;
            var prefix = Path.GetFileNameWithoutExtension(_intent);
            return Directory.Exists(directory)
                ? Directory.GetFiles(directory, prefix + "*", SearchOption.TopDirectoryOnly).Length
                : 0;
        }
    }

    internal bool DispatchMarkerExists => File.Exists(Path.Combine(_exchange, "dispatch.marker"));

    internal static string CreateExchangeDirectory()
    {
        var root = RepositoryRoot();
        var parent = Path.Combine(root, "TestResults", "vps03-issue7", "pending-store");
        Directory.CreateDirectory(parent);
        var exchange = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exchange);
        return exchange;
    }

    internal static Vps03PendingStoreProcess Start(string mode, string origin,
        string exchangeDirectory, HttpPendingWriteRecord record)
    {
        if (mode != "before-save" && mode != "after-save" && mode != "load")
            throw new ArgumentOutOfRangeException(nameof(mode));
        _ = new HttpDesktopOptions(origin);
        ValidateExchange(exchangeDirectory);
        record.Validate();
        var request = JsonConvert.SerializeObject(record, Formatting.None);
        if (Utf8.GetByteCount(request) > MaximumExchangeBytes) throw new InvalidDataException();
        File.WriteAllText(Path.Combine(exchangeDirectory, "request.json"), request, Utf8);
        foreach (var stale in new[] { "stage.json", "result.json", "dispatch.marker" })
        {
            var path = Path.Combine(exchangeDirectory, stale);
            if (File.Exists(path)) File.Delete(path);
        }

        var executable = CrashHostExecutable();
        ValidateX86(executable);
        var start = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = Quote(mode) + " " + Quote(origin) + " " + Quote(exchangeDirectory),
            WorkingDirectory = exchangeDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var process = Process.Start(start) ?? throw new InvalidOperationException("VPS03.PendingChildStartFailed");
        return new Vps03PendingStoreProcess(process, origin, exchangeDirectory, IntentPathFor(origin));
    }

    internal void WaitForStage(string expected, TimeSpan timeout)
    {
        var path = Path.Combine(_exchange, "stage.json");
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited) throw ChildFailure("VPS03.PendingChildExitedBeforeStage");
            if (File.Exists(path))
            {
                var result = ReadResult(path);
                if (result.Stage != expected) throw new InvalidDataException("VPS03.PendingStageMismatch");
                return;
            }
            Thread.Sleep(25);
        }
        throw new TimeoutException("VPS03.PendingStageTimeout");
    }

    internal Vps03PendingStoreLoadResult WaitForLoad(TimeSpan timeout)
    {
        var path = Path.Combine(_exchange, "result.json");
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                var exchange = ReadResult(path);
                if (exchange.Stage == "failed")
                    return new Vps03PendingStoreLoadResult { FailureCode = exchange.FailureCode };
                if (exchange.Stage != "load-complete")
                    throw new InvalidDataException("VPS03.PendingLoadStageMismatch");
                return new Vps03PendingStoreLoadResult
                {
                    OperationId = exchange.OperationId,
                    Fingerprint = exchange.Fingerprint,
                    FailureCode = exchange.FailureCode,
                };
            }
            if (_process.HasExited) throw ChildFailure("VPS03.PendingChildExitedBeforeResult");
            Thread.Sleep(25);
        }
        throw new TimeoutException("VPS03.PendingLoadTimeout");
    }

    internal void KillAndWait(TimeSpan timeout)
    {
        if (!_process.HasExited) _process.Kill();
        if (!_process.WaitForExit(checked((int)timeout.TotalMilliseconds)))
            throw new TimeoutException("VPS03.PendingChildDrainTimeout");
    }

    internal bool WaitForExit(TimeSpan timeout) =>
        _process.WaitForExit(checked((int)timeout.TotalMilliseconds));

    internal static void Cleanup(string origin, string exchangeDirectory, HttpPendingWriteRecord record)
    {
        _ = new HttpDesktopOptions(origin);
        ValidateExchange(exchangeDirectory);
        var intent = IntentPathFor(origin);
        if (File.Exists(intent)) new WindowsHttpPendingWriteStore(new HttpDesktopOptions(origin)).Clear(record);
        if (Directory.GetFiles(exchangeDirectory, "*", SearchOption.AllDirectories).Length > 5)
            throw new InvalidDataException("VPS03.PendingCleanupFileCount");
        Directory.Delete(exchangeDirectory, true);
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                _process.WaitForExit(5000);
            }
        }
        finally { _process.Dispose(); }
    }

    private static Exchange ReadResult(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0 || info.Length > MaximumExchangeBytes)
            throw new InvalidDataException("VPS03.PendingExchangeBounds");
        return JsonConvert.DeserializeObject<Exchange>(File.ReadAllText(path, Utf8),
            new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error,
                TypeNameHandling = TypeNameHandling.None })
            ?? throw new InvalidDataException("VPS03.PendingExchangeMissing");
    }

    private Exception ChildFailure(string message)
    {
        var error = _process.StandardError.ReadToEnd();
        var output = _process.StandardOutput.ReadToEnd();
        return new InvalidOperationException(message + ":exit=" + _process.ExitCode +
            ",stdout=" + Sanitize(output) + ",stderr=" + Sanitize(error));
    }

    private static string Sanitize(string value) =>
        new string(value.Where(c => c >= 32 && c <= 126).Take(256).ToArray());

    private static string CrashHostExecutable()
    {
        var root = RepositoryRoot();
        var configuration = AppContext.BaseDirectory.IndexOf("\\Debug\\", StringComparison.OrdinalIgnoreCase) >= 0
            ? "Debug" : "Release";
        var path = Path.Combine(root, "tests", "MyDmsVn.Vps03.PendingStore.CrashHost", "bin",
            configuration, "net48", "MyDmsVn.Vps03.PendingStore.CrashHost.exe");
        if (!File.Exists(path)) throw new FileNotFoundException("VPS03.PendingCrashHostMissing", path);
        return path;
    }

    internal static string IntentPathFor(string origin)
    {
        string owner;
        using (var identity = WindowsIdentity.GetCurrent())
            owner = identity.User?.Value ?? throw new InvalidOperationException("VPS03.PendingOwnerUnavailable");
        var binding = "MyDmsVn-Vps|pending-v1|" + owner + "|" +
            new Uri(origin).GetLeftPart(UriPartial.Authority);
        byte[] hash;
        using (var sha = SHA256.Create()) hash = sha.ComputeHash(Utf8.GetBytes(binding));
        var file = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant() + ".intent";
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyDmsVn-Vps", "PendingWrites", file);
    }

    internal static void ValidateExchange(string path)
    {
        if (!Path.IsPathRooted(path) || !Directory.Exists(path))
            throw new InvalidDataException("VPS03.PendingExchangeMissing");
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var root = Path.Combine(RepositoryRoot(), "TestResults", "vps03-issue7", "pending-store") +
            Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("VPS03.PendingExchangeUnsafe");
    }

    internal static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MyDmsVn.slnx")) ||
                File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("VPS03.RepositoryRootMissing");
    }

    private static void ValidateX86(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        stream.Position = 0x3c;
        var pe = reader.ReadInt32();
        stream.Position = pe;
        if (reader.ReadUInt32() != 0x00004550 || reader.ReadUInt16() != 0x014c)
            throw new InvalidDataException("VPS03.PendingCrashHostNotX86");
    }

    private static string Quote(string value)
    {
        if (value.IndexOf('"') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
            throw new ArgumentException("VPS03.PendingArgumentUnsafe");
        return "\"" + value + "\"";
    }

    private sealed class Exchange
    {
        public string? Stage { get; set; }
        public string? OperationId { get; set; }
        public string? Fingerprint { get; set; }
        public string? FailureCode { get; set; }
    }
}
