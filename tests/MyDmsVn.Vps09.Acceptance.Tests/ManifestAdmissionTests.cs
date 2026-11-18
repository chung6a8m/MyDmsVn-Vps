using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MyDmsVn.Vps09.Acceptance;
using Xunit;

namespace MyDmsVn.Vps09.Acceptance.Tests;

public sealed class ManifestAdmissionTests
{
    [Fact]
    public void RepositoryManifestProducesExactly293ExecutableCases()
    {
        var manifestPath = FindRepositoryFile("docs", "testing", "vps-09-live-scenarios.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var cases = new List<ExecutableRouteCase>();

        foreach (var scenario in document.RootElement.GetProperty("scenarios").EnumerateArray())
        {
            var request = scenario.GetProperty("request");
            var expected = scenario.GetProperty("expected");
            cases.Add(new ExecutableRouteCase(
                scenario.GetProperty("caseId").GetString()!,
                scenario.GetProperty("variant").GetString()!,
                request.GetProperty("method").GetString()!,
                Regex.Replace(request.GetProperty("pathTemplate").GetString()!, "\\{[^}]+\\}", "1"),
                null,
                request.GetProperty("actor").GetString()!,
                new Dictionary<string, string>(),
                expected.GetProperty("status").GetInt32(),
                expected.GetProperty("contractBodySha256").GetString()!));
        }

        var input = new RoutesCommandInput(
            manifestPath,
            "https://staging.example",
            "runner01",
            "routes.json",
            "independent-real-sql13-oracle",
            new Dictionary<string, string>(),
            cases);

        ManifestAdmission.Validate(input);
        Assert.Equal(293, cases.Count);
    }

    [Fact]
    public void SummaryCannotAcceptSelfDeclared293CasesWithoutCanonicalCoverage()
    {
        var fabricated = Enumerable.Range(0, 293).Select(index =>
            new RouteObservation("invented-" + index, "success", 200,
                new string('a', 64), "protected-real-sql13", "runner", "routes.json",
                new string('a', 64), string.Empty)).ToArray();
        var registry = ReadOracleRegistry();

        var error = Assert.Throws<AcceptanceBlockedException>(() =>
            ManifestAdmission.ValidateSummaryCoverage(fabricated, registry));

        Assert.Equal("VPS09.RouteObservationCoverageGap", error.Code);
    }

    [Fact]
    public void OracleRegistryCannotBeReplacedByArbitraryTwelveNames()
    {
        var fabricated = Enumerable.Range(1, 12).Select(index => "invented-" + index).ToArray();

        var error = Assert.Throws<AcceptanceBlockedException>(() =>
            ManifestAdmission.ValidateOracleRegistry(fabricated));

        Assert.Equal("VPS09.SqlOracleRegistryMismatch", error.Code);
    }

    private static string[] ReadOracleRegistry()
    {
        var path = FindRepositoryFile("docs", "testing", "vps-09-live-scenarios.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("oracleScenarios").EnumerateArray()
            .Select(item => item.GetString()!).ToArray();
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(path))
                return path;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository root was not found from the test output directory.");
    }
}
