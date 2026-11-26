using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using MyDmsVn.Server.Application.Security;
using Xunit;
using LiveFixture = MyDmsVn.Server.AspNetCoreHost.Tests.Vps05cDesktopRouteLiveParityTests.DesktopLiveFixture;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps10SecurityTests
{
    [Theory]
    [InlineData("GET", "/api/v1/security/permissions/check?key=Catalog.Products.Read", null)]
    [InlineData("GET", "/api/v1/security/permissions/catalog", null)]
    [InlineData("GET", "/api/v1/security/users", null)]
    [InlineData("GET", "/api/v1/security/roles", null)]
    [InlineData("POST", "/api/v1/security/mutations", "{\"target\":1,\"operation\":2,\"targetId\":1,\"isActive\":false,\"expectedRevision\":9007199254740993}")]
    [InlineData("POST", "/api/v1/security/import/preview", "{\"targetType\":1,\"targetId\":3,\"json\":\"{\\\"schemaVersion\\\":1,\\\"targetType\\\":\\\"Role\\\",\\\"targetId\\\":3,\\\"entries\\\":[]}\"}")]
    [InlineData("POST", "/api/v1/security/import/apply", "{\"targetType\":1,\"targetId\":3,\"json\":\"{\\\"schemaVersion\\\":1,\\\"targetType\\\":\\\"Role\\\",\\\"targetId\\\":3,\\\"entries\\\":[]}\",\"expectedTargetRevision\":9007199254740993,\"expectedCatalogDigest\":\"digest\"}")]
    [InlineData("GET", "/api/v1/foundation/status?clientName=Desktop", null)]
    public async Task Disabled_gate_and_auth_precede_binding(string verb, string path, string? body)
    {
        _ = body;
        await using var fixture = await LiveFixture.StartAsync(desktopApiEnabled: false);
        var invalid = path + (path.Contains('?') ? "&" : "?") + "unknown=1";
        using var anonymous = await fixture.SendAsync(verb, invalid, "{", authenticated: false);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var disabled = await fixture.SendAsync(verb, invalid, "{");
        Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);
        Assert.Equal(0, fixture.HandlerCalls);
        Assert.Empty(fixture.PermissionChecks);
    }

    [Theory]
    [InlineData("GET", "/api/v1/security/permissions/catalog", null, "Security.Permissions.Read")]
    [InlineData("GET", "/api/v1/security/users", null, "Security.Users.Manage")]
    [InlineData("GET", "/api/v1/security/roles", null, "Security.Roles.Manage")]
    [InlineData("POST", "/api/v1/security/mutations", "{\"target\":0,\"operation\":2,\"targetId\":1,\"isActive\":false,\"expectedRevision\":9007199254740993}", "Security.Users.Manage")]
    [InlineData("POST", "/api/v1/security/import/preview", "{\"targetType\":0,\"targetId\":3,\"json\":\"{\\\"schemaVersion\\\":1,\\\"targetType\\\":\\\"User\\\",\\\"targetId\\\":3,\\\"entries\\\":[]}\"}", "Security.Users.Manage")]
    [InlineData("POST", "/api/v1/security/import/apply", "{\"targetType\":0,\"targetId\":3,\"json\":\"{\\\"schemaVersion\\\":1,\\\"targetType\\\":\\\"User\\\",\\\"targetId\\\":3,\\\"entries\\\":[]}\",\"expectedTargetRevision\":9007199254740993,\"expectedCatalogDigest\":\"digest\"}", "Security.Users.Manage")]
    [InlineData("POST", "/api/v1/security/mutations", "{\"target\":1,\"operation\":2,\"targetId\":1,\"isActive\":false,\"expectedRevision\":9007199254740993}", "Security.Roles.Manage")]
    [InlineData("POST", "/api/v1/security/import/preview", "{\"targetType\":1,\"targetId\":3,\"json\":\"{\\\"schemaVersion\\\":1,\\\"targetType\\\":\\\"Role\\\",\\\"targetId\\\":3,\\\"entries\\\":[]}\"}", "Security.Roles.Manage")]
    [InlineData("POST", "/api/v1/security/import/apply", "{\"targetType\":1,\"targetId\":3,\"json\":\"{\\\"schemaVersion\\\":1,\\\"targetType\\\":\\\"Role\\\",\\\"targetId\\\":3,\\\"entries\\\":[]}\",\"expectedTargetRevision\":9007199254740993,\"expectedCatalogDigest\":\"digest\"}", "Security.Roles.Manage")]
    public async Task Permissions_are_independent_and_checked_before_dispatch(string verb, string path, string? body, string permission)
    {
        await using var fixture = await LiveFixture.StartAsync(denyPermissions: true);
        using var denied = await fixture.SendAsync(verb, path, body);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(permission, Assert.Single(fixture.PermissionChecks));
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData("GET", "/api/v1/security/permissions/check", null)]
    [InlineData("GET", "/api/v1/security/permissions/check?key=%00", null)]
    [InlineData("GET", "/api/v1/security/permissions/check?key=A&%6Bey=B", null)]
    [InlineData("GET", "/api/v1/foundation/status", null)]
    [InlineData("GET", "/api/v1/foundation/status?clientName=Desktop&other=1", null)]
    [InlineData("GET", "/api/v1/security/users?unexpected=1", null)]
    [InlineData("GET", "/api/v1/security/roles", "{}")]
    [InlineData("GET", "/api/v1/security/permissions/catalog", "{}")]
    [InlineData("POST", "/api/v1/security/mutations", "{}")]
    [InlineData("POST", "/api/v1/security/mutations", "{")]
    [InlineData("POST", "/api/v1/security/mutations", "[]")]
    [InlineData("POST", "/api/v1/security/mutations", "{\"target\":\"1\",\"operation\":2,\"targetId\":1,\"isActive\":false,\"expectedRevision\":9007199254740993}")]
    [InlineData("POST", "/api/v1/security/mutations", "{\"target\":1,\"operation\":99,\"targetId\":1,\"isActive\":false,\"expectedRevision\":9007199254740993}")]
    [InlineData("POST", "/api/v1/security/mutations", "{\"target\":1,\"operation\":2,\"targetId\":1,\"isActive\":false,\"expectedRevision\":\"9007199254740993\"}")]
    [InlineData("POST", "/api/v1/security/mutations", "{\"target\":1,\"operation\":2,\"targetId\":1,\"isActive\":false,\"expectedRevision\":9007199254740993.0}")]
    [InlineData("POST", "/api/v1/security/mutations", "{\"target\":1,\"operation\":2,\"targetId\":1,\"isActive\":false,\"expectedRevision\":9e2}")]
    [InlineData("POST", "/api/v1/security/mutations", "{\"target\":1,\"operation\":2,\"targetId\":1,\"isActive\":false,\"expectedRevision\":9223372036854775808}")]
    [InlineData("POST", "/api/v1/security/mutations", "{\"target\":1,\"operation\":2,\"targetId\":1,\"isActive\":false,\"expectedRevision\":-1}")]
    [InlineData("POST", "/api/v1/security/mutations", "{\"target\":1,\"operation\":2,\"targetId\":1,\"isActive\":\"false\",\"expectedRevision\":9007199254740993}")]
    [InlineData("POST", "/api/v1/security/mutations", "{\"target\":1,\"operation\":2,\"targetId\":1,\"isActive\":false,\"expectedRevision\":9007199254740993,\"actorUserId\":99}")]
    [InlineData("POST", "/api/v1/security/mutations", "{\"target\":1,\"operation\":2,\"targetId\":1,\"isActive\":false,\"expectedRevision\":9007199254740993,\"targetId\":2}")]
    [InlineData("POST", "/api/v1/security/mutations", "{\"target\":1,\"operation\":2,\"targetId\":1,\"isActive\":false,\"expectedRevision\":9007199254740993,\"correlationId\":\"bad\"}")]
    [InlineData("POST", "/api/v1/security/import/preview", "{}")]
    [InlineData("POST", "/api/v1/security/import/preview", "{\"targetType\":2,\"targetId\":3,\"json\":\"{\\\"schemaVersion\\\":1,\\\"targetType\\\":\\\"Role\\\",\\\"targetId\\\":3,\\\"entries\\\":[]}\"}")]
    [InlineData("POST", "/api/v1/security/import/preview", "{\"targetType\":1,\"targetId\":\"3\",\"json\":\"{\\\"schemaVersion\\\":1,\\\"targetType\\\":\\\"Role\\\",\\\"targetId\\\":3,\\\"entries\\\":[]}\"}")]
    [InlineData("POST", "/api/v1/security/import/preview", "{\"targetType\":1,\"targetId\":3,\"json\":\"{\\\"schemaVersion\\\":1,\\\"targetType\\\":\\\"Role\\\",\\\"targetId\\\":3,\\\"entries\\\":[]}\",\"actorUserId\":99}")]
    [InlineData("POST", "/api/v1/security/import/apply", "{}")]
    [InlineData("POST", "/api/v1/security/import/apply", "{\"targetType\":1,\"targetId\":3,\"json\":\"{\\\"schemaVersion\\\":1,\\\"targetType\\\":\\\"Role\\\",\\\"targetId\\\":3,\\\"entries\\\":[]}\",\"expectedTargetRevision\":\"9007199254740993\",\"expectedCatalogDigest\":\"digest\"}")]
    [InlineData("POST", "/api/v1/security/import/apply", "{\"targetType\":1,\"targetId\":3,\"json\":\"{\\\"schemaVersion\\\":1,\\\"targetType\\\":\\\"Role\\\",\\\"targetId\\\":3,\\\"entries\\\":[]}\",\"expectedTargetRevision\":1.0,\"expectedCatalogDigest\":\"digest\"}")]
    [InlineData("POST", "/api/v1/security/import/apply", "{\"targetType\":1,\"targetId\":3,\"json\":\"{\\\"schemaVersion\\\":1,\\\"targetType\\\":\\\"Role\\\",\\\"targetId\\\":3,\\\"entries\\\":[]}\",\"expectedTargetRevision\":9007199254740993,\"expectedCatalogDigest\":\"digest\",\"authorizedUserId\":99}")]
    public async Task Invalid_wire_never_reaches_permission_or_handler(string verb, string path, string? body)
    {
        await using var fixture = await LiveFixture.StartAsync(denyPermissions: true);
        using var reply = await fixture.SendAsync(verb, path, body);
        Assert.Equal(HttpStatusCode.BadRequest, reply.StatusCode);
        using var json = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
        Assert.Equal("ValidationError", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(fixture.PermissionChecks);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData("vi-VN")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public async Task Numeric_revisions_remain_exact_under_each_request_culture(string culture)
    {
        await using var fixture = await LiveFixture.StartAsync(requestCultureName: culture);
        using var mutation = await fixture.SendAsync("POST", "/api/v1/security/mutations", "{\"target\":1,\"operation\":2,\"targetId\":1,\"isActive\":false,\"expectedRevision\":9007199254740993}");
        Assert.Equal(HttpStatusCode.OK, mutation.StatusCode);
        Assert.Equal(9007199254740993L, Assert.IsType<MutateSecurityAdministrationCommand>(fixture.LastRequest).ExpectedRevision);
        using var apply = await fixture.SendAsync("POST", "/api/v1/security/import/apply", "{\"targetType\":1,\"targetId\":3,\"json\":\"{\\\"schemaVersion\\\":1,\\\"targetType\\\":\\\"Role\\\",\\\"targetId\\\":3,\\\"entries\\\":[]}\",\"expectedTargetRevision\":9007199254740993,\"expectedCatalogDigest\":\"digest\"}");
        Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
        Assert.Equal(9007199254740993L, Assert.IsType<ApplyPermissionAssignmentImportCommand>(fixture.LastRequest).ExpectedTargetRevision);
        Assert.Equal(culture, fixture.LastRequestCulture);
    }
}
