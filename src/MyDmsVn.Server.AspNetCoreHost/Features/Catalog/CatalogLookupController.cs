using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using MyDmsVn.Contracts;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.Application.Catalog;

namespace MyDmsVn.Server.HttpHost.Features.Catalog;

[ApiController]
[Route("api/v1/catalog")]
[ProducesResponseType(typeof(V1SuccessEnvelope<IReadOnlyList<CatalogLookupDto>>), 200)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 400)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 401)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 403)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 500)]
public sealed class CatalogLookupController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IRequestCorrelationAccessor _correlation;

    public CatalogLookupController(IMediator mediator, IRequestCorrelationAccessor correlation)
    {
        _mediator = mediator;
        _correlation = correlation;
    }

    [HttpGet("products/lookup")]
    public Task<IActionResult> Products([FromQuery] CatalogLookupQuery query, CancellationToken cancellationToken) =>
        Lookup(query, request => new LookupProductsQuery(request), cancellationToken);

    [HttpGet("customers/lookup")]
    public Task<IActionResult> Customers([FromQuery] CatalogLookupQuery query, CancellationToken cancellationToken) =>
        Lookup(query, request => new LookupCustomersQuery(request), cancellationToken);

    [HttpGet("warehouses/lookup")]
    public Task<IActionResult> Warehouses([FromQuery] CatalogLookupQuery query, CancellationToken cancellationToken) =>
        Lookup(query, request => new LookupWarehousesQuery(request), cancellationToken);

    [HttpGet("employees/lookup")]
    public Task<IActionResult> Employees([FromQuery] CatalogLookupQuery query, CancellationToken cancellationToken) =>
        Lookup(query, request => new LookupEmployeesQuery(request), cancellationToken);

    private async Task<IActionResult> Lookup(CatalogLookupQuery query,
        Func<CatalogLookupRequest, IRequest<ErrorOr<IReadOnlyList<CatalogLookupDto>>>> request,
        CancellationToken cancellationToken)
    {
        // Admission has already validated bearer + SQL session. The existing
        // generated Mediator pipeline remains the permission/validation owner.
        var result = await _mediator.Send(request(new CatalogLookupRequest(query.Search, query.Limit)),
            cancellationToken).ConfigureAwait(false);
        return V1ApiResults.Map(result, rows => rows, _correlation.CorrelationId!);
    }
}
