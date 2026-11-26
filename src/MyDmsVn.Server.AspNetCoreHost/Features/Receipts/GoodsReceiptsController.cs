using System;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using MyDmsVn.Contracts;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.Application.Inventory;
using MyDmsVn.Server.Application.Operations;
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
public sealed class GoodsReceiptsController(IMediator mediator, IRequestCorrelationAccessor correlation,
    IV1OperationContextFactory operations) : ControllerBase
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

    [HttpPost]
    [V1WriteMutation]
    [ProducesResponseType(typeof(V1SuccessEnvelope<ReceiptDetailResponse>), 200)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 409)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 410)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 503)]
    public Task<IActionResult> Create([FromBody] ReceiptCreateRequest request, CancellationToken cancellationToken) =>
        ExecuteMutation<GoodsReceiptDto, ReceiptDetailResponse>(WebOperationKind.ReceiptCreate, null,
            async context => await mediator.Send(new CreateGoodsReceiptDraftCommand(ReceiptHttpMapper.ToApplication(request), context),
                cancellationToken).ConfigureAwait(false), ReceiptHttpMapper.Detail, cancellationToken);

    [HttpPut("{receiptId}/draft")]
    [V1WriteMutation]
    [ProducesResponseType(typeof(V1SuccessEnvelope<ReceiptDetailResponse>), 200)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 404)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 409)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 410)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 503)]
    public Task<IActionResult> Update([FromRoute(Name = "receiptId")] long receiptId, [FromBody] ReceiptUpdateRequest request,
        CancellationToken cancellationToken) =>
        ExecuteMutation<GoodsReceiptDto, ReceiptDetailResponse>(WebOperationKind.ReceiptUpdate, receiptId,
            async context => await mediator.Send(new UpdateGoodsReceiptDraftCommand(ReceiptHttpMapper.ToApplication(receiptId, request), context),
                cancellationToken).ConfigureAwait(false), ReceiptHttpMapper.Detail, cancellationToken);

    [HttpPost("{receiptId}/post")]
    [V1WriteMutation]
    [ProducesResponseType(typeof(V1SuccessEnvelope<ReceiptPostResponse>), 200)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 404)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 409)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 410)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 503)]
    public Task<IActionResult> Post([FromRoute(Name = "receiptId")] long receiptId, [FromBody] ReceiptPostRequest request,
        CancellationToken cancellationToken) =>
        ExecuteMutation<PostGoodsReceiptResponse, ReceiptPostResponse>(WebOperationKind.ReceiptPost, receiptId,
            async context => await mediator.Send(new PostGoodsReceiptCommand(ReceiptHttpMapper.ToApplication(receiptId, request), context),
                cancellationToken).ConfigureAwait(false), ReceiptHttpMapper.Post, cancellationToken);

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
