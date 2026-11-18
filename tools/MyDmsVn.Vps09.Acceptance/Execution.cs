using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace MyDmsVn.Vps09.Acceptance;

public sealed record ExecutableRouteCase(
    string CaseId,
    string Variant,
    string Method,
    string Path,
    string? Body,
    string Actor,
    IReadOnlyDictionary<string, string> Headers,
    int ExpectedStatus,
    string ExpectedBodySha256);

public sealed class RouteRunner
{
    private readonly HttpMessageInvoker _http;

    public RouteRunner(HttpMessageInvoker http) => _http = http;

    public async Task<RouteObservation> ExecuteAsync(
        Uri origin,
        ExecutableRouteCase scenario,
        IReadOnlyDictionary<string, string> actorTokens,
        string runner,
        string evidenceArtifact,
        CancellationToken cancellationToken)
    {
        PreflightValidator.Block(origin.Scheme == Uri.UriSchemeHttps && !origin.IsLoopback
                                 && origin.AbsolutePath == "/" && string.IsNullOrEmpty(origin.Query),
            "VPS09.RequireCanonicalPublicHttpsOrigin");
        var hasTarget = Uri.TryCreate(origin, scenario.Path, out var target);
        PreflightValidator.Block(scenario.Path.StartsWith("/api/", StringComparison.Ordinal)
                                 && !scenario.Path.Contains("..", StringComparison.Ordinal)
                                 && hasTarget && target is not null
                                 && target.Host == origin.Host,
            "VPS09.RoutePathInvalid");
        var admittedTarget = target!;
        PreflightValidator.Block(!string.IsNullOrWhiteSpace(runner)
                                 && !string.IsNullOrWhiteSpace(evidenceArtifact),
            "VPS09.RouteEvidenceIdentityMissing");
        PreflightValidator.Block(RouteEvidenceValidator.Digest(scenario.ExpectedBodySha256),
            "VPS09.ExpectedBodyDigestInvalid");

        using var request = new HttpRequestMessage(new HttpMethod(scenario.Method), admittedTarget);
        if (scenario.Body is not null)
            request.Content = new StringContent(scenario.Body, Encoding.UTF8, "application/json");
        if (scenario.Actor != "anonymous")
        {
            PreflightValidator.Block(actorTokens.TryGetValue(scenario.Actor, out var token)
                                     && !string.IsNullOrWhiteSpace(token),
                "VPS09.ActorTokenMissing:" + scenario.Actor);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        foreach (var header in scenario.Headers)
        {
            var protectedHeader = header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("X-Forwarded-For", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("X-Forwarded-Proto", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("X-MyDmsVn-Proxy-Token", StringComparison.OrdinalIgnoreCase);
            PreflightValidator.Block(!protectedHeader, "VPS09.ProtectedHeaderOverrideForbidden");
            PreflightValidator.Block(request.Headers.TryAddWithoutValidation(header.Key, header.Value),
                "VPS09.RouteHeaderInvalid:" + header.Key);
        }

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        PreflightValidator.Block(status is < 300 or >= 400, "VPS09.RedirectRefused");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        PreflightValidator.Block(status == scenario.ExpectedStatus
                                 && digest == scenario.ExpectedBodySha256,
            "VPS09.RouteObservationMismatch");
        return new RouteObservation(scenario.CaseId, scenario.Variant, status, digest,
            "protected-real-sql13", runner, evidenceArtifact,
            scenario.ExpectedBodySha256, string.Empty);
    }
}

public sealed record OraclePlan(string Scenario, string Query, string ExpectedCanonicalJson);

public interface IOracleQueryExecutor
{
    Task<string> ExecuteCanonicalJsonAsync(string query, CancellationToken cancellationToken);
}

public sealed class OracleRunner
{
    private static readonly Regex Mutation = new(
        @"\b(insert|update|delete|merge|drop|alter|create|truncate|exec(?:ute)?|grant|revoke|deny)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly IOracleQueryExecutor _executor;

    public OracleRunner(IOracleQueryExecutor executor) => _executor = executor;

    public async Task<OracleObservation> ExecuteAsync(
        OraclePlan plan,
        string runner,
        string evidenceArtifact,
        CancellationToken cancellationToken)
    {
        var query = plan.Query.Trim();
        var startsReadOnly = query.StartsWith("select", StringComparison.OrdinalIgnoreCase)
            || query.StartsWith("with", StringComparison.OrdinalIgnoreCase);
        var withoutTrailingTerminator = query.EndsWith(';') ? query[..^1] : query;
        PreflightValidator.Block(startsReadOnly && !Mutation.IsMatch(query)
                                 && !withoutTrailingTerminator.Contains(';'),
            "VPS09.SqlOracleReadOnlyQueryRequired");
        PreflightValidator.Block(!string.IsNullOrWhiteSpace(runner)
                                 && !string.IsNullOrWhiteSpace(evidenceArtifact),
            "VPS09.SqlOracleEvidenceIdentityMissing");
        var expected = CanonicalJson(plan.ExpectedCanonicalJson);
        var actual = CanonicalJson(await _executor.ExecuteCanonicalJsonAsync(query, cancellationToken)
            .ConfigureAwait(false));
        var expectedHash = Hash(expected);
        var actualHash = Hash(actual);
        return new OracleObservation(plan.Scenario, Hash(query), expectedHash, actualHash,
            expectedHash == actualHash ? string.Empty : "sha256-mismatch",
            runner, evidenceArtifact);
    }

    private static string CanonicalJson(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow
        });
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed class SqlOracleQueryExecutor : IOracleQueryExecutor
{
    private readonly string _connectionString;

    public SqlOracleQueryExecutor(string connectionString) => _connectionString = connectionString;

    public async Task<string> ExecuteCanonicalJsonAsync(
        string query, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(query, connection) { CommandTimeout = 45 };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var value = await reader.IsDBNullAsync(index, cancellationToken).ConfigureAwait(false)
                    ? null : Normalize(reader.GetValue(index));
                row.Add(reader.GetName(index), value);
            }
            rows.Add(row);
        }
        return JsonSerializer.Serialize(rows);
    }

    private static object Normalize(object value) => value switch
    {
        DateTime date => date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset date => date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToHexString(bytes).ToLowerInvariant(),
        DbDataRecord record => record.ToString() ?? string.Empty,
        _ => value
    };
}
