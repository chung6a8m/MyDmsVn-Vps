using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Server.HttpHost.Features.Sales;

namespace MyDmsVn.Server.HttpHost;

// Lifetime is the admitted HTTP request. Only the native operation factory
// reads the validated source; a controller receives typed DTOs, never JSON DOM.
internal static class V1ValidatedRequestSource
{
    private static readonly object Key = new();

    internal static void Set(HttpContext context, JsonDocument document)
    {
        context.Items.Add(Key, document);
        context.Response.RegisterForDispose(document);
    }

    internal static JsonElement Get(HttpContext context) =>
        context.Items.TryGetValue(Key, out var source) && source is JsonDocument document
            ? document.RootElement : throw new InvalidOperationException("Validated operation source is missing.");
}

// Match the existing ID-before-query-before-body order on mutation routes.
// Core admission (transport, authentication, cap) runs before MVC resource filters.
[AttributeUsage(AttributeTargets.Method)]
internal sealed class V1SalesMutationAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        try
        {
            var request = context.HttpContext.Request;
            if (request.RouteValues.TryGetValue("saleId", out var id))
                V1ScalarBinders.PositiveLong(id as string ?? "", "saleId");
            V1QueryPolicy.Read(request);
        }
        catch (V1BindingException error)
        {
            context.Result = V1ApiResults.Invalid(error.Field,
                context.HttpContext.RequestServices.GetRequiredService<RequestIdentityAdmission>().CorrelationId);
            return;
        }
        await next().ConfigureAwait(false);
    }
}

// Native MVC System.Text.Json input formatter for Sales mutations only.
// A single strict parse supplies both typed input and the immutable canonical
// source. The explicit field traversal is needed for v1 error precedence, not
// a new general serializer; other MVC routes keep the normal JSON formatter.
internal sealed class V1SalesRequestFormatter : InputFormatter
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal const string ErrorPrefix = "vps10-sales:";

    public V1SalesRequestFormatter() => SupportedMediaTypes.Add("application/json");
    protected override bool CanReadType(Type type) => SalesRequestReader.Supports(type);
    public override bool CanRead(InputFormatterContext context) => CanReadType(context.ModelType);

    public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
    {
        var request = context.HttpContext.Request;
        JsonDocument? document = null;
        try
        {
            if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var media) ||
                !string.Equals(media.MediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
                media.CharSet != null && !string.Equals(media.CharSet.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase) ||
                request.Headers.ContainsKey("Content-Encoding"))
                throw new V1BindingException();

            // CoreApiBoundary has buffered and bounded the body before dispatch,
            // including chunked requests. Do not reread or rewind it for hashing.
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, request.HttpContext.RequestAborted).ConfigureAwait(false);
            var bytes = buffer.ToArray();
            Utf8.GetCharCount(bytes);
            document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                MaxDepth = 16, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new V1BindingException();
            ValidateTree(document.RootElement, 0);
            var model = SalesRequestReader.Read(context.ModelType, document.RootElement);
            V1ValidatedRequestSource.Set(context.HttpContext, document);
            document = null; // registered for disposal at response completion
            return await InputFormatterResult.SuccessAsync(model).ConfigureAwait(false);
        }
        catch (V1BindingException error)
        {
            context.ModelState.TryAddModelError(ErrorPrefix + (error.Field ?? ""), "The field or request format is invalid.");
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or EncoderFallbackException or FormatException or InvalidOperationException)
        {
            // Never return parser text or untrusted property names.
            context.ModelState.TryAddModelError(ErrorPrefix, "The field or request format is invalid.");
        }
        finally { document?.Dispose(); }
        return await InputFormatterResult.FailureAsync().ConfigureAwait(false);
    }

    private static void ValidateTree(JsonElement value, int depth)
    {
        if (depth > 15) throw new V1BindingException();
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    Utf8.GetByteCount(property.Name);
                    if (!names.Add(property.Name)) throw new V1BindingException();
                    ValidateTree(property.Value, depth + 1);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray()) ValidateTree(item, depth + 1);
                break;
            case JsonValueKind.String:
                Utf8.GetByteCount(value.GetString()!);
                break;
        }
    }
}
