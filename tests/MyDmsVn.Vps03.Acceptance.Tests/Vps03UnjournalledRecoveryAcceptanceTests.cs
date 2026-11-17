using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MyDmsVn.Vps03.Acceptance.Tests;

public sealed class Vps03UnjournalledRecoveryAcceptanceTests
{
    [Vps03AcceptanceFact]
    [Trait("Category", "Vps03Issue7Live")]
    [Trait("CaseId", "VPS03.ISSUE7.LIVE.UNJOURNALLED")]
    public async Task Catalog_and_security_readback_remain_permanently_fenced()
    {
        await using var scenario = await Vps03UnjournalledRecoveryScenario.CreateAsync(CancellationToken.None);
        var outcomes = await scenario.RunAsync(CancellationToken.None);

        Assert.Collection(outcomes,
            catalog => AssertPermanentFence(catalog, "Catalog.Products.Update"),
            security => AssertPermanentFence(security, "Security.Mutate"));
    }

    private static void AssertPermanentFence(Vps03UnjournalledOutcome outcome, string kind)
    {
        Assert.Equal(kind, outcome.Kind);
        Assert.Equal("UNKNOWN", outcome.DispatchState);
        Assert.Equal("READBACK_OBSERVED", outcome.AuthorizedState);
        Assert.Equal("UNKNOWN", outcome.DeniedState);
        Assert.False(outcome.ReleasedAfterDispatch);
        Assert.False(outcome.ReleasedAfterAuthorizedReadback);
        Assert.False(outcome.ReleasedAfterDeniedReadback);
        Assert.Equal(outcome.OperationId, outcome.IdentityAfterAuthorizedReadback);
        Assert.Equal(outcome.OperationId, outcome.IdentityAfterDeniedReadback);
        Assert.Equal(outcome.Fingerprint, outcome.FingerprintAfterAuthorizedReadback);
        Assert.Equal(outcome.Fingerprint, outcome.FingerprintAfterDeniedReadback);
    }
}
