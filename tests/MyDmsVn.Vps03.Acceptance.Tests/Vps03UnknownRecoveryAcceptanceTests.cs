using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MyDmsVn.Vps03.Acceptance.Tests;

public sealed class Vps03UnknownRecoveryAcceptanceTests
{
    [Vps03AcceptanceFact]
    [Trait("Category", "Vps03Issue7Live")]
    [Trait("CaseId", "VPS03.ISSUE7.LIVE.RESTART")]
    public async Task Lost_ack_commit_survives_client_kill_and_releases_only_for_current_authorized_actor()
    {
        await using var scenario = await Vps03RecoveryScenario.CreateAsync(CancellationToken.None);
        var lost = await scenario.CommitAndLoseAckInChildAsync(CancellationToken.None);
        var committed = await scenario.RecoverAsOriginalActorInChildAsync(CancellationToken.None);

        Assert.Equal("UNKNOWN", lost.State);
        Assert.Equal(1, lost.BusinessRequests);
        Assert.Equal(1, lost.DroppedResponses);
        Assert.Equal(lost.OperationId, committed.OperationId);
        Assert.Equal("COMMITTED", committed.State);
        Assert.True(committed.FenceReleased);
        Assert.Equal(0, committed.BusinessRequests);
        Assert.Equal(1, committed.StatusRequests);
        Assert.Equal(scenario.AfterCommitSnapshot, await scenario.SnapshotAsync(CancellationToken.None));
    }

    [Vps03AcceptanceFact]
    [Trait("Category", "Vps03Issue7Live")]
    [Trait("CaseId", "VPS03.ISSUE7.LIVE.AUTHORITY")]
    public async Task Different_actor_and_revoked_grant_cannot_inspect_or_release()
    {
        await using var scenario = await Vps03RecoveryScenario.CreateAsync(CancellationToken.None);
        var lost = await scenario.CommitAndLoseAckInChildAsync(CancellationToken.None);

        var other = await scenario.RecoverAsDifferentActorInChildAsync(CancellationToken.None);
        Assert.Equal("UNKNOWN", other.State);
        Assert.False(other.FenceReleased);
        Assert.Equal(0, other.StatusRequests);
        Assert.Equal(lost.OperationId, scenario.PendingOperationId);

        await scenario.SetOriginalActorPostGrantAsync(false, CancellationToken.None);
        var revoked = await scenario.RecoverAsOriginalActorInChildAsync(CancellationToken.None);
        Assert.Equal("UNKNOWN", revoked.State);
        Assert.False(revoked.FenceReleased);
        Assert.True(revoked.StatusRequests >= 1);
        Assert.Equal(0, revoked.BusinessRequests);
        Assert.Equal(lost.OperationId, scenario.PendingOperationId);
    }

    [Vps03AcceptanceFact]
    [Trait("Category", "Vps03Issue7Live")]
    [Trait("CaseId", "VPS03.ISSUE7.LIVE.SESSION")]
    public async Task Session_rotation_with_restored_grant_can_release_exact_committed_result()
    {
        await using var scenario = await Vps03RecoveryScenario.CreateAsync(CancellationToken.None);
        var lost = await scenario.CommitAndLoseAckInChildAsync(CancellationToken.None);
        await scenario.SetOriginalActorPostGrantAsync(false, CancellationToken.None);
        var denied = await scenario.RecoverAsOriginalActorInChildAsync(CancellationToken.None);
        Assert.False(denied.FenceReleased);

        await scenario.SetOriginalActorPostGrantAsync(true, CancellationToken.None);
        var recovered = await scenario.RecoverAsOriginalActorInChildAsync(CancellationToken.None);

        Assert.Equal(lost.OperationId, recovered.OperationId);
        Assert.Equal("COMMITTED", recovered.State);
        Assert.True(recovered.FenceReleased);
        Assert.True(denied.SessionVersion > 0);
        Assert.True(recovered.SessionVersion > 0);
        Assert.NotEqual(denied.ClientProcessId, recovered.ClientProcessId);
        Assert.Equal(scenario.AfterCommitSnapshot, await scenario.SnapshotAsync(CancellationToken.None));
    }

    [Vps03AcceptanceFact]
    [Trait("Category", "Vps03Issue7Live")]
    [Trait("CaseId", "VPS03.ISSUE7.LIVE.FAIL-CLOSED")]
    public async Task Mismatched_or_nonterminal_results_never_release_the_fence()
    {
        await using var scenario = await Vps03RecoveryScenario.CreateAsync(CancellationToken.None);
        var outcomes = await scenario.CheckFailClosedMatrixAsync(CancellationToken.None);

        Assert.Contains(outcomes, item => item.Case == "cancelled" && !item.FenceReleased);
        Assert.Contains(outcomes, item => item.Case == "timeout" && !item.FenceReleased);
        Assert.Contains(outcomes, item => item.Case == "missing" && !item.FenceReleased);
        Assert.Contains(outcomes, item => item.Case == "expired" && !item.FenceReleased);
        Assert.Contains(outcomes, item => item.Case == "unavailable" && !item.FenceReleased);
        Assert.Contains(outcomes, item => item.Case == "indeterminate" && !item.FenceReleased);
        Assert.Contains(outcomes, item => item.Case == "http-404" && !item.FenceReleased);
        Assert.Contains(outcomes, item => item.Case == "http-500" && !item.FenceReleased);
        Assert.Contains(outcomes, item => item.Case == "http-503" && !item.FenceReleased);
        Assert.Contains(outcomes, item => item.Case == "actor-mismatch" && !item.FenceReleased);
        Assert.Contains(outcomes, item => item.Case == "epoch-mismatch" && !item.FenceReleased);
        Assert.Contains(outcomes, item => item.Case == "operation-mismatch" && !item.FenceReleased);
        Assert.Contains(outcomes, item => item.Case == "fingerprint-mismatch" && !item.FenceReleased);
        Assert.Contains(outcomes, item => item.Case == "kind-mismatch" && !item.FenceReleased);
        Assert.Contains(outcomes, item => item.Case == "method-mismatch" && !item.FenceReleased);
        Assert.Contains(outcomes, item => item.Case == "target-mismatch" && !item.FenceReleased);
        Assert.All(outcomes, item => Assert.Equal("UNKNOWN", item.State));
    }
}
