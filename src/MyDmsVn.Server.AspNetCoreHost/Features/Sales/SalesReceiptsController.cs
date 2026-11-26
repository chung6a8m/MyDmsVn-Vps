using System;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using MyDmsVn.Contracts;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.Application.Operations;
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
public sealed class SalesReceiptsController(IMediator mediator, IRequestCorrelationAccessor correlation,
    IV1OperationContextFactory operations) : ControllerBase
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

    [HttpPost]
    [V1SalesMutation]
    [ProducesResponseType(typeof(V1SuccessEnvelope<SalesDetailResponse>), 200)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 409)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 410)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 503)]
    public Task<IActionResult> Create([FromBody] SalesCreateRequest request, CancellationToken cancellationToken) =>
        ExecuteMutation<SalesReceiptDto, SalesDetailResponse>(WebOperationKind.SalesCreate, null,
            async context => await mediator.Send(new CreateSalesReceiptDraftCommand(SalesHttpMapper.ToApplication(request), context),
                cancellationToken).ConfigureAwait(false), SalesHttpMapper.Detail, cancellationToken);

    [HttpPut("{saleId}/draft")]
    [V1SalesMutation]
    [ProducesResponseType(typeof(V1SuccessEnvelope<SalesDetailResponse>), 200)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 404)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 409)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 410)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 503)]
    public Task<IActionResult> Update([FromRoute(Name = "saleId")] long saleId, [FromBody] SalesUpdateRequest request,
        CancellationToken cancellationToken) =>
        ExecuteMutation<SalesReceiptDto, SalesDetailResponse>(WebOperationKind.SalesUpdate, saleId,
            async context => await mediator.Send(new UpdateSalesReceiptDraftCommand(SalesHttpMapper.ToApplication(saleId, request), context),
                cancellationToken).ConfigureAwait(false), SalesHttpMapper.Detail, cancellationToken);

    [HttpPost("{saleId}/post")]
    [V1SalesMutation]
    [ProducesResponseType(typeof(V1SuccessEnvelope<SalesPostResponse>), 200)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 404)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 409)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 410)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 503)]
    public Task<IActionResult> Post([FromRoute(Name = "saleId")] long saleId, [FromBody] SalesPostRequest request,
        CancellationToken cancellationToken) =>
        ExecuteMutation<PostSalesReceiptResponse, SalesPostResponse>(WebOperationKind.SalesPost, saleId,
            async context => await mediator.Send(new PostSalesReceiptCommand(SalesHttpMapper.ToApplication(saleId, request), context),
                cancellationToken).ConfigureAwait(false), SalesHttpMapper.Post, cancellationToken);

    private async Task<IActionResult> ExecuteMutation<TSource, TResponse>(WebOperationKind kind, long? targetId,
        Func<WebOperationContext?, Task<ErrorOr<TSource>>> send, Func<TSource, TResponse> map,
        CancellationToken cancellationToken)
    {
        try
        {
            var context = await operations.CreateAsync(HttpContext, kind, targetId, cancellationToken).ConfigureAwait(false);
            var result = await send(context).ConfigureAwait(false);
            return V1ApiResults.Map(result, map, correlation.CorrelationId!);
        }
        catch (V1BindingException error)
        {
            return V1ApiResults.Invalid(error.Field, correlation.CorrelationId!);
        }
        catch (WebOperationException error)
        {
            return V1ApiResults.OperationError(error.Code, correlation.CorrelationId!);
        }
    }
}
