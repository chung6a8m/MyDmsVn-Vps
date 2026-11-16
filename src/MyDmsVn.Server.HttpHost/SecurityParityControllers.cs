using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using MediatR;
using MyDmsVn.Contracts;
using MyDmsVn.Contracts.Security;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.Application.Security;

namespace MyDmsVn.Server.HttpHost;

[RoutePrefix("api/v1/security")]
public sealed class SecurityParityController : WireApiController
{
    public SecurityParityController(IMediator mediator, IRequestCorrelationAccessor correlation) : base(mediator, correlation) { }

    [HttpGet, Route("permissions/check")]
    public Task<HttpResponseMessage> CheckPermission(CancellationToken ct) =>
        ExecuteDesktop<PermissionDecisionDto>(async () =>
        {
            await SalesWireInput.EmptyBody(Request).ConfigureAwait(false);
            return await Mediator.Send(new CheckPermissionQuery(DesktopParityWireInput.QueryValue(Request, "key", 100)), ct).ConfigureAwait(false);
        }, result => result);

    [HttpGet, Route("permissions/catalog")]
    public Task<HttpResponseMessage> PermissionCatalog(CancellationToken ct) =>
        ExecuteDesktop<PermissionCatalogDto>(async () =>
        {
            SalesWireInput.NoQuery(Request);
            await SalesWireInput.EmptyBody(Request).ConfigureAwait(false);
            return await Mediator.Send(new ListPermissionCatalogQuery(), ct).ConfigureAwait(false);
        }, result => result);

    [HttpGet, Route("users")]
    public Task<HttpResponseMessage> ListUsers(CancellationToken ct) =>
        ExecuteDesktop<IReadOnlyList<SecurityUserDto>>(async () =>
        {
            SalesWireInput.NoQuery(Request);
            await SalesWireInput.EmptyBody(Request).ConfigureAwait(false);
            return await Mediator.Send(new ListSecurityUsersQuery(), ct).ConfigureAwait(false);
        }, result => result);

    [HttpGet, Route("roles")]
    public Task<HttpResponseMessage> ListRoles(CancellationToken ct) =>
        ExecuteDesktop<IReadOnlyList<SecurityRoleDto>>(async () =>
        {
            SalesWireInput.NoQuery(Request);
            await SalesWireInput.EmptyBody(Request).ConfigureAwait(false);
            return await Mediator.Send(new ListSecurityRolesQuery(), ct).ConfigureAwait(false);
        }, result => result);

    [HttpPost, Route("mutations")]
    public Task<HttpResponseMessage> Mutate(CancellationToken ct) =>
        ExecuteDesktop<SecurityAdministrationResultDto>(async () =>
        {
            var input = DesktopParityWireInput.Mutation(await DesktopParityWireInput.Body(Request).ConfigureAwait(false));
            return await Mediator.Send(new MutateSecurityAdministrationCommand(
                (SecurityAdministrationTarget)(int)input.Target, (SecurityAdministrationOperation)(int)input.Operation,
                input.TargetId, input.RelatedId, input.Name, input.DisplayName, input.Email,
                input.IsActive, input.Password, input.PermissionKey, input.Decision,
                input.ExpectedRevision, input.ExpectedRelatedRevision, input.CorrelationId), ct).ConfigureAwait(false);
        }, result => result);

    [HttpPost, Route("import/preview")]
    public Task<HttpResponseMessage> PreviewImport(CancellationToken ct) =>
        ExecuteDesktop<PermissionAssignmentImportPreviewDto>(async () =>
        {
            var input = DesktopParityWireInput.ImportPreview(await DesktopParityWireInput.Body(Request).ConfigureAwait(false));
            return await Mediator.Send(new PreviewPermissionAssignmentImportQuery(input.targetType, input.targetId, input.json), ct).ConfigureAwait(false);
        }, result => result);

    [HttpPost, Route("import/apply")]
    public Task<HttpResponseMessage> ApplyImport(CancellationToken ct) =>
        ExecuteDesktop<PermissionAssignmentImportResultDto>(async () =>
        {
            var input = DesktopParityWireInput.ImportApply(await DesktopParityWireInput.Body(Request).ConfigureAwait(false));
            return await Mediator.Send(new ApplyPermissionAssignmentImportCommand(input.targetType, input.targetId,
                input.request.Json, input.request.ExpectedTargetRevision, input.request.ExpectedCatalogDigest), ct).ConfigureAwait(false);
        }, result => result);
}

[RoutePrefix("api/v1/foundation")]
public sealed class FoundationParityController : WireApiController
{
    public FoundationParityController(IMediator mediator, IRequestCorrelationAccessor correlation) : base(mediator, correlation) { }

    [HttpGet, Route("status")]
    public Task<HttpResponseMessage> Status(CancellationToken ct) =>
        ExecuteDesktop<FoundationStatus>(async () =>
        {
            await SalesWireInput.EmptyBody(Request).ConfigureAwait(false);
            return await Mediator.Send(new GetFoundationStatusQuery(
                DesktopParityWireInput.QueryValue(Request, "clientName", 128)), ct).ConfigureAwait(false);
        }, result => result);
}
