using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;

namespace MyDmsVn.Server.HttpHost.Features.Auth;

public sealed record LoginBody(
    [property: JsonRequired] string Username,
    [property: JsonRequired] string Password)
{
    public override string ToString() => nameof(LoginBody);
}

// Login has its own historical grammar and request-level error envelope.
// Bind once; never retain the credential source in HttpContext.Items or logs.
internal sealed class AuthLoginFormatter : InputFormatter
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private const string JsonString = "\"(?:[^\"\\\\\\x00-\\x1f]|\\\\(?:[\"\\\\/bfnrt]|u[0-9a-fA-F]{4}))*\"";
    private static readonly Regex LoginGrammar = new Regex("\\A[ \\t\\r\\n]*\\{[ \\t\\r\\n]*" + JsonString + "[ \\t\\r\\n]*:[ \\t\\r\\n]*" + JsonString + "[ \\t\\r\\n]*,[ \\t\\r\\n]*" + JsonString + "[ \\t\\r\\n]*:[ \\t\\r\\n]*" + JsonString + "[ \\t\\r\\n]*\\}[ \\t\\r\\n]*\\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public AuthLoginFormatter() => SupportedMediaTypes.Add("application/json");
    protected override bool CanReadType(Type type) => type == typeof(LoginBody);
    public override bool CanRead(InputFormatterContext context) => CanReadType(context.ModelType);

    public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
    {
        var request = context.HttpContext.Request;
        try
        {
            if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var media) ||
                media.MediaType != "application/json" ||
                !string.IsNullOrEmpty(media.CharSet) && !string.Equals(media.CharSet.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase) ||
                request.Headers.ContainsKey("Content-Encoding"))
                return await Invalid(context).ConfigureAwait(false);

            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, context.HttpContext.RequestAborted).ConfigureAwait(false);
            var bytes = buffer.ToArray();
            var json = Utf8.GetString(bytes);
            if (!LoginGrammar.IsMatch(json)) return await Invalid(context).ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 2 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
                !root.TryGetProperty("username", out var usernameValue) || usernameValue.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("password", out var passwordValue) || passwordValue.ValueKind != JsonValueKind.String)
                return await Invalid(context).ConfigureAwait(false);
            var username = usernameValue.GetString()!;
            var password = passwordValue.GetString()!;
            if (string.IsNullOrWhiteSpace(username) || username.Length > 256 ||
                password.Length == 0 || Utf8.GetByteCount(password) > 72)
                return await Invalid(context).ConfigureAwait(false);
            return await InputFormatterResult.SuccessAsync(new LoginBody(username, password)).ConfigureAwait(false);
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or EncoderFallbackException or
            RegexMatchTimeoutException or InvalidOperationException)
        {
            return await Invalid(context).ConfigureAwait(false);
        }
    }

    private static Task<InputFormatterResult> Invalid(InputFormatterContext context)
    {
        context.ModelState.TryAddModelError("", "The request is invalid.");
        return InputFormatterResult.FailureAsync();
    }
}

// Reject token headers/query before binding credentials, as the legacy action
// did. Core transport/CSRF admission and body limits already ran.
[AttributeUsage(AttributeTargets.Method)]
internal sealed class AuthLoginRequestAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (new Uri(request.GetEncodedUrl(), UriKind.Absolute).Query.Length != 0 ||
            request.Headers.ContainsKey("X-Refresh-Token") || request.Headers.ContainsKey("Refresh-Token"))
        {
            context.Result = V1ApiResults.Error(400, "ValidationError", "The request is invalid.",
                context.HttpContext.RequestServices.GetRequiredService<RequestIdentityAdmission>().CorrelationId);
            return;
        }
        await next().ConfigureAwait(false);
    }
}
