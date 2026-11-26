using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MyDmsVn.Contracts;
using MyDmsVn.Server.Application.Operations;
using MyDmsVn.Server.HttpHost;
using Xunit;
using LiveFixture = MyDmsVn.Server.AspNetCoreHost.Tests.Vps05cDesktopRouteLiveParityTests.DesktopLiveFixture;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps10OperationRecoveryTests
{
    private const string Key = "11111111-1111-4111-8111-111111111111";
    private const string Epoch = "22222222-2222-4222-8222-222222222222";
    private const string Fingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData("/api/v1/operations/context")]
    [InlineData("/api/v1/operations/" + Key)]
    [InlineData("/api/v2/operations/context")]
    [InlineData("/api/v2/operations/" + Key + "/resolution")]
    [InlineData("/api/v3/operations/resend-context")]
    [InlineData("/api/v3/operations/" + Key + "/resend-receipt")]
    [InlineData("/api/v3/operations/" + Key + "/resend-eligibility")]
    public async Task Disabled_features_precede_invalid_payload(string path)
    {
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("GET", path + "?unexpected=1", "{}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/operations/context?unexpected=1", null)]
    [InlineData("/api/v1/operations/context", "{}")]
    [InlineData("/api/v1/operations/11111111-1111-1111-8111-111111111111", null)]
    [InlineData("/api/v1/operations/" + Key + "/", null)]
    [InlineData("/api/v2/operations/" + Key + "/resolution?x=1", null)]
    public async Task Enabled_v1_v2_reject_noncanonical_requests(string path, string? body)
    {
        await using var fixture = await LiveFixture.StartAsync(web45Enabled: true);
        using var response = await fixture.SendAsync("GET", path, body, operationEpoch: Epoch, operationFingerprint: Fingerprint);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ValidationError", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("web2-v1/sales-create-response.json", WebOperationKind.SalesCreate)]
    [InlineData("web2-v1/sales-update-response.json", WebOperationKind.SalesUpdate)]
    [InlineData("web2-v1/sales-post-response.json", WebOperationKind.SalesPost)]
    [InlineData("web4-v1/receipt-create-response.json", WebOperationKind.ReceiptCreate)]
    [InlineData("web4-v1/receipt-update-response.json", WebOperationKind.ReceiptUpdate)]
    [InlineData("web4-v1/receipt-post-response.json", WebOperationKind.ReceiptPost)]
    public void Native_codec_reads_legacy_and_encodes_identical_projection(string path, WebOperationKind kind)
    {
        using var historical = Vps10CanonicalOperationTests.Read(path);
        var bytes = Encoding.UTF8.GetBytes(historical.RootElement.GetRawText());
        var legacy = new WebOperationResultCodec();
        var native = new V1OperationResultCodec();
        if (kind == WebOperationKind.SalesPost)
        {
            var value = legacy.Decode<PostSalesReceiptResponse>(kind, bytes);
            Assert.Equal(legacy.Encode(kind, value), native.Encode(kind, native.Decode<PostSalesReceiptResponse>(kind, bytes)));
        }
        else if (kind == WebOperationKind.ReceiptPost)
        {
            var value = legacy.Decode<PostGoodsReceiptResponse>(kind, bytes);
            Assert.Equal(legacy.Encode(kind, value), native.Encode(kind, native.Decode<PostGoodsReceiptResponse>(kind, bytes)));
        }
        else if (kind is WebOperationKind.ReceiptCreate or WebOperationKind.ReceiptUpdate)
        {
            var value = legacy.Decode<GoodsReceiptDto>(kind, bytes);
            Assert.Equal(legacy.Encode(kind, value), native.Encode(kind, native.Decode<GoodsReceiptDto>(kind, bytes)));
        }
        else
        {
            var value = legacy.Decode<SalesReceiptDto>(kind, bytes);
            Assert.Equal(legacy.Encode(kind, value), native.Encode(kind, native.Decode<SalesReceiptDto>(kind, bytes)));
        }
        Assert.Equal(historical.RootElement.GetRawText(), native.DecodeEnvelope(kind, bytes).GetRawText());
    }

    [Theory]
    [InlineData("/api/v1/operations/aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee", "permission", 403)]
    [InlineData("/api/v1/operations/aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee", "not-observed", 404)]
    [InlineData("/api/v1/operations/aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee", "epoch-mismatch", 409)]
    [InlineData("/api/v1/operations/aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee", "fingerprint-mismatch", 409)]
    [InlineData("/api/v2/operations/aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee/resolution", "not-observed", 404)]
    [InlineData("/api/v2/operations/aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee/resolution", "fingerprint-mismatch", 409)]
    public async Task Repository_refusal_is_not_a_replay_or_resend_authorization(string path, string variant, int status)
    {
        await using var fixture = await LiveFixture.StartAsync(web45Enabled: true, operationVariant: variant);
        using var response = await fixture.SendOperationAsync(path, status: true, authenticated: true);
        Assert.Equal(status, (int)response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.TryGetProperty("data", out _));
    }

    [Theory]
    [InlineData("/api/v1/operations/aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee")]
    [InlineData("/api/v2/operations/aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee/resolution")]
    public async Task Replay_discloses_original_outcome_without_current_business_read_permission(string path)
    {
        await using var fixture = await LiveFixture.StartAsync(web45Enabled: true, denyPermissions: true);
        using var response = await fixture.SendOperationAsync(path, status: true, authenticated: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal("COMMITTED", data.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("requestTargetId").ValueKind);
        using var original = Vps10CanonicalOperationTests.Read("web2-v1/sales-create-response.json");
        var legacy = new WebOperationResultCodec();
        var historical = legacy.Decode<SalesReceiptDto>(WebOperationKind.SalesCreate,
            Encoding.UTF8.GetBytes(original.RootElement.GetRawText()));
        Assert.Equal(Encoding.UTF8.GetString(legacy.Encode(WebOperationKind.SalesCreate, historical)),
            data.GetProperty("outcome").GetRawText());
    }

    [Theory]
    [InlineData(false, "result-expired", "RESULT_EXPIRED")]
    [InlineData(true, "result-expired", "RESULT_EXPIRED")]
    [InlineData(false, "result-unavailable", "RESULT_UNAVAILABLE")]
    [InlineData(true, "result-unavailable", "RESULT_UNAVAILABLE")]
    [InlineData(false, "result-corrupt", "RESULT_UNAVAILABLE")]
    [InlineData(true, "result-corrupt", "RESULT_UNAVAILABLE")]
    [InlineData(false, "result-id-mismatch", "RESULT_UNAVAILABLE")]
    [InlineData(true, "result-id-mismatch", "RESULT_UNAVAILABLE")]
    [InlineData(true, "not-executed", "NOT_EXECUTED")]
    [InlineData(true, "indeterminate", "INDETERMINATE")]
    public async Task Nonreplayable_states_disclose_no_outcome(bool resolution, string variant, string state)
    {
        const string operation = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";
        var path = resolution ? "/api/v2/operations/" + operation + "/resolution" : "/api/v1/operations/" + operation;
        await using var fixture = await LiveFixture.StartAsync(web45Enabled: true, operationVariant: variant);
        using var response = await fixture.SendOperationAsync(path, status: true, authenticated: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(state, data.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("outcome").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("requestTargetId").ValueKind);
        if (resolution)
        {
            var terminal = data.GetProperty("terminal");
            if (state == "NOT_EXECUTED")
            {
                Assert.Equal("11111111-2222-4333-8444-555555555555", terminal.GetProperty("decisionId").GetString());
                Assert.Equal("CASE_01", terminal.GetProperty("caseId").GetString());
                Assert.Equal("2026-10-31T09:00:00.0000000Z", terminal.GetProperty("decidedAtUtc").GetString());
            }
            else Assert.Equal(JsonValueKind.Null, terminal.ValueKind);
        }
        else Assert.False(data.TryGetProperty("terminal", out _));
    }

    [Theory]
    [InlineData(false, "not-executed")]
    [InlineData(false, "indeterminate")]
    [InlineData(true, "terminal-missing")]
    [InlineData(true, "terminal-invalid")]
    public async Task Invalid_terminal_state_for_contract_fails_closed(bool resolution, string variant)
    {
        const string operation = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";
        var path = resolution ? "/api/v2/operations/" + operation + "/resolution" : "/api/v1/operations/" + operation;
        await using var fixture = await LiveFixture.StartAsync(web45Enabled: true, operationVariant: variant);
        using var response = await fixture.SendOperationAsync(path, status: true, authenticated: true);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Operation.Unavailable", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.False(json.RootElement.TryGetProperty("data", out _));
    }

    [Theory]
    [InlineData("COMMITTED", "COMMITTED_RESULT_AVAILABLE")]
    [InlineData("RESULT_UNAVAILABLE", "COMMITTED_RESULT_UNAVAILABLE")]
    [InlineData("NOT_EXECUTED", "NOT_EXECUTED")]
    public async Task Web6_valid_receipt_does_not_override_known_outcome(string state, string reason)
    {
        await using var fixture = await LiveFixture.StartAsync(web6Enabled: true);
        using var issued = await fixture.SendWeb6Async("VPS01-RESEND-RECEIPT");
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        using var issuedJson = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        var receipt = issuedJson.RootElement.GetProperty("data").GetProperty("receipt").GetString();
        Assert.False(string.IsNullOrWhiteSpace(receipt));
        fixture.SetWeb6Resolution(state);
        using var response = await fixture.SendWeb6Async("VPS01-RESEND-ELIGIBILITY", receipt);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal("DENY", data.GetProperty("decision").GetString());
        Assert.Equal(reason, data.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{},\"data\":{}}")]
    [InlineData("{\"data\":{\"receiptId\":\"1\",\"receiptId\":\"2\",\"status\":\"Posted\",\"postedAtUtc\":\"2026-11-26T00:00:00Z\"}}")]
    [InlineData("{\"data\":{\"receiptId\":1,\"status\":\"Posted\",\"postedAtUtc\":\"2026-11-26T00:00:00Z\"}}")]
    [InlineData("{\"data\":{\"receiptId\":\"01\",\"status\":\"Posted\",\"postedAtUtc\":\"2026-11-26T00:00:00Z\"}}")]
    [InlineData("{\"data\":{\"receiptId\":\"1\",\"status\":\"Posted\",\"postedAtUtc\":\"2026-11-26T00:00:00+07:00\"}}")]
    [InlineData("{\"data\":{\"receiptId\":\"1\",\"status\":\"Posted\",\"postedAtUtc\":\"2026-11-26T00:00:00Z\",\"extra\":false}}")]
    public void Corrupt_stored_result_is_not_a_success(string json)
    {
        var error = Assert.Throws<WebOperationException>(() =>
            new V1OperationResultCodec().DecodeEnvelope(WebOperationKind.ReceiptPost, Encoding.UTF8.GetBytes(json)));
        Assert.Equal("Operation.ResultUnavailable", error.Code);
    }

    [Fact]
    public void Oversized_stored_result_is_refused()
    {
        var error = Assert.Throws<WebOperationException>(() => new V1OperationResultCodec()
            .DecodeEnvelope(WebOperationKind.ReceiptPost, new byte[V1OperationResultCodec.MaximumResultBytes + 1]));
        Assert.Equal("Operation.ResultUnavailable", error.Code);
    }
}
