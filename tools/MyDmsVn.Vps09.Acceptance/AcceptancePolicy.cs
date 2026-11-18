using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;

namespace MyDmsVn.Vps09.Acceptance;

public sealed class AcceptanceBlockedException : InvalidOperationException
{
    public AcceptanceBlockedException(string code) : base(code) => Code = code;

    public string Code { get; }
}

public sealed record PreflightInput(
    string ReviewedSha,
    string ActualSha,
    bool SourceTreeClean,
    string Origin,
    string SqlOracleConnectionString,
    string Admission,
    string GovernanceMode,
    string DecisionId,
    string Owner,
    string RunnerSha256,
    string DatabaseClassification);

public sealed record PreflightReceipt(
    string SourceSha,
    string PublicHost,
    string Database,
    string GovernanceMode,
    string DecisionId,
    string Owner,
    string RunnerSha256,
    string SqlTransport);

public static class PreflightValidator
{
    private static readonly Regex Sha = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);

    public static PreflightReceipt Validate(PreflightInput input)
    {
        Block(Sha.IsMatch(input.ReviewedSha) && input.ActualSha == input.ReviewedSha,
            "VPS09.ReviewedGitShaMismatch");
        Block(input.SourceTreeClean, "VPS09.SourceTreeMustBeClean");
        Block(input.Admission == "reviewed-isolated-sql13-only",
            "VPS09.IsolatedSqlApprovalMissing");
        Block(input.DatabaseClassification == "isolated-nonproduction",
            "VPS09.DatabaseNotIsolatedNonproduction");

        Block(Uri.TryCreate(input.Origin, UriKind.Absolute, out var origin)
              && origin.Scheme == Uri.UriSchemeHttps
              && origin.AbsolutePath == "/"
              && string.IsNullOrEmpty(origin.Query)
              && string.IsNullOrEmpty(origin.Fragment)
              && string.IsNullOrEmpty(origin.UserInfo)
              && !origin.IsLoopback
              && !string.Equals(origin.Host, "localhost", StringComparison.OrdinalIgnoreCase)
              && input.Origin.TrimEnd('/') == origin.GetLeftPart(UriPartial.Authority),
            "VPS09.RequireCanonicalPublicHttpsOrigin");
        var admittedOrigin = origin!;

        var sql = ParseSql(input.SqlOracleConnectionString);
        var encrypt = Value(sql, "encrypt")?.ToLowerInvariant();
        var trust = Value(sql, "trustservercertificate")?.ToLowerInvariant();
        Block(encrypt is "true" or "mandatory" or "strict" && trust == "false",
            "VPS09.SqlOracleTlsNotVerified");
        var database = Value(sql, "initialcatalog", "database");
        Block(!string.IsNullOrWhiteSpace(database)
              && !string.Equals(database, "master", StringComparison.OrdinalIgnoreCase),
            "VPS09.SqlOracleDatabaseInvalid");

        Block(input.GovernanceMode == "single-owner-exception"
              && input.DecisionId == "OWN-VPS-04"
              && input.Owner == "chung6a8m", "VPS09.SingleOwnerGovernanceMissing");
        Block(RouteEvidenceValidator.Digest(input.RunnerSha256),
            "VPS09.FirstPassRunnerIdentityMissing");

        return new PreflightReceipt(input.ReviewedSha, admittedOrigin.Host, database!,
            input.GovernanceMode, input.DecisionId, input.Owner,
            input.RunnerSha256,
            "encrypt-config-admitted; handshake-required-by-oracles");
    }

    private static Dictionary<string, string> ParseSql(string connectionString)
    {
        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in builder.Keys)
            {
                var normalized = Regex.Replace(key, "[\\s_]", string.Empty).ToLowerInvariant();
                Block(!result.ContainsKey(normalized), "VPS09.SqlOracleConnectionStringAmbiguous");
                result[normalized] = Convert.ToString(builder[key], System.Globalization.CultureInfo.InvariantCulture)
                    ?.Trim() ?? string.Empty;
            }
            return result;
        }
        catch (ArgumentException error)
        {
            throw new AcceptanceBlockedException("VPS09.SqlOracleConnectionStringMalformed:" + error.GetType().Name);
        }
    }

    private static string? Value(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
            if (values.TryGetValue(key, out var value)) return value;
        return null;
    }

    internal static void Block(bool condition, string code)
    {
        if (!condition) throw new AcceptanceBlockedException(code);
    }
}

public sealed record ExpectedRouteCase(
    string CaseId, string Variant, int Status, string ExpectedBodySha256);

public sealed record RouteObservation(
    string CaseId,
    string Variant,
    int Status,
    string BodySha256,
    string Environment,
    string Runner,
    string EvidenceArtifact,
    string ExpectedBodySha256 = "",
    string Diff = "");

public sealed record OracleObservation(
    string Scenario,
    string QueryFingerprint,
    string ExpectedSha256,
    string ActualSha256,
    string Diff,
    string Runner,
    string EvidenceArtifact);

public sealed record EvidenceReceipt(int Executed, int Passed, int Failed, int Skipped);

