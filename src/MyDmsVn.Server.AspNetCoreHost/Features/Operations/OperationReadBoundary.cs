using System;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.Application.Identity;
using MyDmsVn.Server.Application.Operations;
using MyDmsVn.Server.Application.Persistence;

namespace MyDmsVn.Server.HttpHost.Features.Operations;

internal sealed record OperationKey(Guid OperationId, Guid Epoch, byte[] Fingerprint);
internal sealed record ResendKey(OperationKey Key, WebOperationKind Kind, long? Target, string? Receipt);

// Raw transport checks are deliberately stricter than MVC's lossy header
// aggregation and path normalization. The result passed to repositories is typed.
internal static class OperationRequest
{
    internal static void Path(HttpRequest request, string expected)
    {
        if (request.Method != "GET" || request.HttpContext.Features.Get<IHttpRequestFeature>()?.RawTarget != expected)
            throw new V1BindingException();
    }

    internal static OperationKey Key(HttpRequest request, string operationId, string prefix, string suffix = "")
    {
        var id = GuidValue(operationId, true);
        Path(request, prefix + id.ToString("D") + suffix);
        var epoch = GuidValue(Header(request, "X-MyDmsVn-Deployment-Epoch")!, false);
        var fingerprint = Header(request, "X-MyDmsVn-Operation-Fingerprint")!;
        if (!Regex.IsMatch(fingerprint, @"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant)) throw new V1BindingException();
        return new(id, epoch, Convert.FromHexString(fingerprint));
    }

    internal static ResendKey Resend(HttpRequest request, string operationId, bool eligibility)
    {
        var key = Key(request, operationId, "/api/v3/operations/", eligibility ? "/resend-eligibility" : "/resend-receipt");
        var kind = V1OperationCanonicalizer.ParseKind(Header(request, "X-MyDmsVn-Operation-Kind")!);
        var value = Header(request, "X-MyDmsVn-Operation-Target-Id", optional: true);
        var create = kind is WebOperationKind.SalesCreate or WebOperationKind.ReceiptCreate;
        long? target = null;
        if (create)
        {
            if (value != null) throw new V1BindingException();
        }
        else
        {
            if (value == null || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                parsed <= 0 || value != parsed.ToString(CultureInfo.InvariantCulture)) throw new V1BindingException();
            target = parsed;
        }
        return new(key, kind, target, eligibility ? Header(request, "X-MyDmsVn-Resend-Receipt") : null);
    }

    private static Guid GuidValue(string text, bool v4)
    {
        if (!Guid.TryParseExact(text, "D", out var value) || value == Guid.Empty || text != value.ToString("D") ||
            (v4 && (text[14] != '4' || "89ab".IndexOf(text[19]) < 0))) throw new V1BindingException();
        return value;
    }

    private static string? Header(HttpRequest request, string name, bool optional = false)
    {
        if (!request.Headers.TryGetValue(name, out var values))
        {
            if (optional) return null;
            throw new V1BindingException();
        }
        if (values.Count != 1 || values[0] == null || values[0]!.Contains(',') ||
            (optional && values[0]!.Length == 0)) throw new V1BindingException();
        return values[0];
    }
}

// Public type for MVC constructor injection; internal construction is registered
// explicitly so the internal single-assignment admission object is not exposed.
public sealed class OperationReadBoundary
{
    private readonly RequestIdentityAdmission _admission;
    private readonly IUnitOfWorkFactory _units;
    internal OperationReadBoundary(RequestIdentityAdmission admission, IUnitOfWorkFactory units, WebHostOptions options, IUtcClock clock)
    { _admission = admission; _units = units; Options = options; Clock = clock; }
    internal WebHostOptions Options { get; }
    internal IUtcClock Clock { get; }

    internal async Task<IActionResult> Run(bool enabled, Func<Task<IActionResult>> read, CancellationToken token)
    {
        var correlation = _admission.CorrelationId;
        if (!enabled) return V1ApiResults.Error(404, "NotFound", "The resource was not found.", correlation);
        try { return await read().ConfigureAwait(false); }
        catch (V1BindingException) { return Invalid(correlation); }
        catch (WebOperationException error) { return V1ApiResults.OperationError(error.Code, correlation); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return V1ApiResults.OperationError("Operation.Unavailable", correlation); }
    }

    private static IActionResult Invalid(string correlation) =>
        V1ApiResults.Error(400, "ValidationError", "The request is invalid.", correlation);

    internal async Task<IActionResult> Transaction<T>(HttpRequest request,
        Func<IUnitOfWork, int, Guid, Task<T>> read, CancellationToken token,
        Action<int>? prepare = null)
    {
        await V1QueryPolicy.RequireEmptyBodyAsync(request).ConfigureAwait(false);
        var identity = _admission;
        if (!identity.Current.IsAuthenticated || !identity.Current.IsActive || !identity.Current.UserId.HasValue || !identity.SessionId.HasValue)
            throw new WebOperationException("Auth.Unauthorized");
        // Validate the signed WEB6 receipt before acquiring the database transaction,
        // after body and admitted identity checks, as in the original protocol.
        prepare?.Invoke(identity.Current.UserId.Value);
        using var unit = await _units.CreateAsync(token).ConfigureAwait(false);
        unit.BeginTransaction();
        var value = await read(unit, identity.Current.UserId.Value, identity.SessionId.Value).ConfigureAwait(false);
        // Serialize while the transaction still owns epoch/actor/permission locks.
        var json = new JsonSerializerOptions();
        V1HttpJson.Configure(json);
        var body = JsonSerializer.Serialize(new V1SuccessEnvelope<T>(value), json);
        unit.Commit();
        return new ContentResult { Content = body, ContentType = "application/json; charset=utf-8", StatusCode = 200 };
    }

    internal static async Task<Guid> Epoch(IUnitOfWork unit, int actor, Guid session, CancellationToken token)
    {
        var epoch = await unit.Repository<IWebOperationReadRepository>().ReadContextAsync(actor, session, token).ConfigureAwait(false);
        if (epoch == Guid.Empty) throw new WebOperationException("Operation.Unavailable");
        return epoch;
    }
}
