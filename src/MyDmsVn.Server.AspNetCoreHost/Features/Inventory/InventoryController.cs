using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.Application.Inventory;

namespace MyDmsVn.Server.HttpHost.Features.Inventory;

[ApiController]
[Route("api/v1/inventory")]
[Produces("application/json")]
[ProducesResponseType(typeof(V1ErrorEnvelope), 400)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 401)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 403)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 413)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 500)]
public sealed class InventoryController(IMediator mediator, IRequestCorrelationAccessor correlation) : ControllerBase
{
    [HttpGet("balances")]
    [ProducesResponseType(typeof(V1SuccessEnvelope<Web3PageResponse<StockBalanceResponse>>), 200)]
    public async Task<IActionResult> Balances([FromQuery] InventoryBalancesQuery query, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetStockBalancesQuery(query.ToApplication()), cancellationToken).ConfigureAwait(false);
        return V1ApiResults.Map(result, InventoryHttpMapper.Balances, correlation.CorrelationId!);
    }

    [HttpGet("stock-card")]
    [ProducesResponseType(typeof(V1SuccessEnvelope<Web3PageResponse<StockCardResponse>>), 200)]
    public async Task<IActionResult> StockCard([FromQuery] InventoryStockCardQuery query, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetStockCardQuery(query.ToApplication()), cancellationToken).ConfigureAwait(false);
        return V1ApiResults.Map(result, InventoryHttpMapper.StockCard, correlation.CorrelationId!);
    }
}
