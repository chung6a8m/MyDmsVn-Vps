using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using MyDmsVn.Server.HttpHost;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps08LoopbackProxyTests
{
    private const string Token = "ProxyCredential_ABC123xyz456ABC123xyz456AB";
    private static readonly LoopbackProxySettings Settings =
        new("app.example.test", Token);

    private static DefaultHttpContext Request()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("app.example.test");
        context.Request.Path = "/api/v1/health";
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Connection.LocalIpAddress = IPAddress.Loopback;
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.17";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-MyDmsVn-Proxy-Token"] = Token;
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task ValidAuthenticatedLoopbackProxyStripsSecretBeforeDispatch()
    {
        var c = Request();
        var reached = false;
        await LoopbackProxyBoundary.AdmitAsync(c, () =>
        {
            reached = true;
            Assert.False(c.Request.Headers.ContainsKey("X-MyDmsVn-Proxy-Token"));
            Assert.Equal("https", c.Request.Headers["X-Forwarded-Proto"].ToString());
            return Task.CompletedTask;
        }, Settings);
        Assert.True(reached);
    }

    [Theory]
    [InlineData("remote")]
    [InlineData("nonloopback-listener")]
    [InlineData("direct-https")]
    [InlineData("wrong-host")]
    [InlineData("wrong-secret")]
    [InlineData("missing-secret")]
    [InlineData("proto-http")]
    [InlineData("proto-list")]
    [InlineData("for-chain")]
    [InlineData("for-invalid")]
    [InlineData("spoof-host")]
    [InlineData("spoof-prefix")]
    [InlineData("spoof-original")]
    [InlineData("spoof-forwarded")]
    [InlineData("spoof-real-ip")]
    public async Task RejectsUntrustedProxyOrAmbiguousForwardingWithoutDispatch(string mutation)
    {
        var c = Request();
        switch (mutation)
        {
            case "remote": c.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1"); break;
            case "nonloopback-listener": c.Connection.LocalIpAddress = IPAddress.Any; break;
            case "direct-https": c.Request.Scheme = "https"; break;
            case "wrong-host": c.Request.Host = new HostString("evil.example.test"); break;
            case "wrong-secret": c.Request.Headers["X-MyDmsVn-Proxy-Token"] = new string('Q', 40); break;
            case "missing-secret": c.Request.Headers.Remove("X-MyDmsVn-Proxy-Token"); break;
            case "proto-http": c.Request.Headers["X-Forwarded-Proto"] = "http"; break;
            case "proto-list": c.Request.Headers["X-Forwarded-Proto"] = "https,https"; break;
            case "for-chain": c.Request.Headers["X-Forwarded-For"] = "198.51.100.17, 192.0.2.1"; break;
            case "for-invalid": c.Request.Headers["X-Forwarded-For"] = "invalid"; break;
            case "spoof-host": c.Request.Headers["X-Forwarded-Host"] = "evil.example.test"; break;
            case "spoof-prefix": c.Request.Headers["X-Forwarded-Prefix"] = "/"; break;
            case "spoof-original": c.Request.Headers["X-Original-Proto"] = "https"; break;
            case "spoof-forwarded": c.Request.Headers["Forwarded"] = "for=127.0.0.1"; break;
            case "spoof-real-ip": c.Request.Headers["X-Real-IP"] = "192.0.2.1"; break;
        }
        var reached = false;
        await LoopbackProxyBoundary.AdmitAsync(c, () =>
        {
            reached = true;
            return Task.CompletedTask;
        }, Settings);
        Assert.False(reached);
        Assert.Equal(400, c.Response.StatusCode);
        Assert.Equal("no-store", c.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void KnownProxyListIsOnlyIpv4LoopbackAndForwardsOnlyForAndProto()
    {
        var options = new ForwardedHeadersOptions();
        LoopbackProxyBoundary.ConfigureForwardedHeaders(options);
        Assert.Single(options.KnownProxies);
        Assert.Equal(IPAddress.Loopback, options.KnownProxies[0]);
        Assert.Empty(options.KnownNetworks);
        Assert.Equal(1, options.ForwardLimit);
        Assert.True(options.RequireHeaderSymmetry);
        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
    }

    [Fact]
    public void AbsentModeDoesNotChangeExistingDirectHttps()
    {
        var values = new Dictionary<string,string?>();
        Assert.Null(LoopbackProxyBoundary.ReadSettings(
            key => values.GetValueOrDefault(key), "https://app.example.test"));
    }

    [Theory]
    [InlineData("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true")]
    [InlineData("MYDMSVN_VPS08_PROXY_MODE", "unknown")]
    [InlineData("ASPNETCORE_URLS", "http://0.0.0.0:5080")]
    [InlineData("MYDMSVN_VPS08_PROXY_TOKEN", "short")]
    public void ProxyStartupRefusesUnsafeSettings(string setting, string value)
    {
        var values = new Dictionary<string, string?>
        {
            ["MYDMSVN_VPS08_PROXY_MODE"] = "loopback-nginx-v1",
            ["ASPNETCORE_URLS"] = LoopbackProxyBoundary.LoopbackUrl,
            ["MYDMSVN_VPS08_PROXY_TOKEN"] = Token
        };
        values[setting] = value;
        Assert.Throws<InvalidOperationException>(() => LoopbackProxyBoundary.ReadSettings(
            key => values.GetValueOrDefault(key), "https://app.example.test"));
    }

    [Fact]
    public void BoundedLinuxProxyConfigAdmitsOnlyExactModeAndListener()
    {
        if (!OperatingSystem.IsLinux()) return;
        var values = new Dictionary<string, string?>
        {
            ["MYDMSVN_VPS08_PROXY_MODE"] = "loopback-nginx-v1",
            ["ASPNETCORE_URLS"] = LoopbackProxyBoundary.LoopbackUrl,
            ["MYDMSVN_VPS08_PROXY_TOKEN"] = Token
        };
        var settings = LoopbackProxyBoundary.ReadSettings(
            key => values.GetValueOrDefault(key), "https://app.example.test");
        Assert.Equal("app.example.test", settings!.OriginAuthority);
    }
}
