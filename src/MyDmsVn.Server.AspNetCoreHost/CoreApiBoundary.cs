using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Server.Application.Identity;

namespace MyDmsVn.Server.HttpHost;

// Native ASP.NET Core admission boundary. Never install a permissive fallback on startup failure.
internal static class CoreApiBoundary
{
    internal static async Task HandleAsync(HttpContext context, Func<Task> next)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next().ConfigureAwait(false);
            return;
        }

        var correlation = SafeCorrelation(HeaderValues(context, "X-Correlation-ID"));
        SetHeaders(context, correlation);
        try
        {
            var options = context.RequestServices.GetRequiredService<WebHostOptions>();
            using var guard = CheckTransport(context, options, correlation);
            if (guard != null)
            {
                await Net10RouteTable.WriteAsync(context, guard).ConfigureAwait(false);
                return;
            }

            if (context.Request.ContentLength > options.MaximumRequestBytes)
            {
                await TooLarge(context, correlation).ConfigureAwait(false);
                return;
            }

            using var body = await BufferAsync(context, options.MaximumRequestBytes).ConfigureAwait(false);
            if (body == null)
            {
                await TooLarge(context, correlation).ConfigureAwait(false);
                return;
            }

            context.Request.Body = body;
            var identity = context.RequestServices.GetRequiredService<RequestIdentityAdmission>();
            identity.BeginCorrelation(correlation);

            var actor = CurrentUser.Anonymous;
            Guid? sessionId = null;
            var path = context.Request.Path.Value ?? string.Empty;
            var cookieEndpoint = HttpMethods.IsPost(context.Request.Method) && IsCookieEndpoint(path);
            // Endpoint routing accepts case and trailing-slash variants: all must
            // share the same anonymous admission and degraded-health outcome.
            var healthEndpoint = HttpMethods.IsGet(context.Request.Method) &&
                path.TrimEnd('/').Equals("/api/v1/health", StringComparison.OrdinalIgnoreCase);
            var anonymous = cookieEndpoint || healthEndpoint;
            var authorization = HeaderValues(context, "Authorization");
            if (!anonymous || !cookieEndpoint && authorization != null)
            {
                var token = ReadBearer(authorization);
                var validated = token == null ? null
                    : new BearerTokenValidator(options, context.RequestServices.GetRequiredService<IUtcClock>()).Validate(token);
                if (validated == null)
                {
                    using var denied = Unauthorized(correlation);
                    await Net10RouteTable.WriteAsync(context, denied).ConfigureAwait(false);
                    return;
                }

                var user = await context.RequestServices.GetRequiredService<IWebAuthSessionStore>()
                    .GetActiveUserAsync(validated.SessionId, validated.UserId, context.RequestAborted)
                    .ConfigureAwait(false);
                if (user == null || !user.IsAuthenticated || !user.IsActive || user.UserId != validated.UserId)
                {
                    using var denied = Unauthorized(correlation);
                    await Net10RouteTable.WriteAsync(context, denied).ConfigureAwait(false);
                    return;
                }
                actor = user;
                sessionId = validated.SessionId;
            }

            identity.Admit(actor, sessionId);
            if (healthEndpoint)
            {
                var audit = context.RequestServices.GetRequiredService<ISecurityAuditReadiness>().Snapshot;
                if (audit.Status == SecurityAuditReadinessStatus.Degraded)
                {
                    using var unavailable = HttpJson.Error(503,
                        SecurityAuditReadinessSnapshot.PersistenceFailureReason,
                        "Required security audit persistence is unavailable.", correlation);
                    await Net10RouteTable.WriteAsync(context, unavailable).ConfigureAwait(false);
                    return;
                }
            }
            await next().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            if (context.Response.HasStarted) throw;
            // A downstream failure can occur after a handler added Set-Cookie, Location or
            // other response headers but before headers were sent. Never send them with a
            // new 500 envelope: an unadmitted refresh cookie would misrepresent success.
            context.Response.Headers.Clear();
            SetHeaders(context, correlation);
            // SQL errors, key material, claims and tokens must not enter the error response.
            using var failure = HttpJson.Error(500, "InternalError",
                "An unexpected error occurred.", correlation);
            await Net10RouteTable.WriteAsync(context, failure).ConfigureAwait(false);
        }
    }

    internal static IList<string>? HeaderValues(HttpContext context, string name) =>
        context.Request.Headers.TryGetValue(name, out var values) ? values.Select(value => value ?? string.Empty).ToArray() : null;

    private static string SafeCorrelation(IList<string>? values) =>
        values?.Count == 1 && Regex.IsMatch(values[0], @"\A[A-Za-z0-9_-]{1,128}\z")
            ? values[0] : Guid.NewGuid().ToString("N");

    private static string? ReadBearer(IList<string>? values)
    {
        if (values == null || values.Count != 1) return null;
        var text = values[0];
        if (!text.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || text.Length <= 7)
            return null;
        var token = text.Substring(7);
        return token.Any(c => char.IsWhiteSpace(c) || c == ',') ? null : token;
    }

    private static HttpResponseMessage Unauthorized(string correlation)
    {
        var response = HttpJson.Error(401, "Auth.Unauthorized",
            "Authentication is required.", correlation);
        response.Headers.WwwAuthenticate.Add(new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer"));
        return response;
    }

    private static bool IsCookieEndpoint(string path)
    {
        var route = path.TrimEnd('/');
        return route.Equals("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase) ||
            route.Equals("/api/v1/auth/refresh", StringComparison.OrdinalIgnoreCase) ||
            route.Equals("/api/v1/auth/logout", StringComparison.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage? CheckTransport(HttpContext context, WebHostOptions options, string correlation)
    {
        var request = context.Request;
        var originAuthority = new Uri(options.PublicOrigin).Authority;
        if (!request.IsHttps || request.Host.Value != originAuthority ||
            request.Headers.Keys.Any(key => key.Equals("Forwarded", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)))
        {
            return HttpJson.Error(400, "Request.InvalidTransport",
                "The request transport is invalid.", correlation);
        }

        var origins = HeaderValues(context, "Origin");
        var safe = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method);
        var sites = HeaderValues(context, "Sec-Fetch-Site");
        if ((!safe && origins == null) ||
            origins != null && (origins.Count != 1 || origins[0] != options.PublicOrigin) ||
            !safe && sites != null && (sites.Count != 1 || sites[0] != "same-origin"))
            return HttpJson.Error(403, "Auth.Forbidden", "Access is denied.",
                correlation);

        if (HttpMethods.IsPost(request.Method) && IsCookieEndpoint(request.Path.Value ?? string.Empty))
        {
            var requested = HeaderValues(context, "X-Requested-With");
            if (requested == null || requested.Count != 1 || requested[0] != "XMLHttpRequest")
                return HttpJson.Error(403, "Auth.Forbidden", "Access is denied.",
                    correlation);
        }
        return null;
    }

    private static void SetHeaders(HttpContext context, string correlation)
    {
        context.Response.Headers["X-Correlation-ID"] = correlation;
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "0";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    }

    private static async Task<MemoryStream?> BufferAsync(HttpContext context, int maxBytes)
    {
        var body = new MemoryStream();
        try
        {
            var buffer = new byte[8192];
            while (true)
            {
                var read = await context.Request.Body.ReadAsync(
                    buffer, 0, buffer.Length, context.RequestAborted).ConfigureAwait(false);
                if (read == 0) break;
                if (body.Length + read > maxBytes)
                {
                    body.Dispose();
                    return null;
                }
                await body.WriteAsync(buffer, 0, read, context.RequestAborted).ConfigureAwait(false);
            }
            body.Position = 0;
            return body;
        }
        catch
        {
            body.Dispose();
            throw;
        }
    }

    private static async Task TooLarge(HttpContext context, string correlation)
    {
        using var response = HttpJson.Error(413, "Request.TooLarge",
            "The request is too large.", correlation);
        await Net10RouteTable.WriteAsync(context, response).ConfigureAwait(false);
    }
}

internal sealed class CoreApiAdmissionMiddleware : IMiddleware
{
    public Task InvokeAsync(HttpContext context, RequestDelegate next) =>
        CoreApiBoundary.HandleAsync(context, () => next(context));
}
