using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using MyDmsVn.Server.HttpHost;
using Xunit;
using LiveFixture = MyDmsVn.Server.AspNetCoreHost.Tests.Vps05cDesktopRouteLiveParityTests.DesktopLiveFixture;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps10NativeCatalogTests
{
    private const string Catalog = "/api/v1/catalog/";

    [Theory]
    [InlineData("products", false)]
    [InlineData("customers", false)]
    [InlineData("warehouses", false)]
    [InlineData("employees", false)]
    [InlineData("products", true)]
    [InlineData("customers", true)]
    [InlineData("warehouses", true)]
    [InlineData("employees", true)]
    public async Task Native_lookup_remains_available_in_both_desktop_modes(string entity, bool desktopEnabled)
    {
        await using var fixture = await LiveFixture.StartAsync(desktopApiEnabled: desktopEnabled);
        using var response = await fixture.SendAsync("GET", Catalog + entity + "/lookup?search=a+b%2Bc&limit=200", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("data").ValueKind);
        Assert.False(json.RootElement.TryGetProperty("error", out _));
        Assert.Equal(1, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData("", "limit")]
    [InlineData("?limit=0", "limit")]
    [InlineData("?limit=201", "limit")]
    [InlineData("?limit=01", "limit")]
    [InlineData("?limit=%2B1", "limit")]
    [InlineData("?limit=1.0", "limit")]
    [InlineData("?limit=1&%6Cimit=2", null)]
    [InlineData("?Limit=1", null)]
    [InlineData("?limit=1&extra=x", null)]
    [InlineData("?limit=1&search=%FF", null)]
    [InlineData("?limit=1&search=%00", "search")]
    [InlineData("?limit=1&search=x&search=y", null)]
    public async Task Native_binder_rejects_invalid_wire_before_dispatch(string query, string? field)
    {
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("GET", Catalog + "products/lookup" + query, null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("ValidationError", error.GetProperty("code").GetString());
        var detail = error.GetProperty("details")[0];
        Assert.Equal("Validation.InvalidValue", detail.GetProperty("code").GetString());
        Assert.Equal(field, detail.TryGetProperty("field", out var value) ? value.GetString() : null);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Fact]
    public async Task Body_rejection_precedes_query_validation()
    {
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("GET", Catalog + "products/lookup?limit=0", "{}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var detail = json.RootElement.GetProperty("error").GetProperty("details")[0];
        Assert.False(detail.TryGetProperty("field", out _));
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Fact]
    public async Task Unauthenticated_malformed_lookup_is_rejected_before_binding()
    {
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("GET", Catalog + "products/lookup?limit=0", "{}", authenticated: false);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Fact]
    public async Task Denied_lookup_retains_application_permission_boundary()
    {
        await using var fixture = await LiveFixture.StartAsync(denyPermissions: true);
        using var response = await fixture.SendAsync("GET", Catalog + "products/lookup?limit=1", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEmpty(fixture.PermissionChecks);
        Assert.Equal(0, fixture.HandlerCalls);
    }
}
