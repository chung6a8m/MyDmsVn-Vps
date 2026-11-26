using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using MyDmsVn.Contracts;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.HttpHost.Features.Security;

namespace MyDmsVn.Server.HttpHost.Features.Foundation;

[ApiController]
[Route("api/v1/foundation")]
[V1DesktopApiRequired]
[Produces("application/json")]
[ProducesResponseType(typeof(V1ErrorEnvelope), 400)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 401)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 403)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 404)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 413)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 500)]
public sealed class FoundationController(IMediator mediator, IRequestCorrelationAccessor correlation) : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType(typeof(V1SuccessEnvelope<FoundationStatus>), 200)]
    public async Task<IActionResult> Status([FromQuery] FoundationStatusQuery query, CancellationToken cancellationToken) =>
        V1ApiResults.Map(await mediator.Send(new GetFoundationStatusQuery(query.ClientName), cancellationToken).ConfigureAwait(false),
            value => value, correlation.CorrelationId!);
}
