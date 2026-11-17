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
