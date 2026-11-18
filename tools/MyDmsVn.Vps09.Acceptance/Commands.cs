using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MyDmsVn.Vps09.Acceptance;

public sealed record PreflightCommandInput(
    string ReviewedSha,
    string Origin,
    string SqlOracleConnectionStringEnvironment,
    string Admission,
    string Operator,
    string Reviewer,
    string Owner,
    string DatabaseClassification);

public sealed record RoutesCommandInput(
    string ManifestPath,
    string Origin,
    string Runner,
    string EvidenceArtifact,
    string ExpectedProvenance,
    Dictionary<string, string> ActorTokenEnvironment,
    List<ExecutableRouteCase> Cases);

public sealed record OraclesCommandInput(
    string SqlOracleConnectionStringEnvironment,
    string Runner,
    string EvidenceArtifactPrefix,
    List<string> RequiredScenarios,
    List<OraclePlan> Plans);

public sealed record SummaryCommandInput(
    string SourceSha,
    string RouteEvidencePath,
    string OracleEvidencePath,
    List<string> RequiredOracleScenarios,
    bool CleanupConfirmed);

public sealed record GitState(string Sha, bool Clean);

public interface IGitStateProvider
{
    GitState Read();
}

public sealed class ProcessGitStateProvider : IGitStateProvider
{
    public GitState Read()
    {
        var sha = Run("rev-parse HEAD").Trim();
        var status = Run("status --porcelain --untracked-files=all");
        return new GitState(sha, string.IsNullOrWhiteSpace(status));
    }

    private static string Run(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        PreflightValidator.Block(process is not null, "VPS09.GitUnavailable");
        var output = process!.StandardOutput.ReadToEnd();
        process.WaitForExit();
        PreflightValidator.Block(process.ExitCode == 0, "VPS09.GitStateUnavailable");
        return output;
    }
}

public static class CommandLine
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task<int> RunAsync(
        string[] args,
        IGitStateProvider git,
        Func<string, string?> environment,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            var (command, inputPath, outputPath) = Parse(args);
            switch (command)
            {
                case "preflight":
                    RunPreflight(inputPath, outputPath, git, environment);
                    break;
                case "routes":
                    await RunRoutesAsync(inputPath, outputPath, environment, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case "oracles":
                    await RunOraclesAsync(inputPath, outputPath, environment, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case "summarize":
                    RunSummary(inputPath, outputPath);
                    break;
                default:
                    throw new AcceptanceBlockedException("VPS09.CommandUnknown");
            }
            await output.WriteLineAsync("VPS09_ACCEPTANCE_COMMAND_PASS: " + command).ConfigureAwait(false);
            return 0;
        }
        catch (AcceptanceBlockedException blocked)
        {
            await error.WriteLineAsync("VPS09_ACCEPTANCE_BLOCKED: " + blocked.Code).ConfigureAwait(false);
            return 1;
        }
        catch (Exception unexpected) when (unexpected is IOException or JsonException
            or UnauthorizedAccessException or DbException or HttpRequestException
            or TaskCanceledException or UriFormatException)
        {
            await error.WriteLineAsync("VPS09_ACCEPTANCE_BLOCKED: " + unexpected.GetType().Name)
                .ConfigureAwait(false);
            return 1;
        }
    }

    private static void RunPreflight(
        string inputPath, string outputPath, IGitStateProvider git,
        Func<string, string?> environment)
    {
        var input = Read<PreflightCommandInput>(inputPath);
        var connection = RequiredEnvironment(environment, input.SqlOracleConnectionStringEnvironment);
        var state = git.Read();
        var receipt = PreflightValidator.Validate(new PreflightInput(
            input.ReviewedSha, state.Sha, state.Clean, input.Origin, connection,
            input.Admission, input.Operator, input.Reviewer, input.Owner,
            input.DatabaseClassification));
        WriteNew(outputPath, receipt);
    }

    private static async Task RunRoutesAsync(
        string inputPath, string outputPath, Func<string, string?> environment,
        CancellationToken cancellationToken)
    {
        var input = Read<RoutesCommandInput>(inputPath);
        ManifestAdmission.Validate(input);
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var actor in input.ActorTokenEnvironment)
            tokens.Add(actor.Key, RequiredEnvironment(environment, actor.Value));
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
        var runner = new RouteRunner(client);
        var origin = new Uri(input.Origin, UriKind.Absolute);
        var observations = new List<RouteObservation>();
        foreach (var scenario in input.Cases)
            observations.Add(await runner.ExecuteAsync(origin, scenario, tokens, input.Runner,
                input.EvidenceArtifact, cancellationToken).ConfigureAwait(false));
        var expected = input.Cases.Select(item => new ExpectedRouteCase(
            item.CaseId, item.Variant, item.ExpectedStatus, item.ExpectedBodySha256)).ToArray();
        RouteEvidenceValidator.Validate(expected, observations);
        WriteNew(outputPath, observations);
    }

    private static async Task RunOraclesAsync(
        string inputPath, string outputPath, Func<string, string?> environment,
        CancellationToken cancellationToken)
    {
        var input = Read<OraclesCommandInput>(inputPath);
        PreflightValidator.Block(input.RequiredScenarios.Count == 12
                                 && input.Plans.Count == 12,
            "VPS09.SqlOracleCoverageGap");
        var connection = RequiredEnvironment(environment, input.SqlOracleConnectionStringEnvironment);
        ValidateSqlTransport(connection);
        var runner = new OracleRunner(new SqlOracleQueryExecutor(connection));
        var observations = new List<OracleObservation>();
        foreach (var plan in input.Plans)
            observations.Add(await runner.ExecuteAsync(plan, input.Runner,
                input.EvidenceArtifactPrefix + plan.Scenario + ".json", cancellationToken)
                .ConfigureAwait(false));
        OracleEvidenceValidator.Validate(input.RequiredScenarios, observations);
        WriteNew(outputPath, observations);
    }

    private static void RunSummary(string inputPath, string outputPath)
    {
        var input = Read<SummaryCommandInput>(inputPath);
        var routes = Read<List<RouteObservation>>(input.RouteEvidencePath);
        var oracles = Read<List<OracleObservation>>(input.OracleEvidencePath);
        var summary = SummaryBuilder.Build(input.SourceSha, routes, oracles,
            input.RequiredOracleScenarios, input.CleanupConfirmed);
        WriteNew(outputPath, summary);
    }

    private static void ValidateSqlTransport(string connectionString)
    {
        PreflightValidator.Validate(new PreflightInput(
            new string('a', 40), new string('a', 40), true,
            "https://sql-admission.invalid", connectionString,
            "reviewed-isolated-sql13-only", "operator", "reviewer", "owner",
            "isolated-nonproduction"));
    }

    private static string RequiredEnvironment(Func<string, string?> environment, string name)
    {
        PreflightValidator.Block(!string.IsNullOrWhiteSpace(name), "VPS09.EnvironmentNameMissing");
        var value = environment(name);
        PreflightValidator.Block(!string.IsNullOrWhiteSpace(value), "VPS09.ProtectedSettingMissing:" + name);
        return value!;
    }

    private static (string Command, string Input, string Output) Parse(string[] args)
    {
        PreflightValidator.Block(args.Length == 5 && args[1] == "--input" && args[3] == "--output",
            "VPS09.Usage");
        return (args[0], Path.GetFullPath(args[2]), Path.GetFullPath(args[4]));
    }

    private static T Read<T>(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = JsonSerializer.Deserialize<T>(stream, Json);
        PreflightValidator.Block(result is not null, "VPS09.JsonInputInvalid");
        return result!;
    }

    private static void WriteNew<T>(string path, T value)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, value, Json);
        stream.WriteByte((byte)'\n');
    }
}

