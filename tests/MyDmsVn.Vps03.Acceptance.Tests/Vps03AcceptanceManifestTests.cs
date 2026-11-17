using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MyDmsVn.Vps03.Acceptance.Tests;

public sealed class Vps03AcceptanceManifestTests
{
    private static readonly string[] RequiredIssue7Groups =
    {
        "crash", "file-security", "live", "attended", "cleanup",
    };

    private static readonly string[] RequiredIssue7Cases =
    {
        "VPS03.ISSUE7.CRASH.BEFORE-SAVE", "VPS03.ISSUE7.CRASH.AFTER-SAVE",
        "VPS03.ISSUE7.FILE.CORRUPT", "VPS03.ISSUE7.FILE.ACL",
        "VPS03.ISSUE7.FILE.ORIGIN", "VPS03.ISSUE7.FILE.EXACT-CLEAR",
        "VPS03.ISSUE7.LIVE.RESTART", "VPS03.ISSUE7.LIVE.AUTHORITY",
        "VPS03.ISSUE7.LIVE.SESSION", "VPS03.ISSUE7.LIVE.FAIL-CLOSED",
        "VPS03.ISSUE7.LIVE.OPERATOR-RACE", "VPS03.ISSUE7.LIVE.RUNTIME-DENIED",
        "VPS03.ISSUE7.LIVE.UNJOURNALLED", "VPS03.GUI.UNKNOWN-RECOVERY",
        "VPS03.ISSUE7.CLEANUP",
    };

    private static readonly string[] RequiredGates =
    {
        "package", "tls", "schema11", "schema12", "schema13", "auth", "permissions",
        "sessions", "transactions", "lost-ack", "inventory", "gui", "cleanup",
    };

    [Fact]
    public void Contains_exactly_the_46_remote_method_identities()
    {
        var source = JObject.Parse(File.ReadAllText(RepositoryFile("docs", "contracts", "vps-02-desktop-method-matrix.json")));
        var expected = source["methods"]!.Children<JObject>()
            .Select(MethodIdentity)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var manifest = Vps03AcceptanceManifest.Load(ManifestFile());
        manifest.Validate(source);

        var actual = manifest.Cases.Where(testCase => testCase.Kind == "method")
            .Select(testCase => testCase.MethodIdentity)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(46, actual.Length);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Maps_every_issue8_gate_to_a_stable_case()
    {
        var manifest = Vps03AcceptanceManifest.Load(ManifestFile());
        var gates = manifest.Cases.Where(testCase => testCase.Kind == "gate")
            .Select(testCase => testCase.Executor)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(RequiredGates, gate => Assert.Contains(gate, gates));
        Assert.All(manifest.Cases, testCase =>
        {
            Assert.Matches("^VPS03[.][A-Z0-9.-]+$", testCase.CaseId);
            Assert.False(string.IsNullOrWhiteSpace(testCase.Executor));
        });
    }

    [Fact]
    public void Rejects_unbound_or_duplicate_method_cases()
    {
        var source = JObject.Parse(File.ReadAllText(RepositoryFile("docs", "contracts", "vps-02-desktop-method-matrix.json")));
        var duplicate = new JObject
        {
            ["schemaVersion"] = 1,
            ["cases"] = new JArray(
                MethodCase("VPS03.API.DUPLICATE", "run-product-list"),
                MethodCase("VPS03.API.DUPLICATE", string.Empty)),
        };
        var path = Path.Combine(Path.GetTempPath(), "vps03-manifest-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, duplicate.ToString());
            var manifest = Vps03AcceptanceManifest.Load(path);
            Assert.Throws<InvalidDataException>(() => manifest.Validate(source));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Issue7_manifest_has_stable_nonempty_strict_groups()
    {
        var manifest = Vps03AcceptanceManifest.Load(Path.Combine(
            AppContext.BaseDirectory, "vps03-issue7-case-manifest.json"));
        manifest.ValidateIssue7(RequiredIssue7Groups, RequiredIssue7Cases);

        Assert.All(RequiredIssue7Groups, group =>
            Assert.Contains(manifest.Cases, item => item.Group == group));
    }

    private static JObject MethodCase(string caseId, string executor) => new()
    {
        ["caseId"] = caseId,
        ["kind"] = "method",
        ["interface"] = "IProductApiClient",
        ["method"] = "ListAsync",
        ["httpMethod"] = "GET",
        ["path"] = "/api/v1/catalog/products",
        ["executor"] = executor,
    };

    private static string MethodIdentity(JObject item) => string.Join("|", new[]
    {
        (string)item["interface"]!, (string)item["method"]!,
        (string)item["httpMethod"]!, (string)item["path"]!,
    });

    private static string ManifestFile() => Path.Combine(AppContext.BaseDirectory, "vps03-issue8-case-manifest.json");

    private static string RepositoryFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException("Repository file was not found.", Path.Combine(parts));
    }
}
