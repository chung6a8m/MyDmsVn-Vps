using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Contracts;
using MyDmsVn.Server.Application.Identity;
using MyDmsVn.Server.HttpHost;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

/// <summary>
/// SQL-free, compiled 57-route admission/error/headers matrix. This is NOT
/// the complete authenticated success/failure SQL/response differential.
/// </summary>
public sealed class Vps05cRouteWireCompatibilityTests
{
    private const string Correlation = "vps05c_wire";
    private const string Auth401 =
        "{\"error\":{\"code\":\"Auth.Unauthorized\",\"message\":\"Authentication is required.\",\"details\":[],\"correlationId\":\"vps05c_wire\"}}";
    private const string Transport400 =
        "{\"error\":{\"code\":\"Request.InvalidTransport\",\"message\":\"The request transport is invalid.\",\"details\":[],\"correlationId\":\"vps05c_wire\"}}";

    public static TheoryData<string, string> AllRoutes
    {
        get
        {
            using var inventory = JsonDocument.Parse(Net10RouteTable.ToInventoryJson());
            var cases = new TheoryData<string, string>();
            foreach (var route in inventory.RootElement.EnumerateArray())
                cases.Add(route.GetProperty("httpMethod").GetString()!, route.GetProperty("path").GetString()!);
            return cases;
        }
    }

    private static bool Anonymous(string method, string path) =>
        method == "GET" && path == "/api/v1/health" ||
        method == "POST" && (path == "/api/v1/auth/login" ||
            path == "/api/v1/auth/refresh" || path == "/api/v1/auth/logout");

    private static DefaultHttpContext NewRequest(string method, string path)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new WebHostOptions("https://localhost", "https://localhost", "vps05c-test",
            "test-key", new Dictionary<string, byte[]> { ["test-key"] = Enumerable.Repeat((byte)0x5A, 32).ToArray() }));
        services.AddScoped<RequestIdentityAdmission>();
        services.AddSingleton<ISecurityAuditReadiness, CoreSecurityAuditReadiness>();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        context.Request.Method = method;
        context.Request.Path = new PathString(path);
        context.Request.Headers["X-Correlation-ID"] = Correlation;
        if (method is "POST" or "PUT")
            context.Request.Headers.Origin = "https://localhost";
        if (method == "POST" && Anonymous(method, path))
            context.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        // Deliberately malformed body: an unauthenticated mutation must
        // fail admission BEFORE any endpoint-specific strict JSON decoder.
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"invalid\":"));
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string Body(DefaultHttpContext context) =>
        Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    private static void Headers(DefaultHttpContext context)
    {
        Assert.Equal(Correlation, context.Response.Headers["X-Correlation-ID"].ToString());
        Assert.Equal("no-store", context.Response.Headers["Cache-Control"].ToString());
        Assert.Equal("no-cache", context.Response.Headers["Pragma"].ToString());
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.Equal("max-age=31536000", context.Response.Headers["Strict-Transport-Security"].ToString());
    }

    [Fact]
    public void Net10Compiled57SignaturesEqualIndependentFrozenVps01AndVps02Inventory()
    {
        using var registered = JsonDocument.Parse(Net10RouteTable.ToInventoryJson());
        var actual = registered.RootElement.EnumerateArray()
            .Select(route => route.GetProperty("httpMethod").GetString() + " " + route.GetProperty("path").GetString())
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var root = LocateRoot();
        var expected = new List<string>();
        foreach (var name in new[] { "vps-01-route-inventory.json", "vps-02-route-additions.json" })
        {
            using var source = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "docs", "contracts", name)));
            expected.AddRange(source.RootElement.GetProperty("endpoints").EnumerateArray()
                .Select(route => route.GetProperty("httpMethod").GetString() + " " + route.GetProperty("path").GetString()));
        }
        Assert.Equal(57, actual.Length);
        Assert.Equal(57, expected.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(expected.OrderBy(s => s, StringComparer.Ordinal), actual);
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryCompiledRoutePreservesAnonymousAdmissionOrLegacy401ErrorAndHeaders(string method, string path)
    {
        var context = NewRequest(method, path);
        var invoked = false;
        await CoreApiBoundary.HandleAsync(context, () =>
        {
            invoked = true;
            var identity = context.RequestServices.GetRequiredService<RequestIdentityAdmission>();
            Assert.False(identity.Current.IsAuthenticated);
            return Task.CompletedTask;
        });
        Headers(context);
        if (Anonymous(method, path))
        {
            Assert.True(invoked, method + " " + path);
            return;
        }

        Assert.False(invoked, method + " " + path);
        Assert.Equal(401, context.Response.StatusCode);
        Assert.Equal("Bearer", context.Response.Headers["WWW-Authenticate"].ToString());
        Assert.StartsWith("application/json", context.Response.ContentType!);
        Assert.Equal(Auth401, Body(context)); // pinned, byte-exact legacy error
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryCompiledRoutePreservesLegacy400TransportErrorBeforeDispatch(string method, string path)
    {
        var context = NewRequest(method, path);
        context.Request.Scheme = "http";
        var invoked = false;
        await CoreApiBoundary.HandleAsync(context, () =>
        {
            invoked = true;
            return Task.CompletedTask;
        });
        Assert.False(invoked, method + " " + path);
        Headers(context);
        Assert.Equal(400, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType!);
        Assert.Equal(Transport400, Body(context));
    }

    [Fact]
    public void Net10SharedDtoDateMetadataIsStrictAndDoesNotDependOnJsonNet()
    {
        var date = new DateTime(2026, 11, 17, 23, 20, 0, DateTimeKind.Utc);
        var sale = new SaveSalesReceiptRequest(date, 1, 2, 3, null,
            new[] { new SaveSalesReceiptLineRequest(0, 7, 1.2500m, 19.9900m, false) });
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(sale));
        Assert.Equal("2026-11-17", json.RootElement.GetProperty("SaleDate").GetString());

        var options = new JsonSerializerOptions();
        options.Converters.Add(new CalendarDateJsonConverter());
        Assert.Equal("\"2026-11-17\"", JsonSerializer.Serialize(date, options));
        var read = JsonSerializer.Deserialize<DateTime>("\"2026-11-17\"", options);
        Assert.Equal(DateTimeKind.Unspecified, read.Kind);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTime>("\"2026-11-17T00:00:00Z\"", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTime>("\"2026-02-30\"", options));
    }

    private static string LocateRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "docs", "contracts", "vps-01-route-inventory.json")))
                return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Frozen independent route inventory not found.");
    }
}
