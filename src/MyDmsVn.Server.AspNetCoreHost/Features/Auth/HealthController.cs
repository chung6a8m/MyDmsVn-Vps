using Microsoft.AspNetCore.Mvc;

namespace MyDmsVn.Server.HttpHost.Features.Auth;

public sealed record HealthResponse(string Status);

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class HealthController : ControllerBase
{
    // Transport, optional bearer and audit readiness have already run, with
    // identical precedence for canonical, case and trailing-slash variants.
    [HttpGet("health")]
    [ProducesResponseType(typeof(V1SuccessEnvelope<HealthResponse>), 200)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 400)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 401)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 403)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 413)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 503)]
    public IActionResult Get() => new JsonResult(new V1SuccessEnvelope<HealthResponse>(new HealthResponse("ok")))
        { StatusCode = 200, ContentType = "application/json; charset=utf-8" };
}
