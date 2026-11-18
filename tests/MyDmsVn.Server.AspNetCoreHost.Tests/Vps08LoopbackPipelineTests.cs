using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Server.Application.Identity;
using MyDmsVn.Server.HttpHost;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

// Real loopback Kestrel+forwarded middleware+native API admission, SQL-free.
// Unit-level option checks alone cannot prove headers are removed before
// CoreApiBoundary.CheckTransport sees the reconstructed HTTPS request.
public sealed class Vps08LoopbackPipelineTests
{
    private const string Token = "ProxyCredential_ABC123xyz456ABC123xyz456AB";
    private const string Origin = "https://app.example.test";
    private static readonly LoopbackProxySettings Settings =
        new("app.example.test", Token);

    private static async Task<(WebApplication app, string url)> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.Configure<ForwardedHeadersOptions>(
            LoopbackProxyBoundary.ConfigureForwardedHeaders);
        builder.Services.AddSingleton(new WebHostOptions(
            publicOrigin: Origin, issuer: Origin, audience: "vps08-pipeline",
            activeKeyId: "test-key",
            validationKeys: new Dictionary<string, byte[]>
            {
                ["test-key"] = Enumerable.Repeat((byte)0x5A, 32).ToArray()
            }));
        builder.Services.AddScoped<RequestIdentityAdmission>();
        builder.Services.AddSingleton<ISecurityAuditReadiness, CoreSecurityAuditReadiness>();

        var app = builder.Build();
        app.Use((context, next) =>
            LoopbackProxyBoundary.AdmitAsync(context, () => next(context), Settings));
        app.UseForwardedHeaders();
        app.Use(async (context, next) =>
        {
            context.Request.Headers.Remove("X-Original-For");
            context.Request.Headers.Remove("X-Original-Proto");
            await next(context);
        });
        app.UseRouting();
        app.Use((context, next) =>
            CoreApiBoundary.HandleAsync(context, () => next(context)));
        app.MapGet("/api/v1/health", (HttpContext context) =>
            $"{context.Request.Scheme}|{context.Connection.RemoteIpAddress}|"
            + $"{context.Request.Headers.ContainsKey("X-Forwarded-For")}|"
            + $"{context.Request.Headers.ContainsKey("X-Forwarded-Proto")}|"
            + $"{context.Request.Headers.ContainsKey("X-Original-For")}|"
            + $"{context.Request.Headers.ContainsKey("X-MyDmsVn-Proxy-Token")}");
        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses;
        return (app, Assert.Single(addresses!));
    }

    private static HttpRequestMessage Request(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url + "/api/v1/health");
        request.Headers.Host = "app.example.test";
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "198.51.100.17");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.TryAddWithoutValidation("X-MyDmsVn-Proxy-Token", Token);
        return request;
    }

    [Fact]
    public async Task AuthenticatedNginxHeadersProduceHttpsAdmittedJsonWithoutForwardedOrCredentialLeak()
    {
        var (app, url) = await StartAsync();
        await using (app)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                using var request = Request(url);
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("https|198.51.100.17|False|False|False|False",
                    await response.Content.ReadAsStringAsync());
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            }
            finally
            {
                await app.StopAsync();
            }
        }
    }

    [Theory]
    [InlineData("missing-secret")]
    [InlineData("spoofed-host")]
    [InlineData("forwarded-chain")]
    [InlineData("forwarded-host")]
    [InlineData("wrong-proto")]
    public async Task ActualKestrelRejectsUntrustedClientForwardingBeforeApi(string mutation)
    {
        var (app, url) = await StartAsync();
        await using (app)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                using var request = Request(url);
                switch (mutation)
                {
                    case "missing-secret":
                        request.Headers.Remove("X-MyDmsVn-Proxy-Token");
                        break;
                    case "spoofed-host":
                        request.Headers.Host = "untrusted.example.test";
                        break;
                    case "forwarded-chain":
                        request.Headers.Remove("X-Forwarded-For");
                        request.Headers.TryAddWithoutValidation("X-Forwarded-For",
                            "198.51.100.17, 192.0.2.1");
                        break;
                    case "forwarded-host":
                        request.Headers.TryAddWithoutValidation("X-Forwarded-Host",
                            "attacker.test");
                        break;
                    case "wrong-proto":
                        request.Headers.Remove("X-Forwarded-Proto");
                        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");
                        break;
                }
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            }
            finally
            {
                await app.StopAsync();
            }
        }
    }
}
