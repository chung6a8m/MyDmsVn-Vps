using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using MyDmsVn.Contracts;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.Application.Identity;
using MyDmsVn.Server.Application.Security;

namespace MyDmsVn.Server.HttpHost.Features.Auth;

public sealed record AuthenticatedResponse(string AccessToken, string ExpiresAtUtc, CurrentUserDto User);
public sealed record CapabilitiesResponse(bool Read, bool? Write = null, bool? Post = null);
public sealed record LogoutResponse;

[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
[ProducesResponseType(typeof(V1ErrorEnvelope), 400)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 401)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 403)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 413)]
[ProducesResponseType(typeof(V1ErrorEnvelope), 500)]
public sealed class AuthController(IMediator mediator, IWebAuthSessionStore sessions, IUtcClock clock,
    IPermissionAuthorizationService permissions, IRequestCorrelationAccessor correlation,
    ISecurityAuditWriter audit, WebHostOptions options, INativeAuthAttemptAdmission limiter) : ControllerBase
{
    private const string CookieName = "__Host-mydmsvn-rt";

    [HttpPost("login")]
    [AuthLoginRequest]
    [ProducesResponseType(typeof(V1SuccessEnvelope<AuthenticatedResponse>), 200)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 429)]
    public async Task<IActionResult> Login([FromBody] LoginBody credentials, CancellationToken cancellationToken)
    {
        if (!NoQueryOrTokenHeaders()) return Invalid();
        if (!Admit("login", UsernameNormalizer.Normalize(credentials.Username)))
            return await Throttled(cancellationToken).ConfigureAwait(false);
        var result = await mediator.Send(new LoginCommand(credentials.Username, credentials.Password), cancellationToken).ConfigureAwait(false);
        if (result.IsError) return Mapped(ApiResponseMapper.Map(result, null, correlation.CorrelationId));
        var session = await sessions.IssueAsync(result.Value.UserId, cancellationToken).ConfigureAwait(false);
        return session == null ? AuthenticationRequired() : Authenticated(session);
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(V1SuccessEnvelope<AuthenticatedResponse>), 200)]
    [ProducesResponseType(typeof(V1ErrorEnvelope), 429)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        if (!NoQueryOrTokenHeaders() || !await EmptyBody(cancellationToken).ConfigureAwait(false)) return Invalid();
        var cookie = ReadCookie();
        if (!Admit("refresh", cookie ?? "missing-or-invalid")) return await Throttled(cancellationToken).ConfigureAwait(false);
        if (cookie == null)
        {
            await Audit("WebAuthRefresh", SecurityAuditOutcome.Denied, null, false, cancellationToken).ConfigureAwait(false);
            return AuthenticationRequired();
        }
        var session = await sessions.RotateAsync(cookie, cancellationToken).ConfigureAwait(false);
        // Rotation may already be committed; do not cancel its success audit.
        await Audit("WebAuthRefresh", session == null ? SecurityAuditOutcome.Denied : SecurityAuditOutcome.Succeeded,
            session?.User.UserId, false, session == null ? cancellationToken : CancellationToken.None).ConfigureAwait(false);
        return session == null ? AuthenticationRequired() : Authenticated(session);
    }

    [HttpPost("logout")]
    [ProducesResponseType(typeof(V1SuccessEnvelope<LogoutResponse>), 200)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (!NoQueryOrTokenHeaders() || !await EmptyBody(cancellationToken).ConfigureAwait(false)) return Invalid();
        var cookie = ReadCookie();
        if (cookie != null) await sessions.RevokeAsync(cookie, cancellationToken).ConfigureAwait(false);
        await Audit("WebAuthLogout", SecurityAuditOutcome.Succeeded, null, cookie != null,
            cookie == null ? cancellationToken : CancellationToken.None, requiresAdmission: false).ConfigureAwait(false);
        WriteCookie(null);
        return Success(new LogoutResponse());
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(V1SuccessEnvelope<CurrentUserDto>), 200)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        if (!NoQueryOrTokenHeaders() || !await EmptyBody(cancellationToken).ConfigureAwait(false)) return Invalid();
        return Mapped(ApiResponseMapper.Map(await mediator.Send(new GetCurrentUserQuery(), cancellationToken)
            .ConfigureAwait(false), null, correlation.CorrelationId));
    }

    [HttpGet("capabilities")]
    [ProducesResponseType(typeof(V1SuccessEnvelope<CapabilitiesResponse>), 200)]
    public async Task<IActionResult> Capabilities([FromQuery] string? feature, CancellationToken cancellationToken)
    {
        // The raw historical query grammar is authoritative, not a binder
        // which could collapse duplicate, case-insensitive or decoded keys.
        _ = feature;
        if (HasTokenHeaders() || !await EmptyBody(cancellationToken).ConfigureAwait(false)) return Invalid();
        string permission;
        switch (RawQuery)
        {
            case "?feature=sales": permission = "Sales.Receipts"; break;
            case "?feature=goods-receipts": permission = "Inventory.GoodsReceipts"; break;
            case "?feature=inventory-balances":
                return Success(new CapabilitiesResponse(await permissions.IsAllowedAsync("Inventory.Balances.Read", cancellationToken).ConfigureAwait(false)));
            case "?feature=inventory-stock-card":
                return Success(new CapabilitiesResponse(await permissions.IsAllowedAsync("Inventory.StockCard.Read", cancellationToken).ConfigureAwait(false)));
            default: return Invalid();
        }
        var read = await permissions.IsAllowedAsync(permission + ".Read", cancellationToken).ConfigureAwait(false);
        var write = await permissions.IsAllowedAsync(permission + ".Write", cancellationToken).ConfigureAwait(false);
        var post = await permissions.IsAllowedAsync(permission + ".Post", cancellationToken).ConfigureAwait(false);
        return Success(new CapabilitiesResponse(read, write, post));
    }

    // Preserve the existing escaped URI query representation without creating
    // an HttpRequestMessage or dispatching through the compatibility bridge.
    private string RawQuery => new Uri(Request.GetEncodedUrl(), UriKind.Absolute).Query;
    private bool NoQueryOrTokenHeaders() => RawQuery.Length == 0 && !HasTokenHeaders();
    private bool HasTokenHeaders() => Request.Headers.ContainsKey("X-Refresh-Token") || Request.Headers.ContainsKey("Refresh-Token");
    private async Task<bool> EmptyBody(CancellationToken cancellationToken) =>
        await Request.Body.ReadAsync(new byte[1].AsMemory(), cancellationToken).ConfigureAwait(false) == 0;
    private bool Admit(string operation, string subject) =>
        limiter.TryAdmit(operation, subject, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", clock.UtcNow);

    private IActionResult Invalid() => V1ApiResults.Error(400, "ValidationError", "The request is invalid.", correlation.CorrelationId!);
    private IActionResult AuthenticationRequired() => V1ApiResults.Error(401, "Auth.Unauthorized", "Authentication is required.", correlation.CorrelationId!);
    private static IActionResult Success<T>(T value) => new JsonResult(new V1SuccessEnvelope<T>(value))
        { StatusCode = 200, ContentType = "application/json; charset=utf-8" };
    private static IActionResult Mapped<T>(ApiResponse<T> mapped) =>
        mapped.IsSuccess ? Success(mapped.Data!) : new JsonResult(new V1ErrorEnvelope(mapped.Error!))
        { StatusCode = (int)mapped.Error!.Status, ContentType = "application/json; charset=utf-8" };
    private Task Audit(string category, SecurityAuditOutcome outcome, int? userId, bool committed,
        CancellationToken cancellationToken, bool requiresAdmission = true) =>
        audit.WriteAsync(new SecurityAuditEntry(SecurityAuditAction.Authentication, outcome, userId, clock.UtcNow,
            category, requiresAdmission: requiresAdmission), committed, cancellationToken);

    private async Task<IActionResult> Throttled(CancellationToken cancellationToken)
    {
        await Audit("WebAuthThrottled", SecurityAuditOutcome.Denied, null, false, cancellationToken).ConfigureAwait(false);
        Response.Headers["Retry-After"] = "300";
        return V1ApiResults.Error(429, "Auth.RateLimited", "Too many authentication attempts. Try again later.", correlation.CorrelationId!);
    }

    private string? ReadCookie()
    {
        var values = CoreApiBoundary.HeaderValues(HttpContext, "Cookie");
        if (values == null) return null;
        var matches = values.SelectMany(v => v.Split(';')).Select(v => v.Trim())
            .Where(v => v.StartsWith(CookieName + "=", StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) return null;
        var token = matches[0].Substring(CookieName.Length + 1);
        return Guid.TryParseExact(token, "D", out var guid) && guid != Guid.Empty &&
            token == guid.ToString("D") && token[14] == '4' && "89ab".Contains(token[19]) ? token : null;
    }

    private IActionResult Authenticated(WebAuthSessionToken session)
    {
        var token = new AccessTokenIssuer(options, clock).Issue(session);
        var user = session.User;
        var response = new AuthenticatedResponse(token.Value,
            token.ExpiresAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            new CurrentUserDto(user.UserId!.Value, user.Username!, user.DisplayName!));
        WriteCookie(session);
        return Success(response);
    }

    private void WriteCookie(WebAuthSessionToken? session)
    {
        var seconds = session == null ? 0 : Math.Max(0, (long)Math.Floor((session.ExpiresAtUtc - clock.UtcNow).TotalSeconds));
        Response.Headers["Set-Cookie"] = CookieName + "=" + (session?.RefreshToken ?? "") + "; Max-Age=" +
            seconds.ToString(CultureInfo.InvariantCulture) + "; Path=/; Secure; HttpOnly; SameSite=Strict";
    }
}