public static class ManifestAdmission
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static void Validate(RoutesCommandInput input)
    {
        PreflightValidator.Block(input.ExpectedProvenance == "independent-real-sql13-oracle",
            "VPS09.ExpectedProvenanceInvalid");
        using var stream = File.OpenRead(input.ManifestPath);
        var manifest = JsonSerializer.Deserialize<LiveManifest>(stream, Json);
        PreflightValidator.Block(manifest is not null && manifest.Scenarios.Count == 293
                                 && input.Cases.Count == 293,
            "VPS09.RouteObservationCoverageGap");
        var definitions = manifest!.Scenarios.ToDictionary(
            item => (item.CaseId, item.Variant), item => item);
        var seen = new HashSet<(string, string)>();
        foreach (var item in input.Cases)
        {
            var key = (item.CaseId, item.Variant);
            PreflightValidator.Block(seen.Add(key), "VPS09.RouteObservationDuplicate");
            PreflightValidator.Block(definitions.TryGetValue(key, out var definition),
                "VPS09.RouteObservationUnknown");
            PreflightValidator.Block(item.Method == definition!.Request.Method,
                "VPS09.RouteExecutionMethodMismatch");
            PreflightValidator.Block(item.Actor == definition.Request.Actor,
                "VPS09.RouteExecutionActorMismatch");
            PreflightValidator.Block(item.ExpectedStatus == definition.Expected.Status,
                "VPS09.RouteExecutionStatusMismatch");
            PreflightValidator.Block(MatchesTemplate(definition.Request.PathTemplate, item.Path),
                "VPS09.RouteExecutionPathMismatch");
            PreflightValidator.Block(RouteEvidenceValidator.Digest(item.ExpectedBodySha256),
                "VPS09.RouteExecutionDigestInvalid");
        }
    }

    private static bool MatchesTemplate(string template, string path)
    {
        const string parameterMarker = "__VPS09_ROUTE_PARAMETER__";
        var marked = Regex.Replace(template, @"\{[^}]+\}", parameterMarker,
            RegexOptions.CultureInvariant);
        var pattern = Regex.Escape(marked).Replace(parameterMarker, "[^/?]+", StringComparison.Ordinal);
        return Regex.IsMatch(path, "^" + pattern + @"(?:\?.*)?$", RegexOptions.CultureInvariant);
    }

    private sealed record LiveManifest(List<LiveScenario> Scenarios);
    private sealed record LiveScenario(string CaseId, string Variant, LiveRequest Request, LiveExpected Expected);
    private sealed record LiveRequest(string Method, string PathTemplate, string Actor);
    private sealed record LiveExpected(int Status);
}
