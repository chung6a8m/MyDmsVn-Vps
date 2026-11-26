using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.Application.Inventory;
using MyDmsVn.Server.HttpHost.Features.Inventory;

namespace MyDmsVn.Server.HttpHost.Features.Receipts;

[ApiController]
[Route("api/v1/goods-receipts")]
[Produces("application/json")]
[ProducesResponseType(typeof(V1ErrorEnvelope), 400)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 401)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 403)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 413)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 500)]
public sealed class GoodsReceiptsController(IMediator mediator, IRequestCorrelationAccessor correlation) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(V1SuccessEnvelope<Web3PageResponse<ReceiptDetailResponse>>), 200)]
    public async Task<IActionResult> List([FromQuery] ReceiptListQuery query, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListGoodsReceiptsQuery(query.ToApplication()), cancellationToken).ConfigureAwait(false);
        return V1ApiResults.Map(result, ReceiptHttpMapper.ReceiptPage, correlation.CorrelationId!);
    }

    [HttpGet("{receiptId}")]
    [ProducesResponseType(typeof(V1SuccessEnvelope<ReceiptDetailResponse>), 200)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 404)]
    public async Task<IActionResult> Get([FromRoute(Name = "receiptId")] long receiptId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetGoodsReceiptByIdQuery(receiptId), cancellationToken).ConfigureAwait(false);
        return V1ApiResults.Map(result, ReceiptHttpMapper.Detail, correlation.CorrelationId!);
    }
}
