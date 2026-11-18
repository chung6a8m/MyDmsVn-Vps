using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyDmsVn.Vps09.Acceptance;
using Xunit;

namespace MyDmsVn.Vps09.Acceptance.Tests;

public sealed class ExecutionTests
{
    private static readonly string OkDigest = Hex("{\"data\":true}");

    [Fact]
    public async Task RouteRunnerUsesPublicHttpsAndProducesSanitizedObservation()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{\"data\":true}");
        var runner = new RouteRunner(new HttpMessageInvoker(handler));
        var scenario = Scenario(expectedStatus: 200, expectedDigest: OkDigest);

        var observation = await runner.ExecuteAsync(
            new Uri("https://staging.example"), scenario,
            new Dictionary<string, string> { ["writer"] = "private-token" },
            "windows-runner-01", "route-case-a.json", CancellationToken.None);

        Assert.Equal(OkDigest, observation.BodySha256);
        Assert.Equal("protected-real-sql13", observation.Environment);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("private-token", handler.LastRequest.Headers.Authorization.Parameter);
        Assert.DoesNotContain("private-token", observation.ToString(), StringComparison.Ordinal);
        Assert.Equal(new Uri("https://staging.example/api/v1/health"), handler.LastRequest.RequestUri);
    }

    [Fact]
    public async Task RouteRunnerRefusesStatusOrBodyMismatch()
    {
        var runner = new RouteRunner(new HttpMessageInvoker(
            new RecordingHandler(HttpStatusCode.InternalServerError, "{\"error\":true}")));

        var error = await Assert.ThrowsAsync<AcceptanceBlockedException>(() => runner.ExecuteAsync(
            new Uri("https://staging.example"), Scenario(200, OkDigest),
            new Dictionary<string, string> { ["writer"] = "token" },
            "runner", "artifact.json", CancellationToken.None));

        Assert.Equal("VPS09.RouteObservationMismatch", error.Code);
    }

    [Fact]
    public async Task RouteRunnerRefusesRedirectEvenWhenExpectedStatusWasMisconfigured()
    {
        var runner = new RouteRunner(new HttpMessageInvoker(
            new RecordingHandler(HttpStatusCode.Redirect, string.Empty)));

        var error = await Assert.ThrowsAsync<AcceptanceBlockedException>(() => runner.ExecuteAsync(
            new Uri("https://staging.example"), Scenario(302, Hex(string.Empty)),
            new Dictionary<string, string> { ["writer"] = "token" },
            "runner", "artifact.json", CancellationToken.None));

        Assert.Equal("VPS09.RedirectRefused", error.Code);
    }

    [Theory]
    [InlineData("Host")]
    [InlineData("X-Forwarded-For")]
    [InlineData("X-Forwarded-Proto")]
    [InlineData("X-MyDmsVn-Proxy-Token")]
    [InlineData("X-Forwarded-Host")]
    [InlineData("Forwarded")]
    [InlineData("X-Original-Url")]
    [InlineData("X-Real-IP")]
    public async Task RouteRunnerRefusesHeadersThatCouldBypassPublicNginx(string header)
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{\"data\":true}");
        var runner = new RouteRunner(new HttpMessageInvoker(handler));
        var scenario = Scenario(200, OkDigest) with
        {
            Headers = new Dictionary<string, string> { [header] = "attacker-controlled" }
        };

        var error = await Assert.ThrowsAsync<AcceptanceBlockedException>(() => runner.ExecuteAsync(
            new Uri("https://staging.example"), scenario,
            new Dictionary<string, string> { ["writer"] = "token" },
            "runner", "artifact.json", CancellationToken.None));

        Assert.Equal("VPS09.ProtectedHeaderOverrideForbidden", error.Code);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task OracleRunnerHashesQueryAndCanonicalExpectedActualValues()
    {
        var executor = new FakeOracleExecutor("{\"rows\":1}");
        var runner = new OracleRunner(executor);
        var plan = new OraclePlan("stock-ledger", "SELECT COUNT(*) AS rows FROM dbo.StockLedger",
            "{\"rows\":1}");

        var observation = await runner.ExecuteAsync(plan, "sql-runner", "stock-ledger.json",
            CancellationToken.None);

        Assert.Equal(Hex(plan.Query), observation.QueryFingerprint);
        Assert.Equal(observation.ExpectedSha256, observation.ActualSha256);
        Assert.Equal(string.Empty, observation.Diff);
        Assert.Equal(plan.Query, executor.Query);
    }

    [Fact]
    public async Task OracleRunnerRefusesMutationAndDoesNotCallDatabase()
    {
        var executor = new FakeOracleExecutor("{}");
        var runner = new OracleRunner(executor);

        var error = await Assert.ThrowsAsync<AcceptanceBlockedException>(() => runner.ExecuteAsync(
            new OraclePlan("cleanup", "DELETE FROM dbo.StockLedger", "{}"),
            "runner", "cleanup.json", CancellationToken.None));

        Assert.Equal("VPS09.SqlOracleReadOnlyQueryRequired", error.Code);
        Assert.Null(executor.Query);
    }

    [Theory]
    [InlineData("SELECT * INTO dbo.UnreviewedTable FROM dbo.StockLedger")]
    [InlineData("WITH src AS (SELECT 1 AS x) SELECT x INTO #temp FROM src")]
    [InlineData("SELECT * FROM OPENROWSET(BULK 'C:\\file', SINGLE_BLOB) AS v")]
    public async Task OracleRunnerRejectsSelectSideEffectsBeforeDatabaseAccess(string query)
    {
        var executor = new FakeOracleExecutor("{}");
        var runner = new OracleRunner(executor);

        var error = await Assert.ThrowsAsync<AcceptanceBlockedException>(() =>
            runner.ExecuteAsync(new OraclePlan("cleanup", query, "{}"),
                "runner", "cleanup.json", CancellationToken.None));

        Assert.Equal("VPS09.SqlOracleReadOnlyQueryRequired", error.Code);
        Assert.Null(executor.Query);
    }

    [Fact]
    public async Task OracleRunnerPreservesDiffInsteadOfClaimingPass()
    {
        var runner = new OracleRunner(new FakeOracleExecutor("{\"rows\":2}"));

        var observation = await runner.ExecuteAsync(
            new OraclePlan("cleanup", "SELECT COUNT(*) AS rows FROM dbo.StockLedger", "{\"rows\":0}"),
            "runner", "cleanup.json", CancellationToken.None);

        Assert.NotEmpty(observation.Diff);
        Assert.NotEqual(observation.ExpectedSha256, observation.ActualSha256);
    }

    private static ExecutableRouteCase Scenario(int expectedStatus, string expectedDigest) => new(
        "case-a", "success", "GET", "/api/v1/health", null, "writer",
        new Dictionary<string, string>(), expectedStatus, expectedDigest);

    private static string Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public RecordingHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeOracleExecutor : IOracleQueryExecutor
    {
        private readonly string _result;

        public FakeOracleExecutor(string result) => _result = result;

        public string? Query { get; private set; }

        public Task<string> ExecuteCanonicalJsonAsync(string query, CancellationToken cancellationToken)
        {
            Query = query;
            return Task.FromResult(_result);
        }
    }
}
