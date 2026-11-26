using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MyDmsVn.Server.Application.Catalog;
using Xunit;
using LiveFixture = MyDmsVn.Server.AspNetCoreHost.Tests.Vps05cDesktopRouteLiveParityTests.DesktopLiveFixture;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps10CatalogCrudTests
{
    private const string Catalog = "/api/v1/catalog/";

    public static IEnumerable<object[]> CrudRoutes()
    {
        foreach (var (entity, body) in new[]
        {
            ("products", "{\"code\":\"P1\",\"name\":\"Product\",\"unit\":\"kg\"}"),
            ("warehouses", "{\"code\":\"W1\",\"name\":\"Warehouse\"}"),
            ("employees", "{\"code\":\"E1\",\"name\":\"Employee\",\"userId\":1}"),
            ("customers", "{\"code\":\"C1\",\"name\":\"Customer\"}")
        })
        {
            var path = Catalog + entity;
            yield return new object[] { entity, "GET", path, "" };
            yield return new object[] { entity, "GET", path + "/1", "" };
            yield return new object[] { entity, "POST", path, body };
            yield return new object[] { entity, "PUT", path + "/1", body };
            yield return new object[] { entity, "PUT", path + "/1/active", "{\"isActive\":false}" };
        }
    }

    [Theory]
    [MemberData(nameof(CrudRoutes))]
    public async Task Disabled_gate_and_auth_precede_binding(string entity, string verb, string path, string body)
    {
        _ = entity;
        _ = body;
        await using var fixture = await LiveFixture.StartAsync(desktopApiEnabled: false);
        var invalidPath = path + "?unknown=x";
        using var anonymous = await fixture.SendAsync(verb, invalidPath, "{", authenticated: false);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var disabled = await fixture.SendAsync(verb, invalidPath, "{");
        Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);
        using var json = JsonDocument.Parse(await disabled.Content.ReadAsStringAsync());
        Assert.Equal("NotFound", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, fixture.HandlerCalls);
        Assert.Empty(fixture.PermissionChecks);
    }

    [Theory]
    [MemberData(nameof(CrudRoutes))]
    public async Task Every_route_keeps_its_exact_independent_permission(string entity, string verb, string path, string body)
    {
        await using var fixture = await LiveFixture.StartAsync(denyPermissions: true);
        using var reply = await fixture.SendAsync(verb, path, body.Length == 0 ? null : body);
        Assert.Equal(HttpStatusCode.Forbidden, reply.StatusCode);
        Assert.Equal("Catalog." + char.ToUpperInvariant(entity[0]) + entity[1..] + (verb == "GET" ? ".Read" : ".Write"),
            Assert.Single(fixture.PermissionChecks));
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData("GET", "products?pageNumber=0", null, "pageNumber")]
    [InlineData("GET", "customers?pageSize=201", null, "pageSize")]
    [InlineData("GET", "warehouses?pageSize=01", null, "pageSize")]
    [InlineData("GET", "employees?includeInactive=True", null, "includeInactive")]
    [InlineData("GET", "employees?search=%00", null, "search")]
    [InlineData("GET", "products?pageSize=1&%70ageSize=2", null, null)]
    [InlineData("GET", "products?unknown=x", null, null)]
    [InlineData("GET", "products?pageSize=0", "{}", null)]
    [InlineData("GET", "products/01", null, "id")]
    [InlineData("GET", "products/0?extra=x", null, null)]
    [InlineData("PUT", "products/0?extra=x", "{", "id")]
    [InlineData("POST", "products?extra=x", "{", null)]
    [InlineData("POST", "products", "{\"code\":\"P\",\"name\":\"N\"}", "unit")]
    [InlineData("POST", "products", "{\"code\":null,\"name\":\"N\",\"unit\":\"EA\"}", "code")]
    [InlineData("POST", "warehouses", "{\"code\":\"W\",\"name\":\"N\",\"address\":1}", "address")]
    [InlineData("POST", "employees", "{\"code\":\"E\",\"name\":\"N\",\"userId\":\"1\"}", "userId")]
    [InlineData("POST", "employees", "{\"code\":\"E\",\"name\":\"N\",\"userId\":1.0}", "userId")]
    [InlineData("POST", "employees", "{\"code\":\"E\",\"name\":\"N\",\"userId\":0}", "userId")]
    [InlineData("POST", "customers", "{\"code\":\"C\",\"name\":\"N\",\"taxCode\":false}", "taxCode")]
    [InlineData("POST", "customers", "{\"code\":\"C\",\"name\":\"N\",\"isActive\":true}", null)]
    [InlineData("POST", "customers", "{\"code\":\"C\",\"code\":\"D\",\"name\":\"N\"}", null)]
    [InlineData("PUT", "products/1/active", "{}", "isActive")]
    [InlineData("PUT", "warehouses/1/active", "{\"isActive\":\"false\"}", "isActive")]
    [InlineData("PUT", "employees/1/active", "{\"isActive\":null}", "isActive")]
    [InlineData("PUT", "customers/1/active", "{\"IsActive\":false}", null)]
    public async Task Invalid_wire_is_rejected_before_permission_and_handler(string verb, string suffix, string? body, string? field)
    {
        await using var fixture = await LiveFixture.StartAsync(denyPermissions: true);
        using var reply = await fixture.SendAsync(verb, Catalog + suffix, body);
        Assert.Equal(HttpStatusCode.BadRequest, reply.StatusCode);
        using var json = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("ValidationError", error.GetProperty("code").GetString());
        var detail = error.GetProperty("details")[0];
        Assert.Equal(field, detail.TryGetProperty("field", out var value) ? value.GetString() : null);
        Assert.Equal(0, fixture.HandlerCalls);
        Assert.Empty(fixture.PermissionChecks);
    }

    [Theory]
    [InlineData("vi-VN")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public async Task Defaults_and_Unicode_are_invariant(string culture)
    {
        await using var fixture = await LiveFixture.StartAsync(requestCultureName: culture);
        using var list = await fixture.SendAsync("GET", Catalog + "products", null);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var query = Assert.IsType<ListProductsQuery>(fixture.LastRequest).Request;
        Assert.Equal(1, query.PageNumber);
        Assert.Equal(50, query.PageSize);
        Assert.False(query.IncludeInactive);
        Assert.Null(query.Search);
        using var create = await fixture.SendAsync("POST", Catalog + "customers",
            "{\"code\":\"KH\",\"name\":\"Khách hàng Đặng\",\"address\":\"Hải Phòng\",\"phone\":null}");
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var body = Assert.IsType<CreateCustomerCommand>(fixture.LastRequest).Request;
        Assert.Equal("Khách hàng Đặng", body.Name);
        Assert.Equal("Hải Phòng", body.Address);
        Assert.Null(body.Phone);
        Assert.Null(body.TaxCode);
        Assert.Equal(culture, fixture.LastRequestCulture);
    }

    [Fact]
    public async Task Malformed_utf8_is_not_silently_replaced()
    {
        await using var fixture = await LiveFixture.StartAsync();
        var bytes = new List<byte>(Encoding.UTF8.GetBytes("{\"code\":\"C\",\"name\":\""));
        bytes.Add(0xff);
        bytes.AddRange(Encoding.UTF8.GetBytes("\"}"));
        using var reply = await fixture.SendRawAsync("POST", Catalog + "customers", bytes.ToArray(), "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, reply.StatusCode);
        Assert.Equal(0, fixture.HandlerCalls);
    }
}
