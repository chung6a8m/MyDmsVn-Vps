using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Newtonsoft.Json.Linq;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03EvidenceRecord
{
    internal Vps03EvidenceRecord(JObject data) => Data = data;
    internal JObject Data { get; }
}

internal sealed class Vps03TrxSummary
{
    internal Vps03TrxSummary(int total, int executed, int passed, int failed)
    {
        Total = total;
        Executed = executed;
        Passed = passed;
        Failed = failed;
    }

    internal int Total { get; }
    internal int Executed { get; }
    internal int Passed { get; }
    internal int Failed { get; }
}

internal sealed class Vps03EvidencePaths
{
    internal Vps03EvidencePaths(string jsonPath, string markdownPath)
    {
        JsonPath = jsonPath;
        MarkdownPath = markdownPath;
    }

    internal string JsonPath { get; }
    internal string MarkdownPath { get; }
}

internal static class Vps03EvidenceWriter
{
    private static readonly Regex SecretKeyPattern = new(
        "connection|string|password|pwd|token|cookie|jwt|secret|key",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static Vps03TrxSummary ValidateTrx(string path, IReadOnlyCollection<string> requiredCaseIds)
    {
        var document = new XmlDocument { XmlResolver = null };
        document.Load(path);
        var manager = new XmlNamespaceManager(document.NameTable);
        manager.AddNamespace("t", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010");
        var counters = document.SelectSingleNode("/t:TestRun/t:ResultSummary/t:Counters", manager)
            ?? throw new InvalidDataException("VPS-03 TRX counters are absent.");
        var total = Counter(counters, "total");
        var executed = Counter(counters, "executed");
        var passed = Counter(counters, "passed");
        var failed = Counter(counters, "failed");
        var notExecuted = Counter(counters, "notExecuted");
        if (total <= 0 || executed != total || passed != executed || failed != 0 || notExecuted != 0)
            throw new InvalidDataException("VPS-03 TRX is empty, incomplete, skipped, or failed.");

        var names = document.SelectNodes("/t:TestRun/t:Results/t:UnitTestResult/@testName", manager)!
            .Cast<XmlAttribute>().Select(attribute => attribute.Value).ToArray();
        foreach (var caseId in requiredCaseIds)
        {
            if (!names.Any(name => name.IndexOf(caseId, StringComparison.Ordinal) >= 0))
                throw new InvalidDataException("VPS-03 TRX is missing required case: " + caseId);
        }
        return new Vps03TrxSummary(total, executed, passed, failed);
    }

    internal static Vps03EvidencePaths Write(string directory, Vps03EvidenceRecord record)
    {
        if (!Path.IsPathRooted(directory) || !Directory.Exists(directory))
            throw new InvalidDataException("VPS-03 evidence directory must be an existing absolute directory.");
        ValidateEvidence(record.Data);
        var jsonPath = Path.Combine(directory, "evidence.json");
        var markdownPath = Path.Combine(directory, "evidence.md");
        var json = record.Data.ToString(Newtonsoft.Json.Formatting.Indented);
        File.WriteAllText(jsonPath, json, new UTF8Encoding(false));
        File.WriteAllText(markdownPath, "# VPS-03 Issue #8 acceptance evidence\r\n\r\n```json\r\n" +
            json.Replace("\n", "\r\n") + "\r\n```\r\n", new UTF8Encoding(false));
        return new Vps03EvidencePaths(jsonPath, markdownPath);
    }

    internal static Vps03EvidencePaths WriteIssue7(string directory,
        Vps03EvidenceRecord record, string expectedHead)
    {
        if (!Path.IsPathRooted(directory) || !Directory.Exists(directory))
            throw new InvalidDataException("VPS-03 Issue #7 evidence directory must be an existing absolute directory.");
        ValidateIssue7Evidence(record.Data, expectedHead);
        ValidateEvidence(record.Data);
        var jsonPath = Path.Combine(directory, "evidence.json");
        var markdownPath = Path.Combine(directory, "evidence.md");
        var json = record.Data.ToString(Newtonsoft.Json.Formatting.Indented);
        File.WriteAllText(jsonPath, json, new UTF8Encoding(false));
        File.WriteAllText(markdownPath, "# VPS-03 Issue #7 acceptance evidence\r\n\r\n```json\r\n" +
            json.Replace("\n", "\r\n") + "\r\n```\r\n", new UTF8Encoding(false));
        return new Vps03EvidencePaths(jsonPath, markdownPath);
    }

    private static void ValidateIssue7Evidence(JObject data, string expectedHead)
    {
        if (!Regex.IsMatch(expectedHead ?? string.Empty, "^[0-9a-f]{40}$") ||
            (string?)data["headSha"] != expectedHead ||
            !Regex.IsMatch((string?)data["baseSha"] ?? string.Empty, "^[0-9a-f]{40}$") ||
            (int?)data["schemaVersion"] != 1 || (int?)data["issue"] != 7)
            throw new InvalidDataException("VPS-03 Issue #7 evidence SHA/schema binding is invalid.");
        if (data["commands"] is not JArray commands || commands.Count == 0 ||
            commands.Any(command => string.IsNullOrWhiteSpace((string?)command)))
            throw new InvalidDataException("VPS-03 Issue #7 evidence commands are absent.");
        if (data["capabilities"] is not JObject || data["blockers"] is not JArray)
            throw new InvalidDataException("VPS-03 Issue #7 capability/blocker evidence is absent.");
        if (data["groups"] is not JArray groups)
            throw new InvalidDataException("VPS-03 Issue #7 groups are absent.");
        var required = new[] { "crash", "file-security", "live", "attended", "cleanup" };
        var names = groups.Children<JObject>().Select(group => (string?)group["name"] ?? string.Empty)
            .ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length ||
            !names.ToHashSet(StringComparer.Ordinal).SetEquals(required))
            throw new InvalidDataException("VPS-03 Issue #7 evidence groups are incomplete or duplicated.");
        foreach (var group in groups.Children<JObject>())
        {
            var total = (int?)group["total"] ?? -1;
            var executed = (int?)group["executed"] ?? -1;
            var passed = (int?)group["passed"] ?? -1;
            var failed = (int?)group["failed"] ?? -1;
            var skipped = (int?)group["skipped"] ?? -1;
            if (total <= 0 || executed != total || passed != total || failed != 0 || skipped != 0)
                throw new InvalidDataException("VPS-03 Issue #7 evidence group is empty, incomplete, skipped, or failed.");
        }
        if (data["cleanup"] is not JObject cleanup || new[]
        {
            "childAbsent", "iisAbsent", "sqlAbsent", "intentAbsent",
        }.Any(name => (bool?)cleanup[name] != true))
            throw new InvalidDataException("VPS-03 Issue #7 cleanup evidence is incomplete.");
    }

    private static int Counter(XmlNode counters, string name)
    {
        var value = counters.Attributes?[name]?.Value;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result < 0)
            throw new InvalidDataException("VPS-03 TRX counter is invalid: " + name);
        return result;
    }

    private static void ValidateEvidence(JToken token)
    {
        if (token is JObject item)
        {
            foreach (var property in item.Properties())
            {
                if (SecretKeyPattern.IsMatch(property.Name))
                    throw new InvalidDataException("Secret-shaped evidence key is forbidden: " + property.Name);
                ValidateEvidence(property.Value);
            }
        }
        else if (token is JArray array)
        {
            foreach (var child in array) ValidateEvidence(child);
        }
    }
}
