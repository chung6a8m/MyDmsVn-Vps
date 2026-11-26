using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using MyDmsVn.Contracts;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.Application.Catalog;

namespace MyDmsVn.Server.HttpHost.Features.Catalog;

[ApiController]
[Route("api/v1/catalog/warehouses")]
[V1DesktopApiRequired]
[V1CatalogRoute]
[Produces("application/json")]
[ProducesResponseType(typeof(V1ErrorEnvelope), 400)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 401)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 403)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 404)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 413)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 500)]
public sealed class WarehousesController(IMediator mediator, IRequestCorrelationAccessor correlation) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(V1SuccessEnvelope<PagedResult<WarehouseDto>>), 200)]
    public async Task<IActionResult> List([FromQuery] CatalogCrudListQuery query, CancellationToken cancellationToken) =>
        V1ApiResults.Map(await mediator.Send(new ListWarehousesQuery(query.ToApplication()), cancellationToken).ConfigureAwait(false),
            value => value, correlation.CorrelationId!);

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(V1SuccessEnvelope<WarehouseDto>), 200)]
    public async Task<IActionResult> Get([FromRoute] int id, CancellationToken cancellationToken) =>
        V1ApiResults.Map(await mediator.Send(new GetWarehouseByIdQuery(id), cancellationToken).ConfigureAwait(false),
            value => value, correlation.CorrelationId!);

    [HttpPost]
    [ProducesResponseType(typeof(V1SuccessEnvelope<WarehouseDto>), 200)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 409)]
    public async Task<IActionResult> Create([FromBody] WarehouseSaveBody request, CancellationToken cancellationToken) =>
        V1ApiResults.Map(await mediator.Send(new CreateWarehouseCommand(request.ToApplication()), cancellationToken).ConfigureAwait(false),
            value => value, correlation.CorrelationId!);

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(V1SuccessEnvelope<WarehouseDto>), 200)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 409)]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] WarehouseSaveBody request, CancellationToken cancellationToken) =>
        V1ApiResults.Map(await mediator.Send(new UpdateWarehouseCommand(id, request.ToApplication()), cancellationToken).ConfigureAwait(false),
            value => value, correlation.CorrelationId!);

    [HttpPut("{id:int}/active")]
    [ProducesResponseType(typeof(V1SuccessEnvelope<UnitResponse>), 200)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 409)]
    public async Task<IActionResult> SetActive([FromRoute] int id, [FromBody] CatalogActiveBody request, CancellationToken cancellationToken) =>
        V1ApiResults.Map(await mediator.Send(new SetWarehouseActiveCommand(id, request.IsActive), cancellationToken).ConfigureAwait(false),
            value => value, correlation.CorrelationId!);
}
