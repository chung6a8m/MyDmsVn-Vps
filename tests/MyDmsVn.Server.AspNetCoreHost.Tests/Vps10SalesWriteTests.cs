using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MyDmsVn.Server.Application.Operations;
using MyDmsVn.Server.Application.Sales;
using Xunit;
using LiveFixture = MyDmsVn.Server.AspNetCoreHost.Tests.Vps05cDesktopRouteLiveParityTests.DesktopLiveFixture;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps10SalesWriteTests
{
    private const string Sales = "/api/v1/sales-receipts";
    private const string Post = Sales + "/9223372036854775807/post";
    private const string Update = Sales + "/9223372036854775807/draft";
    private const string Key = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";
    private const string Epoch = "11111111-2222-4333-8444-555555555555";
    private const string CreateBody = """{"saleDate":"2024-02-29","customerId":101,"warehouseId":102,"employeeId":103,"lines":[{"saleLineId":"0","productId":104,"qty":"1.2500","unitPrice":"2.0000","isGift":false}]}""";
    private const string PostBody = """{"expectedVersion":"AAAAAAAAAAI="}""";

    [Theory]
    [InlineData("vi-VN")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("")]
    public async Task Typed_create_values_in_Kestrel_are_culture_invariant(string culture)
    {
        await using var fixture = await LiveFixture.StartAsync(requestCultureName: culture, desktopApiEnabled: false);
        using var response = await fixture.SendAsync("POST", Sales, CreateBody);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var command = Assert.IsType<CreateSalesReceiptDraftCommand>(fixture.LastRequest);
        Assert.Equal(culture, fixture.LastRequestCulture);
        Assert.Equal(new DateTime(2024, 2, 29), command.Request.SaleDate);
        Assert.Equal(101, command.Request.CustomerId);
        var line = Assert.Single(command.Request.Lines);
        Assert.Equal(0L, line.SaleLineId);
        Assert.Equal(1.2500m, line.Qty);
        Assert.Equal(2.0000m, line.UnitPrice);
        Assert.False(line.IsGift);
        Assert.Null(Operation(command));
        Assert.Equal(1, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData("sales-create", "POST", Sales, null)]
    [InlineData("sales-update", "PUT", Update, "9223372036854775807")]
    [InlineData("sales-post", "POST", Post, "9223372036854775807")]
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
    [InlineData("\"isGift\":false", "\"isGift\":null", "lines[0].isGift")]
    [InlineData(",\"isGift\":false", "", "lines[0].isGift")]
    [InlineData("\"saleLineId\":\"0\"", "\"saleLineId\":\"1\"", "lines[0].saleLineId")]
    [InlineData("\"saleLineId\":\"0\"", "\"saleLineId\":\"00\"", "lines[0].saleLineId")]
    [InlineData("\"productId\":104", "\"productId\":104.0", "lines[0].productId")]
    [InlineData("\"productId\":104", "\"productId\":\"104\"", "lines[0].productId")]
    [InlineData("\"qty\":\"1.2500\"", "\"qty\":\"1.25000\"", "lines[0].qty")]
    [InlineData("\"qty\":\"1.2500\"", "\"qty\":\"0.0000\"", "lines[0].qty")]
    [InlineData("\"qty\":\"1.2500\"", "\"qty\":\"1,25\"", "lines[0].qty")]
    [InlineData("\"unitPrice\":\"2.0000\"", "\"unitPrice\":\"-1\"", "lines[0].unitPrice")]
    [InlineData("\"saleDate\":\"2024-02-29\"", "\"saleDate\":\"0001-01-01\"", "saleDate")]
    [InlineData("\"employeeId\":103", "\"employeeId\":103,\"EmployeeId\":103", null)]
    [InlineData("\"productId\":104", "\"productId\":104,\"\\u0070roductId\":104", null)]
    public async Task Invalid_body_never_dispatches(string before, string after, string? field)
    {
        Assert.Contains(before, CreateBody);
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("POST", Sales, CreateBody.Replace(before, after, StringComparison.Ordinal));
        await Invalid(response, field);
        Assert.Equal(0, fixture.HandlerCalls);
        Assert.Empty(fixture.PermissionChecks);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public async Task Line_cardinality_is_checked_before_dispatch(int count, bool accepted)
    {
        using var source = JsonDocument.Parse(CreateBody);
        var line = source.RootElement.GetProperty("lines")[0].GetRawText();
        var body = CreateBody.Replace("[" + line + "]", "[" + string.Join(",", Enumerable.Repeat(line, count)) + "]", StringComparison.Ordinal);
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("POST", Sales, body);
        if (accepted)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(count, Assert.IsType<CreateSalesReceiptDraftCommand>(fixture.LastRequest).Request.Lines.Count);
        }
        else await Invalid(response, "lines");
        Assert.Equal(accepted ? 1 : 0, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData("PUT", "/0/draft?unknown=1", "saleId")]
    [InlineData("POST", "/bad/post?unknown=1", "saleId")]
    [InlineData("POST", "/1/post?unknown=1", null)]
    [InlineData("POST", "?unknown=1", null)]
    public async Task Route_id_then_query_then_body_precedence(string method, string suffix, string? field)
    {
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync(method, Sales + suffix, "not-json");
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
        using var response = await fixture.SendRawAsync("POST", Sales, Encoding.UTF8.GetBytes(CreateBody), media);
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
        using var response = await fixture.SendAsync("POST", Sales, CreateBody,
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
        using var response = await fixture.SendAsync("POST", Sales, CreateBody,
            operationKey: key, operationEpoch: epoch, duplicateOperationKey: duplicate);
        await Invalid(response, null);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Fact]
    public async Task Enabled_WEB4_rejects_trailing_slash_alias()
    {
        await using var fixture = await LiveFixture.StartAsync(web45Enabled: true);
        using var response = await fixture.SendAsync("POST", Sales + "/", CreateBody, operationKey: Key, operationEpoch: Epoch);
        await Invalid(response, null);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Fact]
    public async Task Sales_post_preserves_legacy_base64_whitespace()
    {
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("POST", Post, PostBody.Replace("AAAAAAAAAAI=", "AAAAAAAA AAI=", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, fixture.HandlerCalls);
    }

    [Fact]
    public async Task Authentication_precedes_native_mutation_binding()
    {
        await using var fixture = await LiveFixture.StartAsync(web45Enabled: true);
        using var response = await fixture.SendAsync("POST", Sales, "not-json", authenticated: false);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Fact]
    public async Task Permission_pipeline_still_denies_valid_native_mutation()
    {
        await using var fixture = await LiveFixture.StartAsync(denyPermissions: true);
        using var response = await fixture.SendAsync("POST", Sales, CreateBody);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEmpty(fixture.PermissionChecks);
        Assert.Equal(0, fixture.HandlerCalls);
    }

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
