using System;
using System.Collections.Generic;
using System.Linq;
using MyDmsVn.Vps09.Acceptance;
using Xunit;

namespace MyDmsVn.Vps09.Acceptance.Tests;

public sealed class AcceptancePolicyTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void ExactProtectedInputsProduceSanitizedPreflightReceipt()
    {
        var receipt = PreflightValidator.Validate(ValidPreflight());

        Assert.Equal(Sha, receipt.SourceSha);
        Assert.Equal("staging.example", receipt.PublicHost);
        Assert.Equal("Vps09_Isolated", receipt.Database);
        Assert.Equal("operator01", receipt.Operator);
        Assert.DoesNotContain("Password", receipt.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StaleSourceShaIsRefused()
    {
        var input = ValidPreflight() with { ActualSha = new string('f', 40) };

        var error = Assert.Throws<AcceptanceBlockedException>(() => PreflightValidator.Validate(input));

        Assert.Equal("VPS09.ReviewedGitShaMismatch", error.Code);
    }

    [Fact]
    public void DirtySourceTreeIsRefused()
    {
        var input = ValidPreflight() with { SourceTreeClean = false };

        var error = Assert.Throws<AcceptanceBlockedException>(() => PreflightValidator.Validate(input));

        Assert.Equal("VPS09.SourceTreeMustBeClean", error.Code);
    }

    [Theory]
    [InlineData("http://staging.example")]
    [InlineData("https://localhost")]
    [InlineData("https://staging.example/path")]
    [InlineData("https://staging.example?query=1")]
    public void NoncanonicalPublicHttpsOriginIsRefused(string origin)
    {
        var error = Assert.Throws<AcceptanceBlockedException>(() =>
            PreflightValidator.Validate(ValidPreflight() with { Origin = origin }));

        Assert.Equal("VPS09.RequireCanonicalPublicHttpsOrigin", error.Code);
    }

    [Theory]
    [InlineData("Encrypt=False;TrustServerCertificate=False")]
    [InlineData("Encrypt=True;TrustServerCertificate=True")]
    [InlineData("Encrypt=True")]
    public void UnverifiedSqlTlsIsRefused(string transport)
    {
        var input = ValidPreflight() with
        {
            SqlOracleConnectionString = $"Server=sql.example;Initial Catalog=Vps09_Isolated;{transport};User ID=oracle;Password=hidden"
        };

        var error = Assert.Throws<AcceptanceBlockedException>(() => PreflightValidator.Validate(input));

        Assert.Equal("VPS09.SqlOracleTlsNotVerified", error.Code);
    }

    [Fact]
    public void OperatorCannotAlsoBeReviewerOrOwner()
    {
        var error = Assert.Throws<AcceptanceBlockedException>(() =>
            PreflightValidator.Validate(ValidPreflight() with { Reviewer = "OPERATOR01" }));

        Assert.Equal("VPS09.ReviewMustBeIndependent", error.Code);
    }

    [Fact]
    public void RouteEvidenceRequiresEveryUniqueMatchingObservation()
    {
        var expected = new[]
        {
            new ExpectedRouteCase("case-a", "success", 200, Digest),
            new ExpectedRouteCase("case-b", "permission", 403, Digest),
        };
        var observed = new[]
        {
            Observation("case-a", "success", 200),
            Observation("case-b", "permission", 403),
        };

        var receipt = RouteEvidenceValidator.Validate(expected, observed);

        Assert.Equal(2, receipt.Executed);
        Assert.Equal(2, receipt.Passed);
        Assert.Equal(0, receipt.Failed);
        Assert.Equal(0, receipt.Skipped);
    }

    [Fact]
    public void DuplicateOrMissingRouteEvidenceIsRefused()
    {
        var expected = new[]
        {
            new ExpectedRouteCase("case-a", "success", 200, Digest),
            new ExpectedRouteCase("case-b", "success", 200, Digest),
        };
        var observed = new[]
        {
            Observation("case-a", "success", 200),
            Observation("case-a", "success", 200),
        };

        var error = Assert.Throws<AcceptanceBlockedException>(() =>
            RouteEvidenceValidator.Validate(expected, observed));

        Assert.Equal("VPS09.RouteObservationDuplicate", error.Code);
    }

    [Fact]
    public void ResponseMismatchIsRefused()
    {
        var expected = new[] { new ExpectedRouteCase("case-a", "success", 200, Digest) };
        var observed = new[] { Observation("case-a", "success", 500) };

        var error = Assert.Throws<AcceptanceBlockedException>(() =>
            RouteEvidenceValidator.Validate(expected, observed));

        Assert.Equal("VPS09.RouteObservationMismatch", error.Code);
    }

    [Fact]
    public void OracleEvidenceRequiresAllRegisteredScenariosAndEmptyDiffs()
    {
        var required = new[] { "stock-ledger", "cleanup" };
        var observations = new[]
        {
            Oracle("stock-ledger"),
            Oracle("cleanup"),
        };

        var receipt = OracleEvidenceValidator.Validate(required, observations);

        Assert.Equal(2, receipt.Executed);
        Assert.Equal(2, receipt.Passed);
    }

    [Fact]
    public void OracleDiffIsRefused()
    {
        var observation = Oracle("stock-ledger") with { Diff = "quantity mismatch" };

        var error = Assert.Throws<AcceptanceBlockedException>(() =>
            OracleEvidenceValidator.Validate(new[] { "stock-ledger" }, new[] { observation }));

        Assert.Equal("VPS09.SqlOracleDiff", error.Code);
    }

    [Fact]
    public void SummaryRefusesIncompleteCleanupAndNeverApprovesRelease()
    {
        var route = new EvidenceReceipt(293, 293, 0, 0);
        var oracle = new EvidenceReceipt(12, 12, 0, 0);

        var error = Assert.Throws<AcceptanceBlockedException>(() =>
            SummaryBuilder.Build(Sha, route, oracle, cleanupConfirmed: false));
        Assert.Equal("VPS09.CleanupNotConfirmed", error.Code);

        var summary = SummaryBuilder.Build(Sha, route, oracle, cleanupConfirmed: true);
        Assert.Equal("NOT_APPROVED_BY_TOOL", summary.OverallRelease);
    }

    [Fact]
    public void SummaryRefusesDuplicateRouteRowsEvenWhenCountIs293()
    {
        var routes = Enumerable.Repeat(Observation("case-a", "success", 200), 293).ToArray();
        var required = Enumerable.Range(1, 12).Select(index => "oracle-" + index).ToArray();
        var oracles = required.Select(Oracle).ToArray();

        var error = Assert.Throws<AcceptanceBlockedException>(() =>
            SummaryBuilder.Build(Sha, routes, oracles, required, cleanupConfirmed: true));

        Assert.Equal("VPS09.RouteObservationDuplicate", error.Code);
    }

    private static PreflightInput ValidPreflight() => new(
        Sha,
        Sha,
        SourceTreeClean: true,
        Origin: "https://staging.example",
        SqlOracleConnectionString: "Server=sql.example;Initial Catalog=Vps09_Isolated;Encrypt=True;TrustServerCertificate=False;User ID=oracle;Password=hidden",
        Admission: "reviewed-isolated-sql13-only",
        Operator: "operator01",
        Reviewer: "reviewer02",
        Owner: "reviewer02",
        DatabaseClassification: "isolated-nonproduction");

    private static RouteObservation Observation(string caseId, string variant, int status) => new(
        caseId, variant, status, Digest, "protected-real-sql13", "runner01", "route-evidence.json",
        Digest, string.Empty);

    private static OracleObservation Oracle(string scenario) => new(
        scenario, Digest, Digest, Digest, string.Empty, "runner01", scenario + ".json");
}
