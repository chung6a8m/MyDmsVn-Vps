using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.Application.Sales;

namespace MyDmsVn.Server.HttpHost.Features.Sales;

[ApiController]
[Route("api/v1/sales-receipts")]
[Produces("application/json")]
[ProducesResponseType(typeof(V1ErrorEnvelope), 400)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 401)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 403)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 413)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 500)]
public sealed class SalesReceiptsController(IMediator mediator, IRequestCorrelationAccessor correlation) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(V1SuccessEnvelope<SalesListPageResponse>), 200)]
    public async Task<IActionResult> List([FromQuery] SalesListQuery query, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListSalesReceiptsQuery(SalesHttpMapper.ToApplication(query)),
            cancellationToken).ConfigureAwait(false);
        return V1ApiResults.Map(result, SalesHttpMapper.Page, correlation.CorrelationId!);
    }

    [HttpGet("{saleId}")]
    [ProducesResponseType(typeof(V1SuccessEnvelope<SalesDetailResponse>), 200)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 404)]
    public async Task<IActionResult> Get([FromRoute(Name = "saleId")] long saleId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetSalesReceiptByIdQuery(saleId), cancellationToken).ConfigureAwait(false);
        return V1ApiResults.Map(result, SalesHttpMapper.Detail, correlation.CorrelationId!);
    }
}
