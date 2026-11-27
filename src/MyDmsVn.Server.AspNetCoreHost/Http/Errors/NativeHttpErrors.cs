using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using MyDmsVn.Contracts;

namespace MyDmsVn.Server.HttpHost;

// Admission and routing failures occur outside MVC. Use the same typed error
// envelope and serializer profile, without HttpResponseMessage or a legacy DOM.
internal static class NativeHttpErrors
{
    private static readonly JsonSerializerOptions Json = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        V1HttpJson.Configure(options);
        return options;
    }

    internal static Task WriteAsync(HttpContext context, int status, string code, string message, string correlationId) =>
        WriteAsync(context, new ApiError((ApiStatusCode)status, code, message, correlationId: correlationId));

    internal static async Task WriteAsync(HttpContext context, ApiError error)
    {
        NativeOperationDiagnostics.RecordError(context, error);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new V1ErrorEnvelope(error), Json);
        context.Response.StatusCode = (int)error.Status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }
}
