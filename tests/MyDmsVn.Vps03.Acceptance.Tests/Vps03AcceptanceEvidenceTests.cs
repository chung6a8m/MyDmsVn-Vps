using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MyDmsVn.Vps03.Acceptance.Tests;

public sealed class Vps03AcceptanceEvidenceTests
{
    [Fact]
    public void Validates_positive_complete_trx_with_every_required_case_id()
    {
        using var fixture = EvidenceFixture.Create();
        var path = fixture.WriteTrx(total: 2, executed: 2, passed: 2, failed: 0,
            "VPS03.API.PRODUCT.LIST", "VPS03.GATE.CLEANUP");

        var summary = Vps03EvidenceWriter.ValidateTrx(path,
            new[] { "VPS03.API.PRODUCT.LIST", "VPS03.GATE.CLEANUP" });

        Assert.Equal(2, summary.Total);
        Assert.Equal(2, summary.Executed);
        Assert.Equal(2, summary.Passed);
        Assert.Equal(0, summary.Failed);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(2, 1, 1, 0)]
    [InlineData(2, 2, 1, 1)]
    public void Rejects_empty_not_executed_skipped_or_failed_trx(int total, int executed, int passed, int failed)
    {
        using var fixture = EvidenceFixture.Create();
        var path = fixture.WriteTrx(total, executed, passed, failed, "VPS03.API.PRODUCT.LIST");

        Assert.Throws<InvalidDataException>(() => Vps03EvidenceWriter.ValidateTrx(path,
            new[] { "VPS03.API.PRODUCT.LIST" }));
    }

    [Fact]
    public void Rejects_trx_missing_a_required_case_id()
    {
        using var fixture = EvidenceFixture.Create();
        var path = fixture.WriteTrx(1, 1, 1, 0, "VPS03.API.PRODUCT.LIST");

        Assert.Throws<InvalidDataException>(() => Vps03EvidenceWriter.ValidateTrx(path,
            new[] { "VPS03.API.PRODUCT.LIST", "VPS03.GATE.CLEANUP" }));
    }

    [Fact]
    public void Writes_only_allowlisted_sanitized_evidence_fields()
    {
        using var fixture = EvidenceFixture.Create();
        var record = new Vps03EvidenceRecord(new JObject
        {
            ["headSha"] = "0123456789abcdef0123456789abcdef01234567",
            ["publicOriginAuthority"] = "localhost:44321",
            ["desktopPackageSha256"] = new string('a', 64),
            ["hostPackageSha256"] = new string('b', 64),
            ["cleanupState"] = "verified",
            ["counts"] = new JObject { ["total"] = 2, ["executed"] = 2, ["failed"] = 0 },
        });

        var paths = Vps03EvidenceWriter.Write(fixture.Root, record);

        Assert.True(File.Exists(paths.JsonPath));
        Assert.True(File.Exists(paths.MarkdownPath));
        Assert.DoesNotContain("connection", File.ReadAllText(paths.JsonPath), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("connectionString")]
    [InlineData("password")]
    [InlineData("refreshToken")]
    [InlineData("cookie")]
    [InlineData("jwt")]
    [InlineData("secret")]
    public void Rejects_secret_shaped_keys_recursively(string key)
    {
        using var fixture = EvidenceFixture.Create();
        var record = new Vps03EvidenceRecord(new JObject
        {
            ["headSha"] = "0123456789abcdef0123456789abcdef01234567",
            ["details"] = new JObject { [key] = "must-not-be-written" },
        });

        Assert.Throws<InvalidDataException>(() => Vps03EvidenceWriter.Write(fixture.Root, record));
        Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    private sealed class EvidenceFixture : IDisposable
    {
        private EvidenceFixture(string root) => Root = root;
        internal string Root { get; }

        internal static EvidenceFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "vps03-evidence-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new EvidenceFixture(root);
        }

        internal string WriteTrx(int total, int executed, int passed, int failed, params string[] caseIds)
        {
            var results = new StringBuilder();
            foreach (var caseId in caseIds)
                results.Append("<UnitTestResult testName=\"").Append(caseId).Append("\" outcome=\"Passed\" />");
            var notExecuted = total - executed;
            var xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\">" +
                "<Results>" + results + "</Results><ResultSummary outcome=\"Completed\"><Counters" +
                " total=\"" + total + "\" executed=\"" + executed + "\" passed=\"" + passed +
                "\" failed=\"" + failed + "\" notExecuted=\"" + notExecuted + "\" /></ResultSummary></TestRun>";
            var path = Path.Combine(Root, "acceptance.trx");
            File.WriteAllText(path, xml, new UTF8Encoding(false));
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
