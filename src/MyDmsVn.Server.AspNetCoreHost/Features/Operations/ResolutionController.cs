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
[Route("api/v2/operations")]
public sealed class ResolutionController(OperationReadBoundary boundary) : ControllerBase
{
    [HttpGet("context", Order = 0)]
    [ProducesResponseType(typeof(V1SuccessEnvelope<ResolutionContextResponse>), 200)]
    public Task<IActionResult> Context(CancellationToken token) => boundary.Run(boundary.Options.Web5Enabled, () =>
    {
        OperationRequest.Path(Request, "/api/v2/operations/context");
        return boundary.Transaction(Request, async (unit, actor, session) =>
            new ResolutionContextResponse(OperationHttpMapper.ResolutionVersion,
                (await OperationReadBoundary.Epoch(unit, actor, session, token)).ToString("D"), actor, false), token);
    }, token);

    [HttpGet("{operationId}/resolution", Order = 1)]
    [ProducesResponseType(typeof(V1SuccessEnvelope<ResolutionResponse>), 200)]
    public Task<IActionResult> Resolution([FromRoute] string operationId, CancellationToken token,
        [FromHeader(Name = "X-MyDmsVn-Deployment-Epoch")] string? epoch = null,
        [FromHeader(Name = "X-MyDmsVn-Operation-Fingerprint")] string? fingerprint = null) => boundary.Run(boundary.Options.Web5Enabled, () =>
    {
        var key = OperationRequest.Key(Request, operationId, "/api/v2/operations/", "/resolution");
        return boundary.Transaction(Request, async (unit, actor, session) =>
        {
            var result = await unit.Repository<IWebOperationResolutionRepository>().ReadResolutionAsync(actor, session,
                key.Epoch, key.OperationId, key.Fingerprint, token).ConfigureAwait(false);
            if (result == null) throw new WebOperationException("Operation.NotObserved");
            return OperationHttpMapper.Resolution(result, actor, key.Epoch, key.OperationId, key.Fingerprint);
        }, token);
    }, token);
}
