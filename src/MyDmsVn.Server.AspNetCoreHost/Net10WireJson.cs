// VPS-05A: System.Text.Json response boundary for the Linux net10 host.
// The separate legacy net48 HttpHost retains Newtonsoft.Json with no source edits.
// VPS-05B: use the net10 strict STJ request DOM and explicit DTO calendar metadata.
using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MyDmsVn.Contracts;

namespace MyDmsVn.Server.HttpHost;

public static class HttpJson
{
    public static HttpResponseMessage Success<T>(T data) => From(ApiResponse<T>.Success(data));

    public static HttpResponseMessage From<T>(ApiResponse<T> result) =>
        new ApiHttpResponseMessage(result.IsSuccess ? HttpStatusCode.OK : (HttpStatusCode)result.Error!.Status)
        {
            Content = new StringContent(Net10WireJson.SerializeEnvelope(result), Encoding.UTF8, "application/json")
        };

    public static HttpResponseMessage Error(int status, string code, string message, string correlationId) =>
        From(ApiResponse<object>.Failure(new ApiError((ApiStatusCode)status, code, message, correlationId: correlationId)));
}

internal sealed class ApiHttpResponseMessage : HttpResponseMessage
{
    public ApiHttpResponseMessage(HttpStatusCode status) : base(status) { }
}

/// <summary>
/// Native net10 response writer. Never serialize ApiResponse directly: Newtonsoft
/// used ShouldSerializeData/Error and ignored Status/IsSuccess. That is not the
/// System.Text.Json contract, so the envelope must be projected explicitly.
/// </summary>
internal static class Net10WireJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        // Match CamelCasePropertyNamesContractResolver for typed payloads and
        // dictionary keys; the latter is a separate setting in System.Text.Json.
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(typeInfo =>
        {
            // Net10 contract metadata is explicit and must never depend on
            // Newtonsoft attributes in the shared net48 DTO assembly.
            var saleType = typeInfo.Type == typeof(SalesReceiptDto) ||
                typeInfo.Type == typeof(SalesReceiptListRequest) ||
                typeInfo.Type == typeof(SalesReceiptListRow) ||
                typeInfo.Type == typeof(SaveSalesReceiptRequest) ||
                typeInfo.Type == typeof(UpdateSalesReceiptDraftRequest);
            var receiptType = typeInfo.Type == typeof(GoodsReceiptDto) ||
                typeInfo.Type == typeof(SaveGoodsReceiptRequest) ||
                typeInfo.Type == typeof(UpdateGoodsReceiptDraftRequest);
            foreach (var property in typeInfo.Properties)
            {
                if (property.PropertyType != typeof(DateTime) &&
                    property.PropertyType != typeof(DateTime?)) continue;
                var isCalendarDate = saleType &&
                        (string.Equals(property.Name, "saleDate", StringComparison.OrdinalIgnoreCase) ||
                         typeInfo.Type == typeof(SalesReceiptListRequest) &&
                         (string.Equals(property.Name, "fromDate", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(property.Name, "toDate", StringComparison.OrdinalIgnoreCase))) ||
                    receiptType && string.Equals(property.Name, "receiptDate", StringComparison.OrdinalIgnoreCase);
                if (!isCalendarDate) continue;
                property.CustomConverter = property.PropertyType == typeof(DateTime)
                    ? (JsonConverter)new CalendarDateWriter()
                    : new NullableCalendarDateWriter();
            }
        });
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
            NumberHandling = JsonNumberHandling.Strict,
            TypeInfoResolver = resolver,
        };
        options.Converters.Add(new UtcDateTimeWriter());
        return options;
    }

    private sealed class UtcDateTimeWriter : System.Text.Json.Serialization.JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("VPS-05A supports response writing only.");

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            // Json.NET DateTimeZoneHandling.Utc: SQL datetime2 Unspecified is
            // labelled UTC, while an actual Local value is converted to UTC.
            var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime()
                : DateTime.SpecifyKind(value, DateTimeKind.Utc);
            writer.WriteStringValue(utc);
        }
    }

    private sealed class CalendarDateWriter : System.Text.Json.Serialization.JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("VPS-05A supports response writing only.");

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private sealed class NullableCalendarDateWriter : System.Text.Json.Serialization.JsonConverter<DateTime?>
    {
        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("VPS-05A supports response writing only.");

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
                writer.WriteStringValue(value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            else
                writer.WriteNullValue();
        }
    }

    internal static string SerializeEnvelope<T>(ApiResponse<T> response)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));
        if (response.IsSuccess)
            return "{\"data\":" + SerializeValue(response.Data!) + "}";
        var error = response.Error ?? throw new InvalidOperationException("An error response requires an error.");
        // Status is out-of-band and must never leak into the JSON body.
        return "{\"error\":" + SerializeValue(new
        {
            error.Code, error.Message, error.Details, error.CorrelationId
        }) + "}";
    }

    private static string SerializeValue(object value)
    {
        // The net10 projection tree is already backed by System.Text.Json.Nodes.
        // Preserve explicit null members, lexical numbers and insertion order.
        if (value is WireToken wire)
            return System.Text.Json.JsonSerializer.Serialize(wire.Node, Options);
        return System.Text.Json.JsonSerializer.Serialize(value, value.GetType(), Options);
    }
}
