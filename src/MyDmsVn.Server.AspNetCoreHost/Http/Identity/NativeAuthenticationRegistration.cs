using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Server.Application.Identity;

namespace MyDmsVn.Server.HttpHost;

// The existing native admission middleware is the sole authentication owner.
// A second ASP.NET authentication handler or [Authorize] would either repeat
// SQL session validation or alter anonymous-cookie/health/fallback precedence.
// Resolve the existing scoped SQL store only after a valid bearer signature.
internal static class NativeAuthenticationRegistration
{
    internal sealed record AdmittedSession(CurrentUser User, Guid SessionId);

    internal static async Task<AdmittedSession?> ValidateAsync(
        HttpContext context, WebHostOptions options, IList<string>? authorization)
    {
        if (authorization == null || authorization.Count != 1) return null;
        var text = authorization[0];
        if (!text.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || text.Length <= 7) return null;
        var token = text.Substring(7);
        if (token.Any(c => char.IsWhiteSpace(c) || c == ',')) return null;
        var validated = new BearerTokenValidator(options, context.RequestServices.GetRequiredService<IUtcClock>()).Validate(token);
        if (validated == null) return null;
        var user = await context.RequestServices.GetRequiredService<IWebAuthSessionStore>()
            .GetActiveUserAsync(validated.SessionId, validated.UserId, context.RequestAborted).ConfigureAwait(false);
        return user == null || !user.IsAuthenticated || !user.IsActive || user.UserId != validated.UserId
            ? null : new AdmittedSession(user, validated.SessionId);
    }
}