public static class RouteEvidenceValidator
{
    public static EvidenceReceipt Validate(
        IReadOnlyCollection<ExpectedRouteCase> expected,
        IReadOnlyCollection<RouteObservation> observed)
    {
        var expectedMap = Unique(expected, item => (item.CaseId, item.Variant),
            "VPS09.ExpectedRouteDuplicate");
        var observedMap = Unique(observed, item => (item.CaseId, item.Variant),
            "VPS09.RouteObservationDuplicate");
        PreflightValidator.Block(expectedMap.Count == observedMap.Count
                                 && expectedMap.Keys.All(observedMap.ContainsKey),
            "VPS09.RouteObservationCoverageGap");
        foreach (var pair in expectedMap)
        {
            var actual = observedMap[pair.Key];
            PreflightValidator.Block(actual.Environment == "protected-real-sql13"
                                     && !string.IsNullOrWhiteSpace(actual.Runner)
                                     && !string.IsNullOrWhiteSpace(actual.EvidenceArtifact)
                                     && actual.Status == pair.Value.Status
                                     && Digest(actual.BodySha256)
                                     && actual.ExpectedBodySha256 == pair.Value.ExpectedBodySha256
                                     && actual.BodySha256 == pair.Value.ExpectedBodySha256
                                     && string.IsNullOrEmpty(actual.Diff),
                "VPS09.RouteObservationMismatch");
        }
        return new EvidenceReceipt(observed.Count, observed.Count, 0, 0);
    }

    private static Dictionary<TKey, TValue> Unique<TValue, TKey>(
        IEnumerable<TValue> values, Func<TValue, TKey> key, string code) where TKey : notnull
    {
        var result = new Dictionary<TKey, TValue>();
        foreach (var value in values)
            PreflightValidator.Block(result.TryAdd(key(value), value), code);
        return result;
    }

    internal static bool Digest(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public static class OracleEvidenceValidator
{
    public static EvidenceReceipt Validate(
        IReadOnlyCollection<string> required,
        IReadOnlyCollection<OracleObservation> observations)
    {
        PreflightValidator.Block(required.Count == required.Distinct(StringComparer.Ordinal).Count(),
            "VPS09.OracleRegistryDuplicate");
        var map = new Dictionary<string, OracleObservation>(StringComparer.Ordinal);
        foreach (var observation in observations)
            PreflightValidator.Block(map.TryAdd(observation.Scenario, observation),
                "VPS09.SqlOracleDuplicate");
        PreflightValidator.Block(map.Count == required.Count && required.All(map.ContainsKey),
            "VPS09.SqlOracleCoverageGap");
        foreach (var scenario in required)
        {
            var observation = map[scenario];
            PreflightValidator.Block(RouteEvidenceValidator.Digest(observation.QueryFingerprint)
                                     && RouteEvidenceValidator.Digest(observation.ExpectedSha256)
                                     && RouteEvidenceValidator.Digest(observation.ActualSha256)
                                     && observation.ExpectedSha256 == observation.ActualSha256
                                     && !string.IsNullOrWhiteSpace(observation.Runner)
                                     && !string.IsNullOrWhiteSpace(observation.EvidenceArtifact),
                "VPS09.SqlOracleMismatch");
            PreflightValidator.Block(string.IsNullOrEmpty(observation.Diff), "VPS09.SqlOracleDiff");
        }
        return new EvidenceReceipt(observations.Count, observations.Count, 0, 0);
    }
}

public sealed record AcceptanceSummary(
    int SchemaVersion,
    string SourceSha,
    EvidenceReceipt Routes,
    EvidenceReceipt Oracles,
    bool CleanupConfirmed,
    string OverallRelease);

public static class SummaryBuilder
{
    public static AcceptanceSummary Build(
        string sourceSha,
        IReadOnlyCollection<RouteObservation> routes,
        IReadOnlyCollection<OracleObservation> oracles,
        IReadOnlyCollection<string> requiredOracleScenarios,
        bool cleanupConfirmed)
    {
        PreflightValidator.Block(routes.Select(item => (item.CaseId, item.Variant)).Distinct().Count()
                                 == routes.Count,
            "VPS09.RouteObservationDuplicate");
        var expected = routes.Select(item => new ExpectedRouteCase(
            item.CaseId, item.Variant, item.Status, item.ExpectedBodySha256)).ToArray();
        var routeReceipt = RouteEvidenceValidator.Validate(expected, routes);
        var oracleReceipt = OracleEvidenceValidator.Validate(requiredOracleScenarios, oracles);
        return Build(sourceSha, routeReceipt, oracleReceipt, cleanupConfirmed);
    }

    public static AcceptanceSummary Build(
        string sourceSha,
        EvidenceReceipt routes,
        EvidenceReceipt oracles,
        bool cleanupConfirmed)
    {
        PreflightValidator.Block(cleanupConfirmed, "VPS09.CleanupNotConfirmed");
        PreflightValidator.Block(routes.Executed == 293 && routes.Passed == 293
                                 && routes.Failed == 0 && routes.Skipped == 0,
            "VPS09.RouteCountsIncomplete");
        PreflightValidator.Block(oracles.Executed == 12 && oracles.Passed == 12
                                 && oracles.Failed == 0 && oracles.Skipped == 0,
            "VPS09.OracleCountsIncomplete");
        return new AcceptanceSummary(1, sourceSha, routes, oracles, true, "NOT_APPROVED_BY_TOOL");
    }
}
