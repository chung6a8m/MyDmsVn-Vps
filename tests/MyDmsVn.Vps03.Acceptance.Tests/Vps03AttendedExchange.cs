using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal static class Vps03AttendedExchange
{
    internal static void Prepare(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var name in new[]
        {
            "attended-stage.json", "attended-result.json", "attended-decision.json",
            "login-observed.json", "errors-observed.json", "unknown-recovery-observed.json",
        })
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    internal static void WriteStage(string directory, string headSha, int processId,
        string executableHash, string stage)
    {
        Write(Path.Combine(directory, "attended-stage.json"), new JObject
        {
            ["headSha"] = headSha,
            ["processId"] = processId,
            ["executableHash"] = executableHash,
            ["stage"] = stage,
            ["observedAtUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        });
    }

    internal static void WriteRecoveryStage(string directory, string headSha, int processId,
        string executableHash, Vps03RecoveryDialogObservation observation)
    {
        Write(Path.Combine(directory, "attended-stage.json"), new JObject
        {
            ["headSha"] = headSha,
            ["processId"] = processId,
            ["executableHash"] = executableHash,
            ["stage"] = "unknown-recovery-ready",
            ["observedAtUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["identityFieldsMatch"] = observation.Operation && observation.Actor &&
                observation.Epoch && observation.Kind && observation.Method &&
                observation.Route && observation.Target && observation.Fingerprint,
            ["warningVisible"] = observation.Warning,
            ["shellWriteSurfaceFenced"] = observation.ShellFenced,
            ["checkEnabled"] = observation.CheckEnabled,
            ["confirmationDisabled"] = observation.ConfirmationDisabled,
            ["noForceRelease"] = observation.NoForceRelease,
        });
    }

    internal static async Task WaitForCheckpointAsync(string directory, string headSha,
        string caseId, TimeSpan timeout, CancellationToken token)
    {
        var path = Path.Combine(directory, CaseFile(caseId));
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                var data = JObject.Parse(File.ReadAllText(path));
                if ((string?)data["headSha"] == headSha &&
                    (string?)data["caseId"] == caseId && (bool?)data["accepted"] == true)
                    return;
                throw new InvalidDataException("VPS03 attended checkpoint does not match the active run.");
            }
            await Task.Delay(200, token).ConfigureAwait(false);
        }
        throw new TimeoutException("VPS03 attended checkpoint timed out: " + caseId);
    }

    internal static async Task<IReadOnlyDictionary<string, bool>> WaitForDecisionAsync(
        string directory, string headSha, IEnumerable<string> required,
        TimeSpan timeout, CancellationToken token)
    {
        var path = Path.Combine(directory, "attended-decision.json");
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                var data = JObject.Parse(File.ReadAllText(path));
                if ((string?)data["headSha"] != headSha || !(data["checkpoints"] is JObject rows))
                    throw new InvalidDataException("VPS03 attended decision does not match the active run.");
                var result = rows.Properties().ToDictionary(p => p.Name,
                    p => p.Value.Type == JTokenType.Boolean && (bool)p.Value, StringComparer.Ordinal);
                if (required.Any(id => !result.TryGetValue(id, out var accepted) || !accepted))
                    throw new InvalidDataException("VPS03 attended decision is incomplete or rejected.");
                return result;
            }
            await Task.Delay(200, token).ConfigureAwait(false);
        }
        throw new TimeoutException("VPS03 attended decision timed out.");
    }

    internal static void WriteResult(string path, Vps03AttendedResult result,
        IEnumerable<string> required)
    {
        var checkpoints = new JObject();
        foreach (var row in result.Checkpoints.OrderBy(x => x.Key, StringComparer.Ordinal))
            checkpoints[row.Key] = row.Value;
        Write(path, new JObject
        {
            ["headSha"] = result.HeadSha,
            ["passed"] = result.Passed(required),
            ["desktopExited"] = result.DesktopExited,
            ["hostAbsent"] = result.HostAbsent,
            ["sqlAbsent"] = result.SqlAbsent,
            ["aborted"] = result.Aborted,
            ["checkpoints"] = checkpoints,
        });
    }

    private static string CaseFile(string caseId) =>
        caseId == "VPS03.GUI.LOGIN" ? "login-observed.json" :
        caseId == "VPS03.GUI.ERRORS" ? "errors-observed.json" :
        caseId == "VPS03.GUI.UNKNOWN-RECOVERY" ? "unknown-recovery-observed.json" :
        throw new ArgumentOutOfRangeException(nameof(caseId));

    private static void Write(string path, JObject data)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, data.ToString(Formatting.None));
        if (File.Exists(path)) File.Delete(path);
        File.Move(temp, path);
    }
}
