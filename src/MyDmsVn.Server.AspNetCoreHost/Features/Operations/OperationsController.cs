using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MyDmsVn.Server.Application.Operations;

namespace MyDmsVn.Server.HttpHost.Features.Operations;

[ApiController]
[Produces("application/json")]
[ProducesResponseType(typeof(V1ErrorEnvelope), 400)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 401)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 403)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 404)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 409)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 410)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 413)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 503)]
[Route("api/v1/operations")]
public sealed class OperationsController(OperationReadBoundary boundary) : ControllerBase
{
    [HttpGet("context", Order = 0)]
    [ProducesResponseType(typeof(V1SuccessEnvelope<OperationContextResponse>), 200)]
    public Task<IActionResult> Context(CancellationToken token) => boundary.Run(boundary.Options.Web4Enabled, () =>
    {
        OperationRequest.Path(Request, "/api/v1/operations/context");
        return boundary.Transaction(Request, async (unit, actor, session) =>
            new OperationContextResponse((await OperationReadBoundary.Epoch(unit, actor, session, token)).ToString("D"), actor, "web4-v1"), token);
    }, token);

    [HttpGet("{operationId}", Order = 1)]
    [ProducesResponseType(typeof(V1SuccessEnvelope<OperationStatusResponse>), 200)]
    public Task<IActionResult> Status([FromRoute] string operationId, CancellationToken token,
        [FromHeader(Name = "X-MyDmsVn-Deployment-Epoch")] string? epoch = null,
        [FromHeader(Name = "X-MyDmsVn-Operation-Fingerprint")] string? fingerprint = null) => boundary.Run(boundary.Options.Web4Enabled, () =>
    {
        var key = OperationRequest.Key(Request, operationId, "/api/v1/operations/");
        return boundary.Transaction(Request, async (unit, actor, session) =>
        {
            var status = await unit.Repository<IWebOperationReadRepository>().ReadStatusAsync(actor, session, key.Epoch,
                key.OperationId, key.Fingerprint, token).ConfigureAwait(false);
            if (status == null) throw new WebOperationException("Operation.NotObserved");
            return OperationHttpMapper.Status(status, key.Epoch, key.OperationId, key.Fingerprint);
        }, token);
    }, token);
}
