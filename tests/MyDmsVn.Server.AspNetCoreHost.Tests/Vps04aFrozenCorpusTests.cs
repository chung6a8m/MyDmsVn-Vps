using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MyDmsVn.Server.HttpHost;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

// These are source-frozen *files*, not 178 executable HTTP request/response scenarios.
// Run in the actual net10 test graph to detect cross-runtime wire/fingerprint drift.
public sealed class Vps04aFrozenCorpusTests
{
    private static readonly string[] Families =
    {
        "web2-v1", "web3-v1", "web4-v1",
        "web5-resolution-v1", "web6-resend-eligibility-v1"
    };

    private static string RepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var current = new DirectoryInfo(start);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "docs", "contracts", "corpus-blobs.sha1")))
                    return current.FullName;
                current = current.Parent;
            }
        }
        throw new InvalidOperationException("Frozen corpus manifest is not present in this checkout.");
    }

    private static IReadOnlyDictionary<string, string> Entries()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), "docs", "contracts", "corpus-blobs.sha1"));
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines.Where(line => line.Length != 0))
        {
            if (line.Length < 43 || line[40] != ' ' || line[41] != ' ')
                throw new InvalidDataException("Invalid Git blob pin.");
            var sha = line.Substring(0, 40);
            var path = line.Substring(42);
            if (!sha.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f') ||
                !Families.Any(f => path.StartsWith("docs/contracts/" + f + "/", StringComparison.Ordinal)) ||
                path.Contains("..", StringComparison.Ordinal) ||
                !entries.TryAdd(path, sha))
                throw new InvalidDataException("Duplicate, untrusted or malformed frozen corpus entry.");
        }
        return entries;
    }

    public static TheoryData<string> FrozenFiles
    {
        get
        {
            var result = new TheoryData<string>();
            foreach (var path in Entries().Keys.OrderBy(x => x, StringComparer.Ordinal))
                result.Add(path);
            return result;
        }
    }

    [Fact]
    public void FrozenCorpusHasExactly178TrackedFilesAndNoUnpinnedAdditions()
    {
        var pinned = Entries();
        Assert.Equal(178, pinned.Count);
        var files = Families.SelectMany(family => Directory.EnumerateFiles(
            Path.Combine(RepoRoot(), "docs", "contracts", family), "*",
            SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        Assert.Equal(pinned.Keys.OrderBy(x => x, StringComparer.Ordinal), files);
        Assert.Equal(new[] { 34, 38, 48, 41, 17 },
            Families.Select(f => files.Count(p => p.StartsWith("docs/contracts/" + f + "/", StringComparison.Ordinal))));
    }

    [Theory]
    [MemberData(nameof(FrozenFiles))]
    public void EachFrozenFileMatchesItsOriginalGitBlobAndJsonGrammar(string relativePath)
    {
        var data = File.ReadAllBytes(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Encoding.ASCII.GetBytes("blob " + data.Length.ToString(CultureInfo.InvariantCulture) + "\0");
        var buffer = new byte[prefix.Length + data.Length];
        Buffer.BlockCopy(prefix, 0, buffer, 0, prefix.Length);
        Buffer.BlockCopy(data, 0, buffer, prefix.Length, data.Length);
        var actual = Convert.ToHexString(SHA1.HashData(buffer)).ToLowerInvariant();
        Assert.Equal(Entries()[relativePath], actual);
        if (relativePath.EndsWith(".json", StringComparison.Ordinal))
        {
            var json = new UTF8Encoding(false, true).GetString(data);
            using var document = JsonDocument.Parse(json.TrimStart('\uFEFF'));
            Assert.True(document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array);
        }
    }

    [Fact]
    public void Net10LegacyWireFingerprintMatchesIndependentFrozenCanonicalVectors()
    {
        var file = Path.Combine(RepoRoot(), "docs", "contracts", "web4-v1", "fingerprint-vectors.json");
        using var vectors = JsonDocument.Parse(File.ReadAllBytes(file));
        var count = 0;
        foreach (var vector in vectors.RootElement.EnumerateArray())
        {
            var intent = vector.GetProperty("intent");
            var kind = WebOperationWireInput.ParseKind(intent.GetProperty("operationKind").GetString()!);
            var targetValue = intent.GetProperty("requestTargetId");
            long? target = targetValue.ValueKind == JsonValueKind.Null ? null :
                long.Parse(targetValue.GetString()!, CultureInfo.InvariantCulture);
            var body = JObject.Parse(intent.GetProperty("body").GetRawText());
            var canonical = WebOperationWireInput.Canonicalize(kind, target, body);
            Assert.Equal(vector.GetProperty("canonical").GetString(), canonical);
            var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
            Assert.Equal(vector.GetProperty("sha256").GetString(), sha);
            count++;
        }
        Assert.True(count >= 6, "All six command kinds must have independently pinned vectors.");
    }
}
