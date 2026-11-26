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
            "/api/v1/auth/capabilities",
            "/api/v1/auth/login",
            "/api/v1/auth/logout",
            "/api/v1/auth/me",
            "/api/v1/auth/refresh",
            "/api/v1/catalog/customers",
            "/api/v1/catalog/customers/lookup",
            "/api/v1/catalog/customers/{id}",
            "/api/v1/catalog/customers/{id}/active",
            "/api/v1/catalog/employees",
            "/api/v1/catalog/employees/lookup",
            "/api/v1/catalog/employees/{id}",
            "/api/v1/catalog/employees/{id}/active",
            "/api/v1/catalog/products",
            "/api/v1/catalog/products/lookup",
            "/api/v1/catalog/products/{id}",
            "/api/v1/catalog/products/{id}/active",
            "/api/v1/catalog/warehouses",
            "/api/v1/catalog/warehouses/lookup",
            "/api/v1/catalog/warehouses/{id}",
            "/api/v1/catalog/warehouses/{id}/active",
            "/api/v1/foundation/status",
            "/api/v1/goods-receipts",
            "/api/v1/goods-receipts/{receiptId}",
            "/api/v1/goods-receipts/{receiptId}/draft",
            "/api/v1/goods-receipts/{receiptId}/post",
            "/api/v1/health",
            "/api/v1/inventory/balances",
            "/api/v1/inventory/stock-card",
            "/api/v1/sales-receipts",
            "/api/v1/sales-receipts/{saleId}",
            "/api/v1/sales-receipts/{saleId}/draft",
            "/api/v1/sales-receipts/{saleId}/post",
            "/api/v1/security/import/apply",
            "/api/v1/security/import/preview",
            "/api/v1/security/mutations",
            "/api/v1/security/permissions/catalog",
            "/api/v1/security/permissions/check",
            "/api/v1/security/roles",
            "/api/v1/security/users"
        }, paths.EnumerateObject().Select(p => p.Name).OrderBy(p => p, StringComparer.Ordinal).ToArray());
        var login = paths.GetProperty("/api/v1/auth/login").GetProperty("post");
        var loginSchema = Resolve(document, login.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema"));
        Assert.Equal(new[] { "password", "username" }, loginSchema.GetProperty("required").EnumerateArray()
            .Select(p => p.GetString()).OrderBy(p => p, StringComparer.Ordinal).ToArray());
        Assert.Equal("string", Resolve(document, loginSchema.GetProperty("properties").GetProperty("password")).GetProperty("type").GetString());
        Assert.False(paths.GetProperty("/api/v1/auth/refresh").GetProperty("post").TryGetProperty("requestBody", out _));
        Assert.False(paths.GetProperty("/api/v1/auth/logout").GetProperty("post").TryGetProperty("requestBody", out _));
        Assert.True(paths.GetProperty("/api/v1/health").GetProperty("get").GetProperty("responses").TryGetProperty("503", out _));
        foreach (var route in paths.EnumerateObject().Where(p => p.Name.StartsWith("/api/v1/catalog/", StringComparison.Ordinal) && p.Name.EndsWith("/lookup", StringComparison.Ordinal)))
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
            var dataType = data.GetProperty("type");
            // OpenAPI 3.1 permits a string or an array of type names, but the
            // lookup contract does not permit null or another type here.
            var dataTypes = dataType.ValueKind == JsonValueKind.Array
                ? dataType.EnumerateArray().Select(type => type.GetString()).ToArray()
                : new[] { dataType.GetString() };
            Assert.Equal(new[] { "array" }, dataTypes);
            Assert.Contains("data", success.GetProperty("required").EnumerateArray().Select(p => p.GetString()));
            Assert.False(success.GetProperty("properties").TryGetProperty("error", out _));
            var error = Resolve(document, ResponseSchema(responses, "400"));
            Assert.True(error.GetProperty("properties").TryGetProperty("error", out _));
            Assert.False(error.GetProperty("properties").TryGetProperty("data", out _));
            Assert.True(responses.TryGetProperty("401", out _));
            Assert.True(responses.TryGetProperty("403", out _));
            Assert.True(responses.TryGetProperty("413", out _));
        }
        // All four CRUD surfaces are native; the lookup remains independent.
        foreach (var entity in new[] { "customers", "employees", "products", "warehouses" })
        {
            var collection = paths.GetProperty("/api/v1/catalog/" + entity);
            var catalogDetail = paths.GetProperty("/api/v1/catalog/" + entity + "/{id}");
            var active = paths.GetProperty("/api/v1/catalog/" + entity + "/{id}/active");
            Assert.False(collection.GetProperty("get").TryGetProperty("requestBody", out _));
            Assert.Equal(4, collection.GetProperty("get").GetProperty("parameters").GetArrayLength());
            Assert.False(catalogDetail.GetProperty("get").TryGetProperty("requestBody", out _));
            foreach (var mutation in new[] { collection.GetProperty("post"), catalogDetail.GetProperty("put"), active.GetProperty("put") })
            {
                Assert.True(mutation.TryGetProperty("requestBody", out _));
                Assert.True(mutation.GetProperty("responses").TryGetProperty("409", out _));
            }
        }
        foreach (var (path, fields) in new[]
        {
            ("/api/v1/security/mutations", new[] { "expectedRevision", "expectedRelatedRevision" }),
            ("/api/v1/security/import/apply", new[] { "expectedTargetRevision" })
        })
        {
            var postOperation = paths.GetProperty(path).GetProperty("post");
            var request = Resolve(document, postOperation.GetProperty("requestBody").GetProperty("content")
                .GetProperty("application/json").GetProperty("schema"));
            var requestProperties = request.GetProperty("properties");
            Assert.False(requestProperties.TryGetProperty("actorUserId", out _));
            var required = request.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToArray();
            if (path.EndsWith("/mutations", StringComparison.Ordinal))
            {
                foreach (var optional in new[] { "targetId", "relatedId" })
                {
                    Assert.DoesNotContain(optional, required);
                    var optionalIdSchema = Resolve(document, requestProperties.GetProperty(optional));
                    Assert.Equal("int32", optionalIdSchema.GetProperty("format").GetString());
                    var idTypes = optionalIdSchema.GetProperty("type").EnumerateArray().Select(x => x.GetString()).ToArray();
                    Assert.Contains("integer", idTypes);
                    Assert.Contains("null", idTypes);
                }
            }
            else
            {
                Assert.Contains("expectedTargetRevision", required);
                Assert.Contains("targetId", required);
            }
            foreach (var field in fields)
            {
                var revision = Resolve(document, requestProperties.GetProperty(field));
                Assert.Equal("int64", revision.GetProperty("format").GetString());
                var type = revision.GetProperty("type");
                var types = type.ValueKind == JsonValueKind.Array
                    ? type.EnumerateArray().Select(x => x.GetString()).ToArray()
                    : new[] { type.GetString() };
                Assert.Contains("integer", types);
                Assert.DoesNotContain("string", types);
            }
        }
        // WEB3 reads and receipt writes are native.
        var receiptListPath = paths.GetProperty("/api/v1/goods-receipts");
        var receiptCreate = receiptListPath.GetProperty("post");
        var receiptUpdate = paths.GetProperty("/api/v1/goods-receipts/{receiptId}/draft").GetProperty("put");
        var receiptPost = paths.GetProperty("/api/v1/goods-receipts/{receiptId}/post").GetProperty("post");
        foreach (var mutation in new[] { receiptCreate, receiptUpdate, receiptPost })
        {
            Assert.True(mutation.TryGetProperty("requestBody", out _));
            Assert.True(mutation.GetProperty("responses").TryGetProperty("409", out _));
            var body = Resolve(document, mutation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema"));
            var fields = body.GetProperty("properties");
            if (fields.TryGetProperty("lines", out var lines))
            {
                var line = Resolve(document, Resolve(document, lines).GetProperty("items"));
                foreach (var amount in new[] { "quantity", "unitCost" })
                    Assert.Equal("string", Resolve(document, line.GetProperty("properties").GetProperty(amount)).GetProperty("type").GetString());
            }
            if (fields.TryGetProperty("expectedVersion", out var versionProperty))
            {
                var version = Resolve(document, versionProperty);
                Assert.Equal("string", version.GetProperty("type").GetString());
                Assert.Equal(12, version.GetProperty("minLength").GetInt32());
                Assert.Equal(12, version.GetProperty("maxLength").GetInt32());
            }
        }
        var receiptDetail = paths.GetProperty("/api/v1/goods-receipts/{receiptId}").GetProperty("get");
        var receiptId = Assert.Single(receiptDetail.GetProperty("parameters").EnumerateArray());
        Assert.Equal("receiptId", receiptId.GetProperty("name").GetString());
        Assert.Equal("string", Resolve(document, receiptId.GetProperty("schema")).GetProperty("type").GetString());
        var receiptData = RequiredObjectData(document, Resolve(document, ResponseSchema(receiptDetail.GetProperty("responses"), "200")));
        var receiptProperties = receiptData.GetProperty("properties");
        foreach (var field in new[] { "note", "postedAtUtc", "warehouseDisplayName", "employeeDisplayName", "lines", "receiptId" })
            Assert.Contains(field, receiptData.GetProperty("required").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal("date", Resolve(document, receiptProperties.GetProperty("receiptDate")).GetProperty("format").GetString());
        foreach (var field in new[] { "note", "postedAtUtc", "warehouseDisplayName", "employeeDisplayName" })
            AssertNullableString(Resolve(document, receiptProperties.GetProperty(field)));
        var receiptLine = Resolve(document, Resolve(document, receiptProperties.GetProperty("lines")).GetProperty("items"));
        var receiptLineProperties = receiptLine.GetProperty("properties");
        AssertNullableString(Resolve(document, receiptLineProperties.GetProperty("productDisplayName")));
        Assert.Contains("productDisplayName", receiptLine.GetProperty("required").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(@"^[0-9]+\.[0-9]{4}$", Resolve(document, receiptLineProperties.GetProperty("quantity")).GetProperty("pattern").GetString());
        var receiptList = receiptListPath.GetProperty("get");
        var receiptParams = receiptList.GetProperty("parameters").EnumerateArray().ToArray();
        Assert.Equal(5, receiptParams.Length);
        foreach (var name in new[] { "pageNumber", "pageSize" })
            Assert.True(Assert.Single(receiptParams, p => p.GetProperty("name").GetString() == name).GetProperty("required").GetBoolean());
        var pageNumberSchema = Resolve(document, Assert.Single(receiptParams, p => p.GetProperty("name").GetString() == "pageNumber").GetProperty("schema"));
        var pageSizeSchema = Resolve(document, Assert.Single(receiptParams, p => p.GetProperty("name").GetString() == "pageSize").GetProperty("schema"));
        Assert.Equal(2147483647m, pageNumberSchema.GetProperty("maximum").GetDecimal());
        Assert.Equal(200m, pageSizeSchema.GetProperty("maximum").GetDecimal());
        Assert.Contains("Receipt detail requires canonical positive Int64 path text; no query or body.", document.GetProperty("info").GetProperty("description").GetString());
        var balances = paths.GetProperty("/api/v1/inventory/balances").GetProperty("get");
        var balanceData = RequiredObjectData(document, Resolve(document, ResponseSchema(balances.GetProperty("responses"), "200")));
        Assert.Contains("totalCount", balanceData.GetProperty("required").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal("string", Resolve(document, balanceData.GetProperty("properties").GetProperty("totalCount")).GetProperty("type").GetString());
        var balanceRow = Resolve(document, Resolve(document, balanceData.GetProperty("properties").GetProperty("items")).GetProperty("items"));
        Assert.Contains("averageUnitCost", balanceRow.GetProperty("required").EnumerateArray().Select(p => p.GetString()));
        var balanceProperties = balanceRow.GetProperty("properties");
        var averageSchema = Resolve(document, balanceProperties.GetProperty("averageUnitCost"));
        AssertNullableString(averageSchema);
        Assert.Equal(@"^[0-9]+\.[0-9]{8}$", averageSchema.GetProperty("pattern").GetString());
        Assert.Equal(@"^[0-9]+\.[0-9]{4}$", Resolve(document, balanceProperties.GetProperty("inventoryValue")).GetProperty("pattern").GetString());
        var card = paths.GetProperty("/api/v1/inventory/stock-card").GetProperty("get");
        var cardParams = card.GetProperty("parameters").EnumerateArray().ToArray();
        Assert.Equal(6, cardParams.Length);
        foreach (var name in new[] { "pageNumber", "pageSize", "warehouseId", "productId" })
            Assert.True(Assert.Single(cardParams, p => p.GetProperty("name").GetString() == name).GetProperty("required").GetBoolean());
        foreach (var name in new[] { "fromUtc", "toUtc" })
        {
            var parameter = Assert.Single(cardParams, p => p.GetProperty("name").GetString() == name);
            Assert.Equal("date-time", Resolve(document, parameter.GetProperty("schema")).GetProperty("format").GetString());
        }
        var list = paths.GetProperty("/api/v1/sales-receipts").GetProperty("get");
        var detail = paths.GetProperty("/api/v1/sales-receipts/{saleId}").GetProperty("get");
        var create = paths.GetProperty("/api/v1/sales-receipts").GetProperty("post");
        var update = paths.GetProperty("/api/v1/sales-receipts/{saleId}/draft").GetProperty("put");
        var post = paths.GetProperty("/api/v1/sales-receipts/{saleId}/post").GetProperty("post");
        foreach (var mutation in new[] { create, update, post })
        {
            Assert.True(mutation.TryGetProperty("requestBody", out _));
            Assert.True(mutation.GetProperty("responses").TryGetProperty("409", out _));
        }
        Assert.False(list.TryGetProperty("requestBody", out _));
        Assert.False(detail.TryGetProperty("requestBody", out _));
        var listParameters = list.GetProperty("parameters").EnumerateArray().ToArray();
        Assert.Equal(10, listParameters.Length);
        var size = Assert.Single(listParameters, p => p.GetProperty("name").GetString() == "pageSize");
        Assert.Equal(1m, Resolve(document, size.GetProperty("schema")).GetProperty("minimum").GetDecimal());
        Assert.Equal(200m, Resolve(document, size.GetProperty("schema")).GetProperty("maximum").GetDecimal());
        var id = Assert.Single(detail.GetProperty("parameters").EnumerateArray());
        Assert.Equal("saleId", id.GetProperty("name").GetString());
        Assert.Equal("path", id.GetProperty("in").GetString());
        var idSchema = Resolve(document, id.GetProperty("schema"));
        Assert.Equal("string", idSchema.GetProperty("type").GetString());
        Assert.Equal("^[1-9][0-9]{0,18}$", idSchema.GetProperty("pattern").GetString());
        var detailEnvelope = Resolve(document, ResponseSchema(detail.GetProperty("responses"), "200"));
        var detailData = RequiredObjectData(document, detailEnvelope);
        var properties = detailData.GetProperty("properties");
        var requiredDetail = detailData.GetProperty("required").EnumerateArray().Select(p => p.GetString()).ToArray();
        Assert.Contains("saleId", requiredDetail);
        Assert.Contains("lines", requiredDetail);
        Assert.DoesNotContain("note", requiredDetail);
        Assert.DoesNotContain("postedAtUtc", requiredDetail);
        Assert.DoesNotContain("totalCost", requiredDetail);
        Assert.Equal("string", Resolve(document, properties.GetProperty("note")).GetProperty("type").GetString());
        Assert.Equal("date-time", Resolve(document, properties.GetProperty("postedAtUtc")).GetProperty("format").GetString());
        Assert.Equal("string", Resolve(document, properties.GetProperty("saleId")).GetProperty("type").GetString());
        Assert.Equal("string", Resolve(document, properties.GetProperty("totalAmount")).GetProperty("type").GetString());
        Assert.Equal("date", Resolve(document, properties.GetProperty("saleDate")).GetProperty("format").GetString());
        Assert.True(detail.GetProperty("responses").TryGetProperty("404", out _));
        var listEnvelope = Resolve(document, ResponseSchema(list.GetProperty("responses"), "200"));
        var listData = RequiredObjectData(document, listEnvelope);
        Assert.Equal(new[] { "items" }, listData.GetProperty("required").EnumerateArray().Select(p => p.GetString()).ToArray());
        var listProperties = listData.GetProperty("properties");
        Assert.Equal("string", Resolve(document, listProperties.GetProperty("nextCursor")).GetProperty("type").GetString());
        Assert.Equal("array", Resolve(document, listProperties.GetProperty("items")).GetProperty("type").GetString());
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

    private static JsonElement RequiredObjectData(JsonElement document, JsonElement envelope)
    {
        Assert.Contains("data", envelope.GetProperty("required").EnumerateArray().Select(p => p.GetString()));
        var data = envelope.GetProperty("properties").GetProperty("data");
        Assert.False(data.TryGetProperty("oneOf", out _), data.GetRawText());
        var resolved = Resolve(document, data);
        Assert.Equal("object", resolved.GetProperty("type").GetString());
        return resolved;
    }

    private static void AssertNullableString(JsonElement schema)
    {
        var type = schema.GetProperty("type");
        Assert.Equal(JsonValueKind.Array, type.ValueKind);
        Assert.Equal(new[] { "null", "string" }, type.EnumerateArray().Select(x => x.GetString()).OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

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
