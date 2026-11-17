using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MyDmsVn.Contracts;
using MyDmsVn.Server.Application.Operations;
using MyDmsVn.Server.HttpHost;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

/// <summary>
/// Transitional VPS-05A differential checks: old Json.NET is an independent
/// writer oracle while the net10 HTTP and journal egress uses System.Text.Json.
/// The frozen WEB4 fingerprint tests remain separate and unmodified.
/// </summary>
public sealed class Vps05JsonEgressParityTests
{
    private static string Legacy<T>(ApiResponse<T> value) =>
        JsonConvert.SerializeObject(value, ApiJson.CreateSerializerSettings());

    [Fact]
    public async Task Success_envelope_keeps_frozen_int64_and_decimal_string_lexemes()
    {
        var data = new { receiptId = "9007199254740993", quantity = "1.2500", missing = (string?)null };
        var result = ApiResponse<object>.Success(data);
        var expected = "{\"data\":{\"receiptId\":\"9007199254740993\",\"quantity\":\"1.2500\"}}";
        Assert.Equal(expected, Legacy(result));
        Assert.Equal(expected, Net10WireJson.SerializeEnvelope(result));
        using var http = HttpJson.From(result);
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        Assert.Equal(expected, await http.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Error_envelope_uses_actual_HTTP_status_and_never_serializes_Status()
    {
        var error = new ApiError(ApiStatusCode.Unauthorized, "Auth.Unauthorized",
            "Authentication is required.", correlationId: "vps01-correlation");
        var result = ApiResponse<object>.Failure(error);
        var expected = "{\"error\":{\"code\":\"Auth.Unauthorized\",\"message\":\"Authentication is required.\",\"details\":[],\"correlationId\":\"vps01-correlation\"}}";
        Assert.Equal(expected, Legacy(result));
        Assert.Equal(expected, Net10WireJson.SerializeEnvelope(result));
        using var http = HttpJson.From(result);
        Assert.Equal(HttpStatusCode.Unauthorized, http.StatusCode);
        Assert.Equal(expected, await http.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Error_detail_omits_optional_field_but_retains_array_shape()
    {
        var error = new ApiError(ApiStatusCode.BadRequest, "ValidationError", "Invalid input.",
            new[] { new ApiErrorDetail("Missing", "A required field is missing.") });
        var response = ApiResponse<object>.Failure(error);
        Assert.Equal(Legacy(response), Net10WireJson.SerializeEnvelope(response));
        using var document = JsonDocument.Parse(Net10WireJson.SerializeEnvelope(response));
        var detail = document.RootElement.GetProperty("error").GetProperty("details")[0];
        Assert.False(detail.TryGetProperty("field", out _));
        Assert.False(document.RootElement.GetProperty("error").TryGetProperty("correlationId", out _));
    }

    [Fact]
    public void Decimal_scale_and_UTC_date_are_culture_independent()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
            var result = ApiResponse<object>.Success(new
            {
                amount = 19.9900m,
                quantity = 2.5000m,
                postedAtUtc = new DateTime(2026, 11, 17, 13, 14, 15, DateTimeKind.Utc)
            });
            var json = Net10WireJson.SerializeEnvelope(result);
            Assert.Equal(Legacy(result), json);
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");
            Assert.Equal("19.9900", data.GetProperty("amount").GetRawText());
            Assert.Equal("2.5000", data.GetProperty("quantity").GetRawText());
            Assert.Equal("2026-11-17T13:14:15Z", data.GetProperty("postedAtUtc").GetString());
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void Legacy_DOM_projection_preserves_explicit_null_and_unmodified_member_order()
    {
        var data = new JObject
        {
            ["receiptId"] = "9007199254740993",
            ["note"] = JValue.CreateNull(),
            ["quantity"] = "1.2500",
            ["warehouseId"] = 3
        };
        var response = ApiResponse<object>.Success(data);
        var json = Net10WireJson.SerializeEnvelope(response);
        Assert.Equal(Legacy(response), json);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("data").GetProperty("note").ValueKind);
        Assert.Equal(new[] { "receiptId", "note", "quantity", "warehouseId" },
            doc.RootElement.GetProperty("data").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void Journal_result_snapshot_and_live_success_use_the_same_server_writer()
    {
        var receipt = new GoodsReceiptDto(
            9007199254740993L, "GR0000000001", new DateTime(2026, 11, 17), 1, 2,
            "Draft", null, null, Convert.ToBase64String(new byte[8]),
            new[] { new GoodsReceiptLineDto(9007199254740993L, 1, 7, 1.2500m, 19.9900m) });
        var body = InventoryWireOutput.Detail(receipt);
        var result = ApiResponse<object>.Success(body);
        var old = Legacy(result);
        var current = Net10WireJson.SerializeEnvelope(result);
        Assert.Equal(old, current);
        var journal = new WebOperationResultCodec().Encode(WebOperationKind.ReceiptCreate, receipt);
        Assert.Equal(current, Encoding.UTF8.GetString(journal));
        var decoded = new WebOperationResultCodec().DecodeEnvelope(WebOperationKind.ReceiptCreate, journal);
        Assert.Equal("9007199254740993", (string)decoded["data"]!["receiptId"]!);
    }
}
