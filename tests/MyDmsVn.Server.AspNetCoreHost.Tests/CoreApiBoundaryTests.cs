using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
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
    public async Task MultipleCorrelationValuesCannotInjectResponseHeaders()
    {
        var context = NewRequest();
        context.Request.Headers["X-Correlation-ID"] = new StringValues(new[] { "one", "two" });
        await CoreApiBoundary.HandleAsync(context, () => Task.CompletedTask);
        var correlation = context.Response.Headers["X-Correlation-ID"].ToString();
        Assert.Matches(new Regex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant), correlation);
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
}
