using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using MyDmsVn.Contracts;
using MyDmsVn.Contracts.Security;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.Application.Security;

namespace MyDmsVn.Server.HttpHost.Features.Security;

[ApiController]
[Route("api/v1/security")]
[V1DesktopApiRequired]
[Produces("application/json")]
[ProducesResponseType(typeof(V1ErrorEnvelope), 400)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 401)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 403)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 404)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 413)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 500)]
public sealed class SecurityController(IMediator mediator, IRequestCorrelationAccessor correlation) : ControllerBase
{
    [HttpGet("permissions/check")]
    [ProducesResponseType(typeof(V1SuccessEnvelope<PermissionDecisionDto>), 200)]
    public async Task<IActionResult> CheckPermission([FromQuery] PermissionCheckQuery query, CancellationToken cancellationToken) =>
        V1ApiResults.Map(await mediator.Send(new CheckPermissionQuery(query.Key), cancellationToken).ConfigureAwait(false),
            value => value, correlation.CorrelationId!);

    [HttpGet("permissions/catalog")]
    [V1SecurityNoQuery]
    [ProducesResponseType(typeof(V1SuccessEnvelope<PermissionCatalogDto>), 200)]
    public async Task<IActionResult> PermissionCatalog(CancellationToken cancellationToken) =>
        V1ApiResults.Map(await mediator.Send(new ListPermissionCatalogQuery(), cancellationToken).ConfigureAwait(false),
            value => value, correlation.CorrelationId!);

    [HttpGet("users")]
    [V1SecurityNoQuery]
    [ProducesResponseType(typeof(V1SuccessEnvelope<IReadOnlyList<SecurityUserDto>>), 200)]
    public async Task<IActionResult> ListUsers(CancellationToken cancellationToken) =>
        V1ApiResults.Map(await mediator.Send(new ListSecurityUsersQuery(), cancellationToken).ConfigureAwait(false),
            value => value, correlation.CorrelationId!);

    [HttpGet("roles")]
    [V1SecurityNoQuery]
    [ProducesResponseType(typeof(V1SuccessEnvelope<IReadOnlyList<SecurityRoleDto>>), 200)]
    public async Task<IActionResult> ListRoles(CancellationToken cancellationToken) =>
        V1ApiResults.Map(await mediator.Send(new ListSecurityRolesQuery(), cancellationToken).ConfigureAwait(false),
            value => value, correlation.CorrelationId!);

    [HttpPost("mutations")]
    [V1SecurityNoQuery]
    [ProducesResponseType(typeof(V1SuccessEnvelope<SecurityAdministrationResultDto>), 200)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 409)]
    public async Task<IActionResult> Mutate([FromBody] SecurityMutationBody request, CancellationToken cancellationToken) =>
        V1ApiResults.Map(await mediator.Send(request.ToCommand(), cancellationToken).ConfigureAwait(false),
            value => value, correlation.CorrelationId!);

    [HttpPost("import/preview")]
    [V1SecurityNoQuery]
    [ProducesResponseType(typeof(V1SuccessEnvelope<PermissionAssignmentImportPreviewDto>), 200)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 409)]
    public async Task<IActionResult> PreviewImport([FromBody] PermissionImportPreviewBody request, CancellationToken cancellationToken) =>
        V1ApiResults.Map(await mediator.Send(request.ToQuery(), cancellationToken).ConfigureAwait(false),
            value => value, correlation.CorrelationId!);

    [HttpPost("import/apply")]
    [V1SecurityNoQuery]
    [ProducesResponseType(typeof(V1SuccessEnvelope<PermissionAssignmentImportResultDto>), 200)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 409)]
    public async Task<IActionResult> ApplyImport([FromBody] PermissionImportApplyBody request, CancellationToken cancellationToken) =>
        V1ApiResults.Map(await mediator.Send(request.ToCommand(), cancellationToken).ConfigureAwait(false),
            value => value, correlation.CorrelationId!);
}
