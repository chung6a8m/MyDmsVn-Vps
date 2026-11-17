using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MyDmsVn.Vps03.Acceptance.Tests;

public sealed class Vps03OperatorRecoveryAcceptanceTests
{
    [Vps03AcceptanceFact]
    [Trait("Category", "Vps03Issue7Live")]
    [Trait("CaseId", "VPS03.ISSUE7.LIVE.OPERATOR-RACE")]
    public async Task Concurrent_operator_dispositions_have_one_winner_and_require_reread()
    {
        await using var scenario = await Vps03OperatorRecoveryScenario.CreateAsync(CancellationToken.None);
        var result = await scenario.RunConcurrentDecisionRaceAsync(CancellationToken.None);

        Assert.Equal(1, result.Completed);
        Assert.Equal(1, result.Rejected);
        Assert.Equal(1, result.AuditRows);
        Assert.Equal(1, result.CancelledRows);
        Assert.Equal(0, result.BusinessRows);
        Assert.False(result.ReleasedBeforeReread);
        Assert.Equal("NOT_EXECUTED", result.FreshRereadState);
        Assert.False(result.ReleasedAfterReread);
        Assert.True(result.ReleasedAfterConfirmation);
    }

    [Vps03AcceptanceFact]
    [Trait("Category", "Vps03Issue7Live")]
    [Trait("CaseId", "VPS03.ISSUE7.LIVE.RUNTIME-DENIED")]
    public async Task Runtime_principal_cannot_record_terminal_disposition()
    {
        await using var scenario = await Vps03OperatorRecoveryScenario.CreateAsync(CancellationToken.None);
        var result = await scenario.AttemptAsRuntimePrincipalAsync(CancellationToken.None);

        Assert.Equal("Auth.Forbidden", result.ErrorCode);
        Assert.Equal(0, result.AuditRows);
        Assert.Equal(0, result.CancelledRows);
    }
}
