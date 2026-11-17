using System.Threading;
using System.Threading.Tasks;
using MyDmsVn.Server.Infrastructure.Operations;
using Xunit;

namespace MyDmsVn.Vps03.Acceptance.Tests;

public sealed class Vps03OwnedEnvironmentLiveTests
{
    [Vps03AcceptanceTheory]
    [InlineData(11, SqlSchemaDefinitionFingerprint.Schema11)]
    [InlineData(12, SqlSchemaDefinitionFingerprint.Schema12)]
    [InlineData(13, SqlSchemaDefinitionFingerprint.Schema13)]
    [Trait("Category", "Vps03Issue8Live")]
    public async Task Creates_real_https_restricted_environment(int schema, string expectedFingerprint)
    {
        var environment = await Vps03OwnedEnvironment.CreateAsync(schema, CancellationToken.None);
        try
        {
            Assert.Equal(expectedFingerprint,
                await new Vps03SqlOracle(environment.OracleConnectionString)
                    .SchemaFingerprintAsync(CancellationToken.None));
            var runtime = await new Vps03SqlOracle(environment.RuntimeConnectionString)
                .RuntimePrincipalAsync(CancellationToken.None);
            Vps03SqlOracle.ValidateRuntimePrincipal(runtime);
            Assert.Equal(Vps03OwnedEnvironment.ModeForSchema(schema), environment.Mode);
        }
        finally
        {
            await environment.DisposeAsync();
        }
    }
}
