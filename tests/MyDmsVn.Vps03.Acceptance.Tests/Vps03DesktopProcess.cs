using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03DesktopProcess : IDisposable
{
    private readonly Process _process;
    private Vps03DesktopProcess(Process process, string executableHash)
    { _process = process; ExecutableHash = executableHash; }

    internal int Id => _process.Id;
    internal string ExecutableHash { get; }
    internal bool HasExited => _process.HasExited;

    internal static Vps03DesktopProcess Start(Vps03OwnedEnvironment environment)
    {
        var info = new ProcessStartInfo(environment.DesktopExecutable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(environment.DesktopExecutable),
        };
        info.EnvironmentVariables.Clear();
        foreach (var item in Vps03OwnedEnvironment.DesktopChildEnvironment(
            Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(x => (string)x.Key, x => (string?)x.Value, StringComparer.OrdinalIgnoreCase),
            environment.Origin, environment.Mode))
            if (item.Value != null) info.EnvironmentVariables[item.Key] = item.Value;
        var process = Process.Start(info) ?? throw new InvalidOperationException("VPS-03 Desktop did not start.");
        return new Vps03DesktopProcess(process, Sha256(environment.DesktopExecutable));
    }

    internal bool Stop(TimeSpan timeout)
    {
        if (_process.HasExited) return true;
        _process.CloseMainWindow();
        if (!_process.WaitForExit((int)timeout.TotalMilliseconds)) return false;
        return _process.HasExited;
    }

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill();
            _process.WaitForExit(15000);
        }
        _process.Dispose();
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }
}

internal sealed class Vps03AttendedResult
{
    internal Vps03AttendedResult(string headSha, IReadOnlyDictionary<string, bool> checkpoints,
        string desktopSha256, string hostSha256, bool desktopExited, bool hostAbsent,
        bool sqlAbsent, bool aborted)
    { HeadSha = headSha; Checkpoints = checkpoints; DesktopSha256 = desktopSha256; HostSha256 = hostSha256; DesktopExited = desktopExited; HostAbsent = hostAbsent; SqlAbsent = sqlAbsent; Aborted = aborted; }
    internal string HeadSha { get; }
    internal IReadOnlyDictionary<string, bool> Checkpoints { get; }
    internal string DesktopSha256 { get; }
    internal string HostSha256 { get; }
    internal bool DesktopExited { get; }
    internal bool HostAbsent { get; }
    internal bool SqlAbsent { get; }
    internal bool Aborted { get; }
    internal bool Passed(IEnumerable<string> required) => !Aborted && DesktopExited && HostAbsent && SqlAbsent &&
        required.All(id => Checkpoints.TryGetValue(id, out var accepted) && accepted);
}
