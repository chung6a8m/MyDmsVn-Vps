// VPS-05A: System.Text.Json response boundary for the Linux net10 host.
// The separate legacy net48 HttpHost retains Newtonsoft.Json with no source edits.
// TODO VPS-05B: replace remaining Json.NET request DOM and contract attributes.
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyDmsVn.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        NumberHandling = JsonNumberHandling.Strict,
    };

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
        // VPS-05A deliberately retains the trusted JToken projections. Parse
        // them as JsonDocument and emit using System.Text.Json, preserving the
        // existing explicit nulls, property order and stringified int64/decimal.
        // VPS-05B will remove this transitional branch with typed projections.
        if (value is JToken legacy)
        {
            using var document = JsonDocument.Parse(legacy.ToString(Formatting.None));
            return JsonSerializer.Serialize(document.RootElement, Options);
        }
        return JsonSerializer.Serialize(value, value.GetType(), Options);
    }
}
