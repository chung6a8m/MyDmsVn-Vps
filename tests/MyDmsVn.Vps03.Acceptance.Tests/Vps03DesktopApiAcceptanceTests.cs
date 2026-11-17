using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyDmsVn.Contracts;
using MyDmsVn.Desktop.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MyDmsVn.Vps03.Acceptance.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class Vps03DesktopApiCollection : ICollectionFixture<Vps03DesktopApiFixture>
{
    public const string CollectionName = "VPS-03 Desktop API acceptance";
}

public sealed class Vps03DesktopApiFixture : IAsyncLifetime
{
    internal Vps03OwnedEnvironment? Environment { get; private set; }

    public async ValueTask InitializeAsync()
    {
        if (!Vps03AcceptanceFactAttribute.IsApproved) return;
        Environment = await Vps03OwnedEnvironment.CreateAsync(13, CancellationToken.None);
        var login = await Environment.DesktopServices.GetRequiredService<IIdentityApiClient>()
            .LoginAsync(new LoginRequest(Environment.Username, Environment.Password), CancellationToken.None);
        Assert.True(login.IsSuccess, login.Error?.Code + ": " + login.Error?.Message);
    }

    public async ValueTask DisposeAsync()
    {
        if (Environment != null) await Environment.DisposeAsync();
    }
}

[Collection(Vps03DesktopApiCollection.CollectionName)]
public sealed class Vps03DesktopApiAcceptanceTests
{
    private readonly Vps03DesktopApiFixture _fixture;

    public Vps03DesktopApiAcceptanceTests(Vps03DesktopApiFixture fixture) => _fixture = fixture;

    public static IEnumerable<object[]> MethodCases() =>
        Vps03AcceptanceManifest.Load(Path.Combine(AppContext.BaseDirectory, "vps03-issue8-case-manifest.json"))
            .Cases.Where(item => item.Kind == "method")
            .Select(item => new object[] { item.CaseId });

    [Vps03AcceptanceTheory]
    [MemberData(nameof(MethodCases))]
    public async Task Production_adapter_executes_manifest_method(string caseId)
    {
        var environment = Assert.IsType<Vps03OwnedEnvironment>(_fixture.Environment);
        var testCase = Vps03AcceptanceManifest.Load(
            Path.Combine(AppContext.BaseDirectory, "vps03-issue8-case-manifest.json"))
            .Cases.Single(item => item.CaseId == caseId);

        await new Vps03DesktopApiCaseExecutor().ExecuteAsync(testCase, environment, CancellationToken.None);
    }
}
