using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MyDmsVn.Server.Application.Operations;
using MyDmsVn.Contracts;
using MyDmsVn.Server.Application.Inventory;
using MyDmsVn.Server.HttpHost;
using MyDmsVn.Server.HttpHost.Features.Receipts;
using Xunit;
using LiveFixture = MyDmsVn.Server.AspNetCoreHost.Tests.Vps05cDesktopRouteLiveParityTests.DesktopLiveFixture;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps10ReceiptWriteTests
{
    private const string Receipt = "/api/v1/goods-receipts";
    private const string Post = Receipt + "/9007199254740993/post";
    private const string Update = Receipt + "/9007199254740993/draft";
    private const string Key = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";
    private const string Epoch = "11111111-2222-4333-8444-555555555555";
    private const string CreateBody = """{"receiptDate":"2024-02-29","warehouseId":1,"employeeId":2,"lines":[{"productId":7,"quantity":"1.2500","unitCost":"0.0000"}]}""";
    private const string PostBody = """{"expectedVersion":"AAAAAAAAAAI="}""";

    [Theory]
    [InlineData("vi-VN")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("")]
    public async Task Typed_create_values_in_Kestrel_are_culture_invariant(string culture)
    {
        await using var fixture = await LiveFixture.StartAsync(requestCultureName: culture, desktopApiEnabled: false);
        using var response = await fixture.SendAsync("POST", Receipt, CreateBody);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var command = Assert.IsType<CreateGoodsReceiptDraftCommand>(fixture.LastRequest);
        Assert.Equal(culture, fixture.LastRequestCulture);
        Assert.Equal(new DateTime(2024, 2, 29), command.Request.ReceiptDate);
        Assert.Equal(1, command.Request.WarehouseId);
        Assert.Equal(2, command.Request.EmployeeId);
        Assert.Null(command.Request.Note);
        var line = Assert.Single(command.Request.Lines);
        Assert.Equal(7, line.ProductId);
        Assert.Equal(1.2500m, line.Quantity);
        Assert.Equal(0m, line.UnitCost);
        Assert.Null(Operation(command));
        Assert.Equal(1, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData("receipt-create", "POST", Receipt, null)]
    [InlineData("receipt-update", "PUT", Update, "9007199254740993")]
    [InlineData("receipt-post", "POST", Post, "9007199254740993")]
    public async Task Native_operation_uses_independent_fingerprint_and_admitted_session(
        string vectorName, string method, string path, string? target)
    {
        using var vectors = Vps10CanonicalOperationTests.Read("web4-v1/fingerprint-vectors.json");
        var vector = vectors.RootElement.GetProperty("vectors").EnumerateArray()
            .Single(x => x.GetProperty("name").GetString() == vectorName);
        var input = vector.GetProperty("intent").GetProperty("body").GetRawText();
        await using var fixture = await LiveFixture.StartAsync(web45Enabled: true);
        using var response = await fixture.SendAsync(method, path, input, operationKey: Key, operationEpoch: Epoch);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var context = Assert.IsType<WebOperationContext>(Operation(fixture.LastRequest!));
        Assert.Equal(Guid.Parse(Key), context.OperationId);
        Assert.Equal(Guid.Parse(Epoch), context.DeploymentEpoch);
        Assert.Equal(Guid.Parse("11111111-1111-4111-8111-111111111111"), context.SessionId);
        Assert.Equal(target == null ? (long?)null : long.Parse(target, CultureInfo.InvariantCulture), context.RequestTargetId);
        Assert.Equal(vector.GetProperty("sha256").GetString(), Convert.ToHexString(context.Fingerprint).ToLowerInvariant());
        Assert.Equal(1, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData("\"lines\":[{", "\"lines\":[null,{", "lines[0]")]
    [InlineData("\"productId\":7", "\"productId\":7.0", "lines[0].productId")]
    [InlineData("\"productId\":7", "\"productId\":\"7\"", "lines[0].productId")]
    [InlineData("\"quantity\":\"1.2500\"", "\"quantity\":\"1.25000\"", "lines[0].quantity")]
    [InlineData("\"quantity\":\"1.2500\"", "\"quantity\":\"0.0000\"", "lines[0].quantity")]
    [InlineData("\"quantity\":\"1.2500\"", "\"quantity\":\"1,25\"", "lines[0].quantity")]
    [InlineData("\"quantity\":\"1.2500\"", "\"quantity\":1.25", "lines[0].quantity")]
    [InlineData("\"quantity\":\"1.2500\"", "\"quantity\":null", "lines[0].quantity")]
    [InlineData(",\"quantity\":\"1.2500\"", "", "lines[0].quantity")]
    [InlineData("\"unitCost\":\"0.0000\"", "\"unitCost\":\"-1\"", "lines[0].unitCost")]
    [InlineData("\"unitCost\":\"0.0000\"", "\"unitCost\":\"0.00000\"", "lines[0].unitCost")]
    [InlineData("\"unitCost\":\"0.0000\"", "\"unitCost\":null", "lines[0].unitCost")]
    [InlineData("\"unitCost\":\"0.0000\"", "\"unitCost\":0", "lines[0].unitCost")]
    [InlineData(",\"unitCost\":\"0.0000\"", "", "lines[0].unitCost")]
    [InlineData("\"receiptDate\":\"2024-02-29\"", "\"receiptDate\":\"0001-01-01\"", "receiptDate")]
    [InlineData("\"employeeId\":2", "\"employeeId\":2,\"EmployeeId\":2", null)]
    [InlineData("\"productId\":7", "\"productId\":7,\"\\u0070roductId\":7", null)]
    [InlineData("\"employeeId\":2", "\"employeeId\":2,\"note\":42", "note")]
    public async Task Invalid_body_never_dispatches(string before, string after, string? field)
    {
        Assert.Contains(before, CreateBody);
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("POST", Receipt, CreateBody.Replace(before, after, StringComparison.Ordinal));
        await Invalid(response, field);
        Assert.Equal(0, fixture.HandlerCalls);
        Assert.Empty(fixture.PermissionChecks);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, true)]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public async Task Line_cardinality_is_checked_before_dispatch(int count, bool accepted)
    {
        using var source = JsonDocument.Parse(CreateBody);
        var line = source.RootElement.GetProperty("lines")[0].GetRawText();
        // Use distinct product IDs: Application rejects duplicate products independently of wire cardinality.
        var lines = Enumerable.Range(7, count).Select(id => line.Replace("\"productId\":7",
            "\"productId\":" + id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        var body = CreateBody.Replace("[" + line + "]", "[" + string.Join(",", lines) + "]", StringComparison.Ordinal);
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("POST", Receipt, body);
        if (accepted)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(count, Assert.IsType<CreateGoodsReceiptDraftCommand>(fixture.LastRequest).Request.Lines.Count);
        }
        else await Invalid(response, "lines");
        Assert.Equal(accepted ? 1 : 0, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData("POST", Receipt)]
    [InlineData("PUT", Update)]
    public async Task Duplicate_product_preserves_application_validation(string method, string path)
    {
        using var source = JsonDocument.Parse(CreateBody);
        var line = source.RootElement.GetProperty("lines")[0].GetRawText();
        var body = CreateBody.Replace("[" + line + "]", "[" + line + "," + line + "]", StringComparison.Ordinal);
        if (method == "PUT") body = body[..^1] + ",\"expectedVersion\":\"AAAAAAAAAAI=\"}";
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync(method, path, body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("ValidationError", error.GetProperty("code").GetString());
        var detail = Assert.Single(error.GetProperty("details").EnumerateArray());
        Assert.Equal("GoodsReceipt.DuplicateProduct", detail.GetProperty("code").GetString());
        Assert.Equal("lines[1].productId", detail.GetProperty("field").GetString());
        Assert.NotEmpty(fixture.PermissionChecks);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData("PUT", "/0/draft?unknown=1", "receiptId")]
    [InlineData("POST", "/bad/post?unknown=1", "receiptId")]
    [InlineData("POST", "/1/post?unknown=1", null)]
    [InlineData("POST", "?unknown=1", null)]
    public async Task Route_id_then_query_then_body_precedence(string method, string suffix, string? field)
    {
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync(method, Receipt + suffix, "not-json");
        await Invalid(response, field);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/problem+json")]
    [InlineData("application/json; charset=utf-16")]
    public async Task Invalid_content_type_is_legacy_400_not_framework_415(string media)
    {
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendRawAsync("POST", Receipt, Encoding.UTF8.GetBytes(CreateBody), media);
        await Invalid(response, null);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData(Key, null, null)]
    [InlineData(null, Epoch, null)]
    [InlineData(null, null, "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f")]
    public async Task Disabled_WEB4_rejects_operation_headers(string? key, string? epoch, string? fingerprint)
    {
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("POST", Receipt, CreateBody,
            operationKey: key, operationEpoch: epoch, operationFingerprint: fingerprint);
        await Invalid(response, null);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData(null, Epoch, false)]
    [InlineData(Key, null, false)]
    [InlineData("AAAAAAAA-BBBB-4CCC-8DDD-EEEEEEEEEEEE", Epoch, false)]
    [InlineData("aaaaaaaa-bbbb-1ccc-8ddd-eeeeeeeeeeee", Epoch, false)]
    [InlineData(Key, Epoch, true)]
    public async Task Enabled_WEB4_rejects_missing_or_noncanonical_identity(string? key, string? epoch, bool duplicate)
    {
        await using var fixture = await LiveFixture.StartAsync(web45Enabled: true);
        using var response = await fixture.SendAsync("POST", Receipt, CreateBody,
            operationKey: key, operationEpoch: epoch, duplicateOperationKey: duplicate);
        await Invalid(response, null);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Fact]
    public async Task Enabled_WEB4_rejects_trailing_slash_alias()
    {
        await using var fixture = await LiveFixture.StartAsync(web45Enabled: true);
        using var response = await fixture.SendAsync("POST", Receipt + "/", CreateBody, operationKey: Key, operationEpoch: Epoch);
        await Invalid(response, null);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData("AAAAAAAA AAI=", false)]
    [InlineData("AAAAAAAAAAJ=", false)]
    [InlineData("AAAAAAAAAAI", false)]
    [InlineData("AAAAAAAAAAA=", true)]
    [InlineData("AAAAAAAAAAI=", true)]
    public async Task Receipt_post_requires_canonical_eight_byte_version(string version, bool accepted)
    {
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("POST", Post, PostBody.Replace("AAAAAAAAAAI=", version, StringComparison.Ordinal));
        if (accepted) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        else await Invalid(response, "expectedVersion");
        Assert.Equal(accepted ? 1 : 0, fixture.HandlerCalls);
    }

    [Fact]
    public async Task Authentication_precedes_native_mutation_binding()
    {
        await using var fixture = await LiveFixture.StartAsync(web45Enabled: true);
        using var response = await fixture.SendAsync("POST", Receipt, "not-json", authenticated: false);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Fact]
    public async Task Permission_pipeline_still_denies_valid_native_mutation()
    {
        await using var fixture = await LiveFixture.StartAsync(denyPermissions: true);
        using var response = await fixture.SendAsync("POST", Receipt, CreateBody);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEmpty(fixture.PermissionChecks);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData("vi-VN")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("")]
    public void Every_frozen_Receipt_vector_preserves_exact_canonical_UTF8_and_hash(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            using var corpus = Vps10CanonicalOperationTests.Read("web4-v1/fingerprint-vectors.json");
            var vectors = corpus.RootElement.GetProperty("vectors").EnumerateArray()
                .Where(v => v.GetProperty("intent").GetProperty("operationKind").GetString()!.StartsWith("Receipt.", StringComparison.Ordinal)).ToArray();
            Assert.Equal(4, vectors.Length);
            foreach (var vector in vectors)
            {
                var intent = vector.GetProperty("intent");
                var kind = ReceiptKind(intent.GetProperty("operationKind").GetString()!);
                var target = intent.GetProperty("requestTargetId");
                long? id = target.ValueKind == JsonValueKind.Null ? null : long.Parse(target.GetString()!, CultureInfo.InvariantCulture);
                var source = intent.GetProperty("body");
                var before = source.GetRawText();
                _ = ReceiptRequestReader.Read(kind switch
                {
                    WebOperationKind.ReceiptCreate => typeof(ReceiptCreateRequest),
                    WebOperationKind.ReceiptUpdate => typeof(ReceiptUpdateRequest),
                    _ => typeof(ReceiptPostRequest)
                }, source);
                var canonical = V1OperationCanonicalizer.Canonicalize(kind, id, source);
                Assert.Equal(before, source.GetRawText());
                Assert.Equal(vector.GetProperty("canonical").GetString(), canonical);
                Assert.Equal(vector.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant());
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Theory]
    [InlineData("create", "POST", "/api/v1/goods-receipts", WebOperationKind.ReceiptCreate)]
    [InlineData("update", "PUT", "/api/v1/goods-receipts/9007199254740993/draft", WebOperationKind.ReceiptUpdate)]
    [InlineData("post", "POST", "/api/v1/goods-receipts/9007199254740993/post", WebOperationKind.ReceiptPost)]
    public async Task Frozen_pre_upgrade_outcome_decodes_and_native_live_response_equals_retained_codec(
        string action, string method, string path, WebOperationKind kind)
    {
        using var legacy = Vps10CanonicalOperationTests.Read("web3-v1/receipt-" + action + "-response.json");
        var bytes = Encoding.UTF8.GetBytes(legacy.RootElement.GetRawText());
        var codec = new WebOperationResultCodec();
        var options = new JsonSerializerOptions();
        V1HttpJson.Configure(options);
        byte[] retained;
        byte[] projected;
        if (kind == WebOperationKind.ReceiptPost)
        {
            var value = codec.Decode<PostGoodsReceiptResponse>(kind, bytes);
            retained = codec.Encode(kind, value);
            projected = JsonSerializer.SerializeToUtf8Bytes(new V1SuccessEnvelope<ReceiptPostResponse>(ReceiptHttpMapper.Post(value)), options);
        }
        else
        {
            var value = codec.Decode<GoodsReceiptDto>(kind, bytes);
            retained = codec.Encode(kind, value);
            projected = JsonSerializer.SerializeToUtf8Bytes(new V1SuccessEnvelope<ReceiptDetailResponse>(ReceiptHttpMapper.Detail(value)), options);
        }
        Assert.Equal(retained, projected);

        using var vectors = Vps10CanonicalOperationTests.Read("web4-v1/fingerprint-vectors.json");
        var vector = vectors.RootElement.GetProperty("vectors").EnumerateArray()
            .Single(x => x.GetProperty("name").GetString() == "receipt-" + action);
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync(method, path, vector.GetProperty("intent").GetProperty("body").GetRawText());
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal(retained, await response.Content.ReadAsByteArrayAsync());
    }

    private static WebOperationKind ReceiptKind(string text) => text switch
    {
        "Receipt.Create" => WebOperationKind.ReceiptCreate,
        "Receipt.Update" => WebOperationKind.ReceiptUpdate,
        "Receipt.Post" => WebOperationKind.ReceiptPost,
        _ => throw new InvalidOperationException("Unexpected frozen Receipt vector.")
    };

    private static object? Operation(object request)
    {
        var property = Assert.Single(request.GetType().GetProperties(), p => p.PropertyType == typeof(WebOperationContext));
        return property.GetValue(request);
    }

    private static async Task Invalid(System.Net.Http.HttpResponseMessage response, string? field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("ValidationError", error.GetProperty("code").GetString());
        var detail = Assert.Single(error.GetProperty("details").EnumerateArray());
        Assert.Equal("Validation.InvalidValue", detail.GetProperty("code").GetString());
        Assert.Equal(field, detail.TryGetProperty("field", out var value) ? value.GetString() : null);
    }
}
