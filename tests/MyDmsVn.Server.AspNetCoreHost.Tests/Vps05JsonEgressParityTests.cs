using System;
using System.Collections.Generic;
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
using Newtonsoft.Json.Serialization;
using System.Reflection;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

/// <summary>
/// Transitional VPS-05A differential checks: old Json.NET is an independent
/// writer oracle while the net10 HTTP and journal egress uses System.Text.Json.
/// The frozen WEB4 fingerprint tests remain separate and unmodified.
/// </summary>
public sealed class Vps05JsonEgressParityTests
{
    // Independent Json.NET oracle, now owned by the *test* project. The
    // production net10 Contracts assembly cannot depend on Newtonsoft.
    private static readonly JsonSerializerSettings LegacySettings = new()
    {
        ContractResolver = new LegacyContractResolver(),
        Culture = CultureInfo.InvariantCulture,
        DateFormatHandling = DateFormatHandling.IsoDateFormat,
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Ignore
    };

    private sealed class LegacyContractResolver : CamelCasePropertyNamesContractResolver
    {
        protected override Newtonsoft.Json.Serialization.JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);
            if ((property.PropertyType == typeof(DateTime) || property.PropertyType == typeof(DateTime?)) &&
                member.Name is "SaleDate" or "ReceiptDate" or "FromDate" or "ToDate")
                property.Converter = new LegacyCalendarDateConverter();
            return property;
        }
    }

    private sealed class LegacyCalendarDateConverter : Newtonsoft.Json.JsonConverter
    {
        public override bool CanConvert(Type type) =>
            type == typeof(DateTime) || type == typeof(DateTime?);

        public override void WriteJson(JsonWriter writer, object? value, Newtonsoft.Json.JsonSerializer serializer)
        {
            if (value is DateTime date)
                writer.WriteValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            else
                writer.WriteNull();
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue,
            Newtonsoft.Json.JsonSerializer serializer) => throw new NotSupportedException();
    }

    private static string Legacy<T>(ApiResponse<T> value)
    {
        if (value.IsSuccess)
            return "{\"data\":" + JsonConvert.SerializeObject(value.Data, LegacySettings) + "}";
        var error = value.Error!;
        return "{\"error\":" + JsonConvert.SerializeObject(new
        {
            error.Code, error.Message, error.Details, error.CorrelationId
        }, LegacySettings) + "}";
    }

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
        var native = WireObject.Parse(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data, LegacySettings)));
        var json = Net10WireJson.SerializeEnvelope(ApiResponse<object>.Success(native));
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
        var old = Legacy(ApiResponse<object>.Success(JObject.Parse(body.Node!.ToJsonString())));
        var current = Net10WireJson.SerializeEnvelope(result);
        Assert.Equal(old, current);
        var journal = new WebOperationResultCodec().Encode(WebOperationKind.ReceiptCreate, receipt);
        Assert.Equal(current, Encoding.UTF8.GetString(journal));
        var decoded = new WebOperationResultCodec().DecodeEnvelope(WebOperationKind.ReceiptCreate, journal);
        Assert.Equal("9007199254740993", (string)decoded["data"]!["receiptId"]!);
    }

    [Fact]
    public void Typed_dictionary_keys_follow_legacy_camel_case_naming()
    {
        var response = ApiResponse<object>.Success(new Dictionary<string, object>
        {
            ["CanRead"] = true,
            ["UserRole"] = "Operator",
        });
        var expected = Legacy(response);
        Assert.Contains("\"canRead\":true", expected);
        Assert.Contains("\"userRole\":\"Operator\"", expected);
        Assert.Equal(expected, Net10WireJson.SerializeEnvelope(response));
    }

    [Fact]
    public void Unspecified_SQL_datetime_and_Local_datetime_use_legacy_UTC_wire_policy()
    {
        var response = ApiResponse<object>.Success(new
        {
            postedAtUtc = new DateTime(2026, 11, 17, 13, 14, 15, DateTimeKind.Unspecified),
            localTime = new DateTime(2026, 11, 17, 13, 14, 15, DateTimeKind.Local),
        });
        var expected = Legacy(response);
        Assert.Contains("\"postedAtUtc\":\"2026-11-17T13:14:15Z\"", expected);
        Assert.Equal(expected, Net10WireJson.SerializeEnvelope(response));
    }

    [Fact]
    public void Legacy_calendar_date_attributes_are_preserved_on_typed_DTOs()
    {
        var date = new DateTime(2026, 11, 17);
        var sale = new SaveSalesReceiptRequest(date, 1, 2, 3, null,
            new[] { new SaveSalesReceiptLineRequest(0, 4, 1.2500m, 19.9900m, false) });
        var saleResponse = ApiResponse<object>.Success(sale);
        var saleJson = Net10WireJson.SerializeEnvelope(saleResponse);
        Assert.Equal(Legacy(saleResponse), saleJson);
        Assert.Contains("\"saleDate\":\"2026-11-17\"", saleJson);

        var filter = new SalesReceiptListRequest(date, null, null, null, null, null, null, null);
        var filterResponse = ApiResponse<object>.Success(filter);
        var filterJson = Net10WireJson.SerializeEnvelope(filterResponse);
        Assert.Equal(Legacy(filterResponse), filterJson);
        Assert.Contains("\"fromDate\":\"2026-11-17\"", filterJson);

        var receipt = new GoodsReceiptDto(7, "GR0000000001", date, 2, 3, "Draft", null, null,
            Convert.ToBase64String(new byte[8]),
            new[] { new GoodsReceiptLineDto(8, 1, 4, 1.2500m, 19.9900m) });
        var receiptResponse = ApiResponse<object>.Success(receipt);
        var receiptJson = Net10WireJson.SerializeEnvelope(receiptResponse);
        Assert.Equal(Legacy(receiptResponse), receiptJson);
        Assert.Contains("\"receiptDate\":\"2026-11-17\"", receiptJson);
    }

    [Fact]
    public void Legacy_JToken_datetime_uses_UTC_settings_before_STJ_egress()
    {
        var legacy = new JObject
        {
            ["postedAtUtc"] = new JValue(new DateTime(2026, 11, 17, 13, 14, 15, DateTimeKind.Unspecified)),
            ["note"] = JValue.CreateNull(),
        };
        var expected = Legacy(ApiResponse<object>.Success(legacy));
        Assert.Contains("\"postedAtUtc\":\"2026-11-17T13:14:15Z\"", expected);
        var native = WireObject.Parse(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(legacy, LegacySettings)));
        Assert.Equal(expected, Net10WireJson.SerializeEnvelope(ApiResponse<object>.Success(native)));
    }
}
