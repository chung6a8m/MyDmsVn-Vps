using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Server.HttpHost;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps10OpenApiAndFeatureGateTests
{
    [Theory]
    [InlineData("Development", 200)]
    [InlineData("Production", 404)]
    public async Task Native_document_is_partial_and_development_only_without_SQL(string environment, int status)
    {
        var builder = NewBuilder(environment);
        builder.Services.AddVps10NativeApi();
        await using var app = builder.Build();
        Net10RouteTable.Map(app);
        NativeApiRegistration.MapNativeOpenApi(app);
        Net10RouteTable.MapFallback(app);
        await app.StartAsync();
        using var client = Client(app);
        using var response = await client.GetAsync("/openapi/v1-native.json");
        Assert.Equal(status, (int)response.StatusCode);
        if (status != 200) return;

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var document = json.RootElement;
        var paths = document.GetProperty("paths");
        Assert.Equal(new[]
        {
            "/api/v1/catalog/customers/lookup",
            "/api/v1/catalog/employees/lookup",
            "/api/v1/catalog/products/lookup",
            "/api/v1/catalog/warehouses/lookup"
        }, paths.EnumerateObject().Select(p => p.Name).OrderBy(p => p, StringComparer.Ordinal).ToArray());
        foreach (var route in paths.EnumerateObject())
        {
            var operation = route.Value.GetProperty("get");
            Assert.False(operation.TryGetProperty("requestBody", out _));
            var parameters = operation.GetProperty("parameters").EnumerateArray().ToArray();
            Assert.Equal(2, parameters.Length);
            var limit = Assert.Single(parameters, p => p.GetProperty("name").GetString() == "limit");
            Assert.Equal("query", limit.GetProperty("in").GetString());
            Assert.True(limit.GetProperty("required").GetBoolean());
            var limitSchema = Resolve(document, limit.GetProperty("schema"));
            Assert.Equal("integer", limitSchema.GetProperty("type").GetString());
            Assert.Equal(1m, limitSchema.GetProperty("minimum").GetDecimal());
            Assert.Equal(200m, limitSchema.GetProperty("maximum").GetDecimal());
            var search = Assert.Single(parameters, p => p.GetProperty("name").GetString() == "search");
            Assert.False(search.TryGetProperty("required", out var required) && required.GetBoolean());
            Assert.Equal(256, Resolve(document, search.GetProperty("schema")).GetProperty("maxLength").GetInt32());
            var responses = operation.GetProperty("responses");
            var success = Resolve(document, ResponseSchema(responses, "200"));
            var data = Resolve(document, success.GetProperty("properties").GetProperty("data"));
            Assert.Equal("array", data.GetProperty("type").GetString());
            Assert.False(success.GetProperty("properties").TryGetProperty("error", out _));
            var error = Resolve(document, ResponseSchema(responses, "400"));
            Assert.True(error.GetProperty("properties").TryGetProperty("error", out _));
            Assert.False(error.GetProperty("properties").TryGetProperty("data", out _));
            Assert.True(responses.TryGetProperty("401", out _));
            Assert.True(responses.TryGetProperty("403", out _));
            Assert.True(responses.TryGetProperty("413", out _));
        }
    }

    // Exercise the real routing/MVC middleware sequence over Kestrel; only the
    // upstream admission identity is substituted. No test route ships in the host.
    [Theory]
    [InlineData(false, 404, "NotFound")]
    [InlineData(true, 400, "ValidationError")]
    public async Task Desktop_metadata_gate_precedes_MVC_body_binding(bool enabled, int status, string code)
    {
        var builder = NewBuilder("Production");
        builder.Services.AddVps10NativeApi();
        builder.Services.AddControllers().AddApplicationPart(typeof(Vps10DesktopGateProbeController).Assembly);
        builder.Services.AddScoped<RequestIdentityAdmission>();
        builder.Services.AddSingleton(new WebHostOptions("https://localhost", "https://localhost", "probe", "probe",
            new Dictionary<string, byte[]> { ["probe"] = new byte[32] }, desktopApiEnabled: enabled));
        await using var app = builder.Build();
        app.UseRouting();
        app.Use((context, next) =>
        {
            context.RequestServices.GetRequiredService<RequestIdentityAdmission>().BeginCorrelation("gate_probe");
            return next();
        });
        app.Use((context, next) => NativeApiRegistration.AdmitDesktopFeatureAsync(context, next));
        app.MapControllers();
        await app.StartAsync();
        using var client = Client(app);
        using var content = new StringContent("{", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/v1/vps10-test/desktop-gate", content);
        Assert.Equal(status, (int)response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static WebApplicationBuilder NewBuilder(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        return builder;
    }

    private static HttpClient Client(WebApplication app) => new()
    {
        BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single())
    };

    private static JsonElement ResponseSchema(JsonElement responses, string code) =>
        responses.GetProperty(code).GetProperty("content").GetProperty("application/json").GetProperty("schema");

    private static JsonElement Resolve(JsonElement document, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference)) return schema;
        var pointer = reference.GetString()!;
        Assert.StartsWith("#/components/schemas/", pointer);
        return document.GetProperty("components").GetProperty("schemas").GetProperty(
            pointer.Split('/').Last().Replace("~1", "/").Replace("~0", "~"));
    }
}

[ApiController]
[Route("api/v1/vps10-test/desktop-gate")]
[V1DesktopApiRequired]
public sealed class Vps10DesktopGateProbeController : ControllerBase
{
    [HttpPost]
    public IActionResult Post([FromBody] Vps10DesktopGatePayload payload) => Ok(payload);
}

public sealed record Vps10DesktopGatePayload(string Value);
