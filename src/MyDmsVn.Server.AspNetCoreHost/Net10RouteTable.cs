using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace MyDmsVn.Server.HttpHost;

// The transport changes, not the public contract or business controller source.
// The hard 57-route guard prevents a source-link or reflection drift from silently
// returning 404 for a Desktop API while a migration PR appears green.
internal static class Net10RouteTable
{
    internal sealed record Route(string Verb, string Path, int Order, Type Controller, MethodInfo Action);
    private static readonly IReadOnlyList<Route> Routes = Discover();

    internal static string ToInventoryJson() =>
        JsonSerializer.Serialize(Routes.Select(r => new
        {
            httpMethod = r.Verb, path = r.Path, controller = r.Controller.Name,
            action = r.Action.Name, order = r.Order
        }).ToArray());

    internal static void Map(WebApplication app)
    {
        foreach (var route in Routes)
        {
            // ASP.NET Core routing chooses literals over parameters, independently of enumeration order.
            app.MapMethods(route.Path, new[] { route.Verb },
                context => Dispatch(context, route));
        }
    }

    internal static void MapFallback(WebApplication app)
    {
        // The default MapFallback pattern excludes paths containing a filename.
        // Every unknown /api path, including dotted segments, needs the WEB2 JSON 404 envelope.
        app.MapFallback("{**path}", (HttpContext context) => FallbackAsync(context));
    }

    internal static async Task FallbackAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            using var response = HttpJson.Error(404, "NotFound", "The resource was not found.",
                context.RequestServices.GetRequiredService<RequestIdentityAdmission>().CorrelationId);
            await WriteAsync(context, response).ConfigureAwait(false);
        }
        else
        {
            // SPA static-file routing, proxy configuration and systemd are VPS-04B/VPS-08 gates.
            context.Response.StatusCode = 404;
        }
    }

    private static IReadOnlyList<Route> Discover()
    {
        var routes = new List<Route>();
        foreach (var type in typeof(Program).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ApiController).IsAssignableFrom(t)))
        {
            var prefix = type.GetCustomAttribute<RoutePrefixAttribute>();
            if (prefix == null) continue;
            foreach (var action in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var route = action.GetCustomAttribute<RouteAttribute>();
                if (route == null) continue;
                var verb = action.GetCustomAttribute<HttpGetAttribute>() != null ? "GET"
                    : action.GetCustomAttribute<HttpPostAttribute>() != null ? "POST"
                    : action.GetCustomAttribute<HttpPutAttribute>() != null ? "PUT"
                    : action.GetCustomAttribute<HttpDeleteAttribute>() != null ? "DELETE"
                    : throw new InvalidOperationException("An API route lacks an HTTP method.");
                var path = "/" + prefix.Prefix.Trim('/') +
                    (route.Template.Trim('/').Length == 0 ? "" : "/" + route.Template.Trim('/'));
                if (action.GetParameters().Any(p =>
                    p.ParameterType != typeof(string) && p.ParameterType != typeof(CancellationToken)))
                    throw new InvalidOperationException("Unexpected HTTP controller parameter type.");
                routes.Add(new Route(verb, path, route.Order, type, action));
            }
        }

        if (routes.Count != 57 || routes.Select(x => (x.Verb, x.Path))
            .Distinct().Count() != 57)
            throw new InvalidOperationException("Frozen API route inventory drift.");
        return routes.OrderBy(x => x.Verb, StringComparer.Ordinal)
            .ThenBy(x => x.Path, StringComparer.Ordinal).ToArray();
    }

    private static async Task Dispatch(HttpContext context, Route route)
    {
        using var request = await CreateMessage(context).ConfigureAwait(false);
        var controller = (ApiController)ActivatorUtilities.CreateInstance(context.RequestServices, route.Controller);
        controller.Request = request;
        var parameters = route.Action.GetParameters().Select(p =>
        {
            if (p.ParameterType == typeof(CancellationToken))
                return (object)context.RequestAborted;
            return context.Request.RouteValues.TryGetValue(p.Name!, out var value)
                ? value?.ToString() ?? string.Empty
                : throw new InvalidOperationException("Route parameter binding was not satisfied.");
        }).ToArray();
        var invoked = route.Action.Invoke(controller, parameters);
        var response = invoked switch
        {
            HttpResponseMessage message => message,
            Task<HttpResponseMessage> task => await task.ConfigureAwait(false),
            _ => throw new InvalidOperationException("HTTP action returned an unsupported response.")
        };
        if ((int)response.StatusCode >= 300 && response is not ApiHttpResponseMessage)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            var code = status == 401 ? "Auth.Unauthorized" : status == 403 ? "Auth.Forbidden" :
                status == 404 ? "NotFound" : status >= 500 ? "InternalError" : "Request.Invalid";
            var message = status == 401 ? "Authentication is required." : status == 403 ?
                "Access is denied." : status == 404 ? "The resource was not found." :
                status >= 500 ? "An unexpected error occurred." : "The request is invalid.";
            response = HttpJson.Error(status is >= 300 and < 400 ? 500 : status,
                code, message, context.RequestServices.GetRequiredService<RequestIdentityAdmission>().CorrelationId);
        }
        using (response)
            await WriteAsync(context, response).ConfigureAwait(false);
    }

    internal static async Task<HttpRequestMessage> CreateMessage(HttpContext context)
    {
        using var stream = new MemoryStream();
        await context.Request.Body.CopyToAsync(stream, context.RequestAborted).ConfigureAwait(false);
        // Path.Value is decoded; concatenating it can reinterpret encoded '?' and '#'
        // as new query/fragment delimiters. Preserve the URI boundary for wire parsers.
        var uri = new Uri(context.Request.GetEncodedUrl(), UriKind.Absolute);
        var result = new HttpRequestMessage(new HttpMethod(context.Request.Method), uri);
        foreach (var pair in context.Request.Headers)
        {
            if (pair.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                continue;
            result.Headers.TryAddWithoutValidation(pair.Key, pair.Value.ToArray());
        }
        if (stream.Length != 0 ||
            HttpMethods.IsPost(context.Request.Method) ||
            HttpMethods.IsPut(context.Request.Method) ||
            context.Request.Headers.ContainsKey("Content-Type"))
        {
            result.Content = new ByteArrayContent(stream.ToArray());
            foreach (var pair in context.Request.Headers)
                if (pair.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) &&
                    !pair.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    result.Content.Headers.TryAddWithoutValidation(pair.Key, pair.Value.ToArray());
        }
        CoreRequestContextExtension.AttachContext(result,
            new Microsoft.Owin.CoreContextAdapter(context,
                context.RequestServices.GetRequiredService<WebHostOptions>(),
                context.RequestServices.GetRequiredService<AuthAttemptLimiter>()));
        return result;
    }

    internal static async Task WriteAsync(HttpContext context, HttpResponseMessage message)
    {
        context.Response.StatusCode = (int)message.StatusCode;
        foreach (var header in message.Headers)
            context.Response.Headers[header.Key] = header.Value.ToArray();

        if (message.Content == null) return;
        foreach (var header in message.Content.Headers)
            if (!header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                context.Response.Headers[header.Key] = header.Value.ToArray();

        var bytes = await message.Content.ReadAsByteArrayAsync(context.RequestAborted).ConfigureAwait(false);
        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }
}

[RoutePrefix("api/v1")]
public sealed class HealthController : ApiController
{
    [HttpGet, Route("health")]
    public HttpResponseMessage Get() => HttpJson.Success(new { status = "ok" });
}
