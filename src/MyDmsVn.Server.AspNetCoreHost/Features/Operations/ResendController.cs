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
[Route("api/v3/operations")]
public sealed class ResendController(OperationReadBoundary boundary) : ControllerBase
{
    private bool Enabled => boundary.Options.Web5Enabled && boundary.Options.Web6ResendEligibilityEnabled && boundary.Options.Web6CandidateId != null;

    [HttpGet("resend-context", Order = 0)]
    [ProducesResponseType(typeof(V1SuccessEnvelope<ResendContextResponse>), 200)]
    public Task<IActionResult> Context(CancellationToken token) => boundary.Run(Enabled, () =>
    {
        OperationRequest.Path(Request, "/api/v3/operations/resend-context");
        return boundary.Transaction(Request, async (unit, actor, session) =>
            OperationHttpMapper.ResendContext(boundary.Options,
                await OperationReadBoundary.Epoch(unit, actor, session, token).ConfigureAwait(false), actor, boundary.Clock.UtcNow), token);
    }, token);

    [HttpGet("{operationId}/resend-receipt", Order = 1)]
    [ProducesResponseType(typeof(V1SuccessEnvelope<ResendReceiptResponse>), 200)]
    public Task<IActionResult> Receipt([FromRoute] string operationId, CancellationToken token,
        [FromHeader(Name = "X-MyDmsVn-Deployment-Epoch")] string? epoch = null,
        [FromHeader(Name = "X-MyDmsVn-Operation-Fingerprint")] string? fingerprint = null,
        [FromHeader(Name = "X-MyDmsVn-Operation-Kind")] string? kind = null,
        [FromHeader(Name = "X-MyDmsVn-Operation-Target-Id")] string? target = null) => boundary.Run(Enabled, () =>
    {
        var input = OperationRequest.Resend(Request, operationId, false);
        return boundary.Transaction(Request, async (unit, actor, session) =>
        {
            await unit.Repository<IWebOperationResendEligibilityRepository>().AuthorizeReceiptAsync(actor, session,
                input.Key.Epoch, input.Kind, token).ConfigureAwait(false);
            var identity = Identity(input, actor);
            var issued = new Web6ResendReceiptCodec(boundary.Options, boundary.Clock).Issue(identity);
            return OperationHttpMapper.Receipt(identity, issued);
        }, token);
    }, token);

    [HttpGet("{operationId}/resend-eligibility", Order = 2)]
    [ProducesResponseType(typeof(V1SuccessEnvelope<ResendEligibilityResponse>), 200)]
    public Task<IActionResult> Eligibility([FromRoute] string operationId, CancellationToken token,
        [FromHeader(Name = "X-MyDmsVn-Deployment-Epoch")] string? epoch = null,
        [FromHeader(Name = "X-MyDmsVn-Operation-Fingerprint")] string? fingerprint = null,
        [FromHeader(Name = "X-MyDmsVn-Operation-Kind")] string? kind = null,
        [FromHeader(Name = "X-MyDmsVn-Operation-Target-Id")] string? target = null,
        [FromHeader(Name = "X-MyDmsVn-Resend-Receipt")] string? receipt = null) => boundary.Run(Enabled, () =>
    {
        var input = OperationRequest.Resend(Request, operationId, true);
        Web6ResendIdentity? identity = null;
        ValidatedWeb6ResendReceipt? validated = null;
        return boundary.Transaction(Request, async (unit, actor, session) =>
        {
            var admittedIdentity = identity!;
            var admittedReceipt = validated!;
            var repository = unit.Repository<IWebOperationResendEligibilityRepository>();
            WebOperationResolution? result = null;
            if (admittedReceipt.IsExpired)
                await repository.AuthorizeReceiptAsync(actor, session, input.Key.Epoch, input.Kind, token).ConfigureAwait(false);
            else
                result = await repository.ReadEligibilityAsync(actor, session, input.Key.Epoch, input.Key.OperationId,
                    input.Kind, input.Target, input.Key.Fingerprint, token).ConfigureAwait(false);
            return OperationHttpMapper.Eligibility(admittedIdentity, admittedReceipt, result, boundary.Clock.UtcNow);
        }, token, actor =>
        {
            identity = Identity(input, actor);
            validated = new Web6ResendReceiptCodec(boundary.Options, boundary.Clock).Validate(input.Receipt!, identity);
        });
    }, token);

    private Web6ResendIdentity Identity(ResendKey input, int actor) =>
        new(actor, boundary.Options.Web6CandidateId!, input.Key.Epoch, input.Key.OperationId, input.Kind, input.Target, input.Key.Fingerprint);
}
