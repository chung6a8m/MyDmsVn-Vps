// VPS-04A only: compile-time facade for unchanged legacy HTTP handlers.
// No Microsoft.Owin, System.Web or Microsoft.AspNet.WebApi package is loaded in the net10 host.
// VPS-04B will replace these source-level facades with native ASP.NET Core request handlers.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using MyDmsVn.Server.HttpHost;

namespace System.Web.Http
{
    public abstract class ApiController
    {
        public HttpRequestMessage Request { get; set; } = null!;
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class RoutePrefixAttribute : Attribute
    {
        public RoutePrefixAttribute(string prefix) => Prefix = prefix;
        public string Prefix { get; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RouteAttribute : Attribute
    {
        public RouteAttribute(string template) => Template = template;
        public string Template { get; }
        public int Order { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HttpGetAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HttpPostAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HttpPutAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HttpDeleteAttribute : Attribute { }
}

namespace Microsoft.Owin
{
    // Legacy source uses only these three request-scope accessors.
    // They are views of ASP.NET Core HttpContext, not OWIN hosting objects.
    public interface IOwinContext
    {
        T Get<T>(string key);
        IOwinRequest Request { get; }
    }

    public interface IOwinRequest
    {
        string? RemoteIpAddress { get; }
        IOwinRequestHeaders Headers { get; }
    }

    public interface IOwinRequestHeaders
    {
        IList<string>? GetValues(string name);
    }

    internal sealed class CoreContextAdapter : IOwinContext, IOwinRequest, IOwinRequestHeaders
    {
        private readonly HttpContext _context;
        private readonly WebHostOptions _options;
        private readonly AuthAttemptLimiter _limiter;

        internal CoreContextAdapter(HttpContext context, WebHostOptions options, AuthAttemptLimiter limiter)
        {
            _context = context;
            _options = options;
            _limiter = limiter;
        }

        public IOwinRequest Request => this;
        public IOwinRequestHeaders Headers => this;
        public string? RemoteIpAddress => _context.Connection.RemoteIpAddress?.ToString();

        public IList<string>? GetValues(string name) =>
            _context.Request.Headers.TryGetValue(name, out var values)
                ? values.ToArray()
                : null;

        public T Get<T>(string key)
        {
            object value = key switch
            {
                Web2Pipeline.ScopeKey => _context.RequestServices,
                AuthController.OptionsKey => _options,
                AuthController.LimiterKey => _limiter,
                _ => throw new InvalidOperationException("Unknown request-scope key.")
            };
            return (T)value;
        }
    }
}

namespace System.Net.Http
{
    public static class CoreRequestContextExtension
    {
        private const string Key = "mydmsvn.aspnetcore.context";
        public static Microsoft.Owin.IOwinContext GetOwinContext(this HttpRequestMessage request) =>
            request.Options.TryGetValue(new HttpRequestOptionsKey<Microsoft.Owin.IOwinContext>(Key), out var context)
                ? context : throw new InvalidOperationException("HTTP request context is unavailable.");

        internal static void AttachContext(HttpRequestMessage request, Microsoft.Owin.IOwinContext context) =>
            request.Options.Set(new HttpRequestOptionsKey<Microsoft.Owin.IOwinContext>(Key), context);
    }
}
