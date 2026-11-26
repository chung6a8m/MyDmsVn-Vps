using System;
using System.IO;
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
    public void Native_codec_reads_legacy_and_encodes_identical_projection(string path, WebOperationKind kind)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "docs", "contracts")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var bytes = File.ReadAllBytes(Path.Combine(directory!.FullName, "docs", "contracts", path));
        var legacy = new WebOperationResultCodec();
        var native = new V1OperationResultCodec();
        if (kind == WebOperationKind.SalesPost)
        {
            var value = legacy.Decode<PostSalesReceiptResponse>(kind, bytes);
            Assert.Equal(legacy.Encode(kind, value), native.Encode(kind, native.Decode<PostSalesReceiptResponse>(kind, bytes)));
        }
        else
        {
            var value = legacy.Decode<SalesReceiptDto>(kind, bytes);
            Assert.Equal(legacy.Encode(kind, value), native.Encode(kind, native.Decode<SalesReceiptDto>(kind, bytes)));
        }
        Assert.Equal(JsonDocument.Parse(bytes).RootElement.GetRawText(), native.DecodeEnvelope(kind, bytes).GetRawText());
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
