using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MyDmsVn.Vps09.Acceptance;
using Xunit;

namespace MyDmsVn.Vps09.Acceptance.Tests;

public sealed class CommandAdmissionTests
{
    [Fact]
    public async Task RoutesRefuseUnapprovedOriginBeforeManifestOrBearerTokenAccess()
    {
        var input = new RoutesCommandInput(
            "not-a-reviewed-manifest.json", "https://unapproved.example", "runner",
            "routes.json", "independent-real-sql13-oracle",
            new Dictionary<string, string>(), new List<ExecutableRouteCase>());

        var result = await ExecuteAsync("routes", input,
            name => name == "VPS09_PUBLIC_ORIGIN" ? "https://staging.example" : null);

        Assert.Equal(1, result.Code);
        Assert.Contains("VPS09.RouteOriginNotApproved", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OraclesRefuseUnreviewedCredentialSourceBeforeSqlAccess()
    {
        var input = new OraclesCommandInput("PRODUCTION_SQL_SECRET", "runner", "oracle-",
            new List<string>(), new List<OraclePlan>());

        var result = await ExecuteAsync("oracles", input, _ => null);

        Assert.Equal(1, result.Code);
        Assert.Contains("VPS09.SqlOracleCredentialSourceInvalid", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RoutesRefuseSubstitutedManifestBeforeExecution()
    {
        var input = new RoutesCommandInput(
            "not-a-reviewed-manifest.json", "https://staging.example", "runner",
            "routes.json", "independent-real-sql13-oracle",
            new Dictionary<string, string>(), new List<ExecutableRouteCase>());

        var error = Assert.Throws<AcceptanceBlockedException>(() =>
            ManifestAdmission.Validate(input));

        Assert.Equal("VPS09.ManifestPathUntrusted", error.Code);
    }

    private static async Task<(int Code, string Error)> ExecuteAsync<T>(
        string command, T input, Func<string, string?> environment)
    {
        var directory = Path.Combine(Path.GetTempPath(), "vps09-command-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var inputPath = Path.Combine(directory, "input.json");
            var outputPath = Path.Combine(directory, "output.json");
            await File.WriteAllTextAsync(inputPath, JsonSerializer.Serialize(input));
            using var output = new StringWriter();
            using var error = new StringWriter();
            var code = await CommandLine.RunAsync(
                new[] { command, "--input", inputPath, "--output", outputPath },
                new FakeGitStateProvider(), environment, output, error, CancellationToken.None);
            Assert.False(File.Exists(outputPath));
            return (code, error.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeGitStateProvider : IGitStateProvider
    {
        public GitState Read() => new(new string('a', 40), true);
    }
}
