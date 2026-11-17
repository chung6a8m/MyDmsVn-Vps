using System;
using System.Threading;
using System.Threading.Tasks;
using MyDmsVn.Contracts;
using MyDmsVn.Contracts.Security;
using MyDmsVn.Desktop.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MyDmsVn.Vps03.Acceptance.Tests;

public sealed class Vps03SessionAcceptanceTests
{
    [Vps03AcceptanceFact]
    [Trait("CaseId", "VPS03.GATE.SESSIONS")]
    public async Task Login_me_logout_and_reauthentication_cross_the_real_host()
    {
        var env = await Vps03OwnedEnvironment.CreateAsync(13, CancellationToken.None);
        try
        {
            var identity = env.DesktopServices.GetRequiredService<IIdentityApiClient>();
            Ok(await identity.LoginAsync(new LoginRequest(env.Username, env.Password), default));
            Assert.Equal(env.Username, Ok(await identity.GetCurrentUserAsync(default)).Username);
            env.DesktopServices.GetRequiredService<IDesktopSession>().SignOut();
            var signedOut = await identity.GetCurrentUserAsync(default);
            Assert.False(signedOut.IsSuccess);
            Assert.Equal("Auth.SessionChanged", signedOut.Error!.Code);
            Assert.Equal(env.Username, Ok(await identity.LoginAsync(new LoginRequest(env.Username, env.Password), default)).Username);
        }
        finally { await env.DisposeAsync(); }
    }

    [Vps03AcceptanceFact]
    [Trait("CaseId", "VPS03.GATE.AUTH")]
    public async Task Unauthorized_login_is_normalized_without_server_details()
    {
        var env = await Vps03OwnedEnvironment.CreateAsync(13, CancellationToken.None);
        try
        {
            using var second = env.CreateDesktopServices();
            var denied = await second.GetRequiredService<IIdentityApiClient>()
                .LoginAsync(new LoginRequest(env.Username, "wrong-password"), default);
            Assert.False(denied.IsSuccess); Assert.Equal(ApiStatusCode.Unauthorized, denied.Error!.Status);
            Assert.DoesNotContain("System.", denied.Error.Message);
        }
        finally { await env.DisposeAsync(); }
    }

    [Vps03AcceptanceFact]
    [Trait("CaseId", "VPS03.GATE.PERMISSIONS")]
    public async Task Permission_revocation_is_visible_to_an_independent_authenticated_session()
    {
        var env = await Vps03OwnedEnvironment.CreateAsync(13, CancellationToken.None);
        try
        {
            Ok(await env.DesktopServices.GetRequiredService<IIdentityApiClient>()
                .LoginAsync(new LoginRequest(env.Username, env.Password), default));
            var admin = env.DesktopServices.GetRequiredService<ISecurityAdministrationApiClient>()
                .ForSession(env.DesktopServices.GetRequiredService<IDesktopSession>().Version);
            var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            var username = "vps03.user." + suffix;
            var password = "Vps03User!" + suffix;
            Ok(await admin.MutateAsync(new SecurityAdministrationMutationRequest(
                SecurityAdministrationTargetDto.User, SecurityAdministrationOperationDto.Create,
                name: username, displayName: "VPS03 User", password: password), default));
            var user = Assert.Single(Ok(await admin.ListUsersAsync(default)), x => x.Username == username);
            var roleName = "VPS03-ROLE-" + suffix;
            Ok(await admin.MutateAsync(new SecurityAdministrationMutationRequest(
                SecurityAdministrationTargetDto.Role, SecurityAdministrationOperationDto.Create, name: roleName), default));
            var role = Assert.Single(Ok(await admin.ListRolesAsync(default)), x => x.Name == roleName);
            var membership = Ok(await admin.MutateAsync(new SecurityAdministrationMutationRequest(
                SecurityAdministrationTargetDto.User, SecurityAdministrationOperationDto.SetMembership,
                user.UserId, role.RoleId, isActive: true, expectedRevision: user.AssignmentRevision,
                expectedRelatedRevision: role.AssignmentRevision), default));
            var grant = Ok(await admin.MutateAsync(new SecurityAdministrationMutationRequest(
                SecurityAdministrationTargetDto.Role, SecurityAdministrationOperationDto.SetPermission,
                role.RoleId, permissionKey: "Catalog.Products.Read", decision: true,
                expectedRevision: membership.RelatedAssignmentRevision), default));
            using var second = env.CreateDesktopServices();
            Ok(await second.GetRequiredService<IIdentityApiClient>()
                .LoginAsync(new LoginRequest(username, password), default));
            Assert.True(Ok(await second.GetRequiredService<IPermissionApiClient>()
                .CheckAsync("Catalog.Products.Read", default)).IsAllowed);
            Ok(await admin.MutateAsync(new SecurityAdministrationMutationRequest(
                SecurityAdministrationTargetDto.Role, SecurityAdministrationOperationDto.SetPermission,
                role.RoleId, permissionKey: "Catalog.Products.Read", decision: false,
                expectedRevision: grant.AssignmentRevision), default));
            Assert.False(Ok(await second.GetRequiredService<IPermissionApiClient>()
                .CheckAsync("Catalog.Products.Read", default)).IsAllowed);
        }
        finally { await env.DisposeAsync(); }
    }

    private static T Ok<T>(ApiResponse<T> response)
    { Assert.True(response.IsSuccess, response.Error?.Code + ": " + response.Error?.Message); Assert.NotNull(response.Data); return response.Data!; }
}
