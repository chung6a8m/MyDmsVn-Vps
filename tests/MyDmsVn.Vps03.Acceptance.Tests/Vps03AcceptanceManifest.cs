using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03AcceptanceCase
{
    internal Vps03AcceptanceCase(string caseId, string kind, string interfaceName, string method,
        string httpMethod, string path, string executor)
    {
        CaseId = caseId;
        Kind = kind;
        Interface = interfaceName;
        Method = method;
        HttpMethod = httpMethod;
        Path = path;
        Executor = executor;
    }

    internal string CaseId { get; }
    internal string Kind { get; }
    internal string Interface { get; }
    internal string Method { get; }
    internal string HttpMethod { get; }
    internal string Path { get; }
    internal string Executor { get; }
    internal string MethodIdentity => string.Join("|", new[] { Interface, Method, HttpMethod, Path });
}

internal sealed class Vps03AcceptanceManifest
{
    private Vps03AcceptanceManifest(IReadOnlyList<Vps03AcceptanceCase> cases) => Cases = cases;

    internal IReadOnlyList<Vps03AcceptanceCase> Cases { get; }

    internal static Vps03AcceptanceManifest Load(string path)
    {
        var document = JObject.Parse(File.ReadAllText(path));
        if ((int?)document["schemaVersion"] != 1 || document["cases"] is not JArray rows)
            throw new InvalidDataException("VPS-03 acceptance manifest schema is invalid.");
        var cases = rows.Children<JObject>().Select(row => new Vps03AcceptanceCase(
            Required(row, "caseId"), Required(row, "kind"), Optional(row, "interface"),
            Optional(row, "method"), Optional(row, "httpMethod"), Optional(row, "path"),
            Optional(row, "executor"))).ToArray();
        return new Vps03AcceptanceManifest(cases);
    }

    internal void Validate(JObject sourceMatrix)
    {
        if (Cases.Count == 0) throw new InvalidDataException("VPS-03 acceptance manifest is empty.");
        if (Cases.GroupBy(item => item.CaseId, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new InvalidDataException("VPS-03 acceptance case IDs must be unique.");
        if (Cases.Any(item => item.Kind != "method" && item.Kind != "gate"))
            throw new InvalidDataException("VPS-03 acceptance case kind is invalid.");
        if (Cases.Any(item => string.IsNullOrWhiteSpace(item.Executor)))
            throw new InvalidDataException("VPS-03 acceptance case executor is required.");

        var methods = Cases.Where(item => item.Kind == "method").ToArray();
        if (methods.Any(item => string.IsNullOrWhiteSpace(item.Interface) || string.IsNullOrWhiteSpace(item.Method) ||
            string.IsNullOrWhiteSpace(item.HttpMethod) || string.IsNullOrWhiteSpace(item.Path)))
            throw new InvalidDataException("VPS-03 method case identity is incomplete.");
        if (methods.GroupBy(item => item.MethodIdentity, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new InvalidDataException("VPS-03 method case identities must be unique.");

        if (sourceMatrix["methods"] is not JArray sourceRows)
            throw new InvalidDataException("VPS-02 source method matrix is invalid.");
        var expected = sourceRows.Children<JObject>().Select(row => string.Join("|", new[]
        {
            Required(row, "interface"), Required(row, "method"),
            Required(row, "httpMethod"), Required(row, "path"),
        })).ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(methods.Select(item => item.MethodIdentity)))
            throw new InvalidDataException("VPS-03 method cases do not exactly cover the VPS-02 matrix.");
    }

    private static string Required(JObject row, string name) =>
        string.IsNullOrWhiteSpace((string?)row[name])
            ? throw new InvalidDataException("VPS-03 manifest field is required: " + name)
            : (string)row[name]!;

    private static string Optional(JObject row, string name) => (string?)row[name] ?? string.Empty;
}
