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
