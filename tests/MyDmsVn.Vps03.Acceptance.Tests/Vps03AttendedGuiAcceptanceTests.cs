using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MyDmsVn.Vps03.Acceptance.Tests;

public sealed class Vps03AttendedGuiAcceptanceTests
{
    private static readonly string[] Required =
    {
        "VPS03.GUI.LOGIN", "VPS03.GUI.SHELL", "VPS03.GUI.CATALOG", "VPS03.GUI.GOODS",
        "VPS03.GUI.SALES", "VPS03.GUI.SECURITY", "VPS03.GUI.ERRORS", "VPS03.GUI.LOGOUT",
        "VPS03.GUI.UNKNOWN-RECOVERY",
    };

    [Fact]
    public void Unknown_recovery_checkpoint_is_required_by_attended_contract()
    {
        const string caseId = "VPS03.GUI.UNKNOWN-RECOVERY";
        Assert.Contains(caseId, Required);
        var manifest = JObject.Parse(File.ReadAllText(Path.Combine(
            Vps03PendingStoreProcess.RepositoryRoot(), "tests",
            "MyDmsVn.Vps03.Acceptance.Tests", "vps03-issue8-case-manifest.json")));
        var cases = manifest["cases"] as JArray ?? throw new InvalidDataException("VPS03 attended manifest cases missing.");
        Assert.Contains(cases.Values<JObject>(),
            row => row != null && (string?)row["caseId"] == caseId &&
                (string?)row["executor"] == "unknown-recovery");
    }

    [Fact]
    public void Aborted_walkthrough_and_uncertain_cleanup_cannot_pass()
    {
        var accepted = new Dictionary<string, bool>();
        foreach (var id in Required) accepted[id] = true;
        Assert.False(new Vps03AttendedResult(new string('a', 40), accepted, true, true, true, aborted: true).Passed(Required));
        Assert.False(new Vps03AttendedResult(new string('a', 40), accepted, true, false, true, aborted: false).Passed(Required));
        accepted.Remove("VPS03.GUI.ERRORS");
        Assert.False(new Vps03AttendedResult(new string('a', 40), accepted, true, true, true, aborted: false).Passed(Required));
    }

    [Fact]
    public void Complete_operator_confirmation_and_cleanup_can_pass()
    {
        var accepted = new Dictionary<string, bool>();
        foreach (var id in Required) accepted[id] = true;
        Assert.True(new Vps03AttendedResult(new string('b', 40), accepted, true, true, true, aborted: false).Passed(Required));
    }

    [Vps03AcceptanceFact]
    [Trait("Phase", "Attended")]
    [Trait("CaseId", "VPS03.GATE.GUI")]
    public async Task Exact_published_desktop_completes_attended_native_walkthrough()
    {
        var env = await Vps03OwnedEnvironment.CreateAsync(13, CancellationToken.None);
        var directory = Path.Combine(Environment.GetEnvironmentVariable("MYDMSVN_VPS03_RESULTS_DIRECTORY")!, "attended");
        Vps03AttendedExchange.Prepare(directory);
        var resultPath = Path.Combine(directory, "attended-result.json");
        var desktopExited = false;
        var hostAbsent = false;
        var sqlAbsent = false;
        var aborted = true;
        IReadOnlyDictionary<string, bool> checkpoints = new Dictionary<string, bool>();
        Vps03NativeRecoverySeed? recovery = null;
        try
        {
            recovery = await Vps03NativeRecoverySeed.CreateAsync(env, CancellationToken.None);
            using var desktop = Vps03DesktopProcess.Start(env);
            Assert.Equal(64, desktop.ExecutableHash.Length);
            using var probe = new Vps03RemoteGuiProbe(desktop.Id);
            probe.WaitForLogin(TimeSpan.FromSeconds(30));
            Vps03AttendedExchange.WriteStage(directory, env.HeadSha, desktop.Id,
                desktop.ExecutableHash, "login-ready");
            await Vps03AttendedExchange.WaitForCheckpointAsync(directory, env.HeadSha,
                "VPS03.GUI.LOGIN", TimeSpan.FromMinutes(10), CancellationToken.None);
            probe.SubmitInvalidLogin(env.Username, env.Password, TimeSpan.FromSeconds(30));
            Vps03AttendedExchange.WriteStage(directory, env.HeadSha, desktop.Id,
                desktop.ExecutableHash, "error-ready");
            await Vps03AttendedExchange.WaitForCheckpointAsync(directory, env.HeadSha,
                "VPS03.GUI.ERRORS", TimeSpan.FromMinutes(10), CancellationToken.None);
            probe.SubmitValidLogin(env.Username, env.Password, TimeSpan.FromSeconds(30));
            var recoveryObservation = probe.ObserveUnknownRecovery(
                recovery.Record, TimeSpan.FromSeconds(30));
            Assert.True(recoveryObservation.Complete,
                "The Native UNKNOWN recovery surface did not match the exact pending identity and fail-closed controls.");
            Vps03AttendedExchange.WriteRecoveryStage(directory, env.HeadSha, desktop.Id,
                desktop.ExecutableHash, recoveryObservation);
            await Vps03AttendedExchange.WaitForCheckpointAsync(directory, env.HeadSha,
                "VPS03.GUI.UNKNOWN-RECOVERY", TimeSpan.FromMinutes(10), CancellationToken.None);
            probe.WaitForUnknownRecoveryReleased(TimeSpan.FromSeconds(30));
            Assert.True(recovery.FenceReleased,
                "The Native confirmation did not clear the exact certified NOT_EXECUTED fence.");
            probe.CloseUnknownRecovery(TimeSpan.FromSeconds(30));
            Vps03AttendedExchange.WriteStage(directory, env.HeadSha, desktop.Id,
                desktop.ExecutableHash, "shell-ready");
            checkpoints = await Vps03AttendedExchange.WaitForDecisionAsync(directory,
                env.HeadSha, Required, TimeSpan.FromMinutes(20), CancellationToken.None);
            desktopExited = desktop.Stop(TimeSpan.FromSeconds(15));
            Assert.True(desktopExited, "The attended Desktop did not exit cleanly.");
            aborted = false;
        }
        finally
        {
            try
            {
                recovery?.Dispose();
                await env.DisposeAsync();
                hostAbsent = true;
                sqlAbsent = true;
            }
            finally
            {
                var result = new Vps03AttendedResult(env.HeadSha, checkpoints,
                    desktopExited, hostAbsent, sqlAbsent, aborted);
                Vps03AttendedExchange.WriteResult(resultPath, result, Required);
            }
        }
        Assert.True(new Vps03AttendedResult(env.HeadSha, checkpoints,
            desktopExited, hostAbsent, sqlAbsent, aborted).Passed(Required));
    }
}
