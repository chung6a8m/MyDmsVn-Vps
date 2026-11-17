using System;
using System.Collections.Generic;
using System.Net.Http;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using MyDmsVn.Server.HttpHost;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class CoreApiBoundaryTests
{
    private static DefaultHttpContext NewRequest(string method = "GET", string path = "/api/v1/health")
    {
        var services = new ServiceCollection();
        services.AddSingleton(new WebHostOptions(
            publicOrigin: "https://localhost",
            issuer: "https://localhost",
            audience: "vps04a-acceptance",
            activeKeyId: "test-key",
            validationKeys: new Dictionary<string, byte[]>
            {
                ["test-key"] = Enumerable.Repeat((byte)0x5A, 32).ToArray()
            }));
        services.AddScoped<RequestIdentityAdmission>();
        services.AddSingleton<AuthAttemptLimiter>();
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        context.Request.Method = method;
        context.Request.Path = new PathString(path);
        context.Request.Body = Stream.Null;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task AssertDenied(DefaultHttpContext context, int status)
    {
        var downstreamCalled = false;
        await CoreApiBoundary.HandleAsync(context, () =>
        {
            downstreamCalled = true;
            return Task.CompletedTask;
        });
        Assert.False(downstreamCalled);
        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers["Cache-Control"].ToString());
        Assert.Equal("max-age=31536000", context.Response.Headers["Strict-Transport-Security"].ToString());
    }

    [Theory]
    [InlineData("http", "localhost")]
    [InlineData("https", "other.local")]
    public async Task RefusesInsecureOrUnexpectedOriginAuthority(string scheme, string host)
    {
        var context = NewRequest();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        await AssertDenied(context, 400);
    }

    [Theory]
    [InlineData("Forwarded")]
    [InlineData("X-Forwarded-Proto")]
    [InlineData("X-Forwarded-For")]
    public async Task DoesNotTrustUnadmittedProxyHeaders(string header)
    {
        var context = NewRequest();
        context.Request.Headers[header] = "for=127.0.0.1";
        await AssertDenied(context, 400);
    }

    [Fact]
    public async Task RefusesMutatingOriginWithoutExplicitOriginHeader()
    {
        var context = NewRequest("POST", "/api/v1/auth/login");
        await AssertDenied(context, 403);
    }

    [Fact]
    public async Task RefusesForeignOrigin()
    {
        var context = NewRequest("POST", "/api/v1/auth/login");
        context.Request.Headers.Origin = "https://other.local";
        context.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        await AssertDenied(context, 403);
    }

    [Fact]
    public async Task RefusesCookieEndpointWithoutExplicitAjaxAdmission()
    {
        var context = NewRequest("POST", "/api/v1/auth/login");
        context.Request.Headers.Origin = "https://localhost";
        await AssertDenied(context, 403);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Basic abc")]
    [InlineData("Bearer a b")]
    public async Task ProtectedEndpointNeverFallsBackToAnonymous(string authorization)
    {
        var context = NewRequest("GET", "/api/v1/sales-receipts");
        if (authorization.Length != 0)
            context.Request.Headers.Authorization = authorization;
        await AssertDenied(context, 401);
        Assert.Equal("Bearer", context.Response.Headers["WWW-Authenticate"].ToString());
    }

    [Fact]
    public async Task RefusesOversizedDeclaredRequestBeforeDispatch()
    {
        var context = NewRequest("POST", "/api/v1/auth/login");
        context.Request.Headers.Origin = "https://localhost";
        context.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        context.Request.ContentLength = 1048577;
        await AssertDenied(context, 413);
    }

    [Fact]
    public async Task RefusesOversizedStreamingBodyBeforeDispatch()
    {
        var context = NewRequest("POST", "/api/v1/auth/login");
        context.Request.Headers.Origin = "https://localhost";
        context.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        context.Request.Body = new MemoryStream(new byte[1048577]);
        await AssertDenied(context, 413);
    }

    [Fact]
    public async Task HealthyAnonymousEndpointUsesRequestScopedAdmission()
    {
        var context = NewRequest();
        context.Request.Headers["X-Correlation-ID"] = "vps04a_01";
        var called = false;
        await CoreApiBoundary.HandleAsync(context, () =>
        {
            called = true;
            var admission = context.RequestServices.GetRequiredService<RequestIdentityAdmission>();
            Assert.False(admission.Current.IsAuthenticated);
            Assert.Equal("vps04a_01", admission.CorrelationId);
            Assert.Null(admission.SessionId);
            return Task.CompletedTask;
        });
        Assert.True(called);
        Assert.Equal("vps04a_01", context.Response.Headers["X-Correlation-ID"].ToString());
    }

    [Fact]
    public async Task CookieEndpointAdmitsOnlyExactTrustedAjaxEnvelope()
    {
        var context = NewRequest("POST", "/api/v1/auth/login");
        context.Request.Headers.Origin = "https://localhost";
        context.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        var called = false;
        await CoreApiBoundary.HandleAsync(context, () =>
        {
            called = true;
            Assert.False(context.RequestServices.GetRequiredService<RequestIdentityAdmission>().Current.IsAuthenticated);
            return Task.CompletedTask;
        });
        Assert.True(called);
    }

    [Fact]
    public async Task FailureBeforeResponseStartsCannotCommitStaleCookiesOrHeaders()
    {
        var context = NewRequest();
        context.Request.Headers["X-Correlation-ID"] = "failure_01";
        await CoreApiBoundary.HandleAsync(context, () =>
        {
            // Simulate a handler that staged a refresh cookie, then threw before
            // the HTTP response started. The failure envelope must be authoritative.
            context.Response.Headers["Set-Cookie"] = "__Host-mydmsvn-rt=unadmitted; Secure; HttpOnly";
            context.Response.Headers["Location"] = "https://untrusted.test/";
            context.Response.ContentType = "text/plain";
            context.Response.ContentLength = 3;
            throw new InvalidOperationException("secret-sql-cause");
        });
        Assert.Equal(500, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
        Assert.False(context.Response.Headers.ContainsKey("Location"));
        Assert.StartsWith("application/json", context.Response.ContentType!);
        Assert.Equal("no-store", context.Response.Headers["Cache-Control"].ToString());
        Assert.Equal("failure_01", context.Response.Headers["X-Correlation-ID"].ToString());
        var body = System.Text.Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        Assert.Contains("InternalError", body);
        Assert.DoesNotContain("secret-sql-cause", body);
        Assert.DoesNotContain("unadmitted", body);
    }

    [Fact]
    public async Task MultipleCorrelationValuesCannotInjectResponseHeaders()
    {
        var context = NewRequest();
        context.Request.Headers["X-Correlation-ID"] = new StringValues(new[] { "one", "two" });
        await CoreApiBoundary.HandleAsync(context, () => Task.CompletedTask);
        var correlation = context.Response.Headers["X-Correlation-ID"].ToString();
        Assert.Matches(new Regex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant), correlation);
    }

    private sealed class ScopedIdentityMarker { }

    private sealed class SingletonCapturingScopedIdentity
    {
        public SingletonCapturingScopedIdentity(ScopedIdentityMarker scoped) => Scoped = scoped;
        public ScopedIdentityMarker Scoped { get; }
    }

    [Fact]
    public void ProductionHostRejectsSingletonCaptureOfScopedIdentity()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production"
        });
        Program.RequireScopeValidation(builder);
        builder.Services.AddScoped<ScopedIdentityMarker>();
        builder.Services.AddSingleton<SingletonCapturingScopedIdentity>();
        using var app = builder.Build();
        Assert.Throws<InvalidOperationException>(() =>
            app.Services.GetRequiredService<SingletonCapturingScopedIdentity>());
    }

    [Fact]
    public void FallbackRouteIncludesUnknownApiPathsWithFileExtensions()
    {
        using var app = WebApplication.CreateBuilder().Build();
        Net10RouteTable.MapFallback(app);
        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        Assert.Contains(endpoints, endpoint => endpoint.RoutePattern.RawText == "{**path}" &&
            endpoint.Order == int.MaxValue);
    }

    [Fact]
    public async Task UnknownDottedApiPathProducesNotFoundJsonEnvelope()
    {
        var context = NewRequest("GET", "/api/v1/unknown.json");
        context.RequestServices.GetRequiredService<RequestIdentityAdmission>().BeginCorrelation("fallback_01");
        await Net10RouteTable.FallbackAsync(context);
        Assert.Equal(404, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType!);
        using var document = JsonDocument.Parse(((MemoryStream)context.Response.Body).ToArray());
        Assert.Contains("NotFound", document.RootElement.GetRawText());
        Assert.Contains("fallback_01", document.RootElement.GetRawText());
    }

    [Fact]
    public async Task LegacyRequestUriKeepsEscapedPathDelimitersSeparateFromQuery()
    {
        var context = NewRequest("GET", "/api/v1/sales-receipts/a?b#c");
        context.Request.QueryString = new QueryString("?feature=sales");
        using var message = await Net10RouteTable.CreateMessage(context);
        Assert.Equal("/api/v1/sales-receipts/a%3Fb%23c", message.RequestUri!.AbsolutePath);
        Assert.Equal("?feature=sales", message.RequestUri.Query);
    }

    [Fact]
    public void RouteOrderAndDedicatedMethodMissEndpointsRemainExplicit()
    {
        using var app = WebApplication.CreateBuilder().Build();
        Net10RouteTable.Map(app);
        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        var valid = endpoints.Where(e => e.Order < int.MaxValue - 1).ToArray();
        var misses = endpoints.Where(e => e.Order == int.MaxValue - 1).ToArray();
        Assert.Equal(57, valid.Length);
        Assert.Equal(valid.Select(e => e.RoutePattern.RawText).Distinct().Count(), misses.Length);
        Assert.Contains(valid, e => e.RoutePattern.RawText == "/api/v3/operations/{operationId}/resend-eligibility" &&
            e.Order == 2);
        Assert.Contains(misses, e => e.RoutePattern.RawText == "/api/v1/catalog/products");
    }

    [Fact]
    public async Task MethodMissSerializesLegacyJson405AndAllowedVerbs()
    {
        var context = NewRequest("PATCH", "/api/v1/catalog/products");
        context.RequestServices.GetRequiredService<RequestIdentityAdmission>().BeginCorrelation("method_01");
        await Net10RouteTable.MethodNotAllowedAsync(context, new[] { "GET", "POST" });
        Assert.Equal(405, context.Response.StatusCode);
        Assert.Equal("GET, POST", context.Response.Headers.Allow.ToString());
        Assert.StartsWith("application/json", context.Response.ContentType!);
        using var document = JsonDocument.Parse(((MemoryStream)context.Response.Body).ToArray());
        Assert.Contains("Request.Invalid", document.RootElement.GetRawText());
        Assert.Contains("method_01", document.RootElement.GetRawText());
    }

    [Fact]
    public async Task NativeHttpRouterDistinguishesKnownMethodMissFromUnknownPathWithoutSql()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(new WebHostOptions(
            publicOrigin: "https://localhost",
            issuer: "https://localhost",
            audience: "routing-only-test",
            activeKeyId: "routing-test-key",
            validationKeys: new Dictionary<string, byte[]>
            {
                ["routing-test-key"] = Enumerable.Repeat((byte)0x5A, 32).ToArray()
            }));
        builder.Services.AddSingleton<AuthAttemptLimiter>();
        builder.Services.AddScoped<RequestIdentityAdmission>();
        await using var app = builder.Build();
        // Isolate routing, error envelopes and verb constraints from the
        // SQL-dependent security/session admission exercised in later gates.
        app.Use(async (context, next) =>
        {
            context.RequestServices.GetRequiredService<RequestIdentityAdmission>()
                .BeginCorrelation("routing_01");
            await next().ConfigureAwait(false);
        });
        Net10RouteTable.Map(app);
        Net10RouteTable.MapFallback(app);
        await app.StartAsync();
        try
        {
            var addresses = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?.Addresses;
            var address = Assert.Single(addresses!);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

            using var wrongVerb = await client.SendAsync(new HttpRequestMessage(
                HttpMethod.Delete, address + "/api/v1/health"));
            Assert.Equal(405, (int)wrongVerb.StatusCode);
            Assert.Contains("GET", wrongVerb.Content.Headers.Allow);
            Assert.Contains("Request.Invalid", await wrongVerb.Content.ReadAsStringAsync());

            using var unknown = await client.GetAsync(address + "/api/v1/unknown.json");
            Assert.Equal(404, (int)unknown.StatusCode);
            Assert.Contains("NotFound", await unknown.Content.ReadAsStringAsync());

            using var health = await client.GetAsync(address + "/api/v1/health");
            Assert.Equal(200, (int)health.StatusCode);
            Assert.Contains("ok", await health.Content.ReadAsStringAsync());
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public void ReflectedNet10RoutesRemainUniqueAndCounted()
    {
        using var document = JsonDocument.Parse(Net10RouteTable.ToInventoryJson());
        var values = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(57, values.Length);
        Assert.Equal(57, values.Select(e => e.GetProperty("httpMethod").GetString() + " " +
            e.GetProperty("path").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(values, e => e.GetProperty("path").GetString() == "/api/v1/health");
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("?limit=25", "limit=25")]
    [InlineData("?search=abc%2Fdef&limit=25", "search=abc%2Fdef&limit=25")]
    [InlineData("?limit=25&limit=50", "limit=25&limit=50")]
    public void NativeQueryAdapterUsesFrozenOwinRawValueNotAspNetCoreQuestionMark(
        string coreQuery, string expectedLegacyValue)
    {
        // WEB2/3 strict query grammar reads IOwinRequest.QueryString.Value,
        // not HttpRequest.Query (which would silently lose duplicates).
        var context = NewRequest(path: "/api/v1/catalog/products/lookup");
        context.Request.QueryString = new QueryString(coreQuery);
        var adapter = new Microsoft.Owin.CoreContextAdapter(context, null!, null!);
        Assert.Equal(expectedLegacyValue, adapter.Request.QueryString.Value);
    }
}
