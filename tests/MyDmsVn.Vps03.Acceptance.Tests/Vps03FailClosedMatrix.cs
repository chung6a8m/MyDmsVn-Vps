using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyDmsVn.Contracts;
using MyDmsVn.Desktop.Application;
using MyDmsVn.Desktop.Infrastructure.Http;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal static class Vps03FailClosedMatrix
{
    internal static readonly string[] Cases =
    {
        "cancelled", "timeout", "missing", "expired", "unavailable", "indeterminate",
        "http-404", "http-500", "http-503", "actor-mismatch", "epoch-mismatch",
        "operation-mismatch", "fingerprint-mismatch", "kind-mismatch", "method-mismatch",
        "scope-mismatch", "target-mismatch", "contract-mismatch",
    };

    internal static async Task<IReadOnlyList<Vps03FailClosedResult>> RunAsync(
        Vps03OwnedEnvironment environment, Vps03RecoveryActor actor,
        WindowsHttpPendingWriteStore store, CancellationToken token)
    {
        var results = new List<Vps03FailClosedResult>(Cases.Length);
        foreach (var @case in Cases)
            results.Add(await RunCaseAsync(environment, actor, store, @case, token)
                .ConfigureAwait(false));
        return results;
    }

    private static async Task<Vps03FailClosedResult> RunCaseAsync(
        Vps03OwnedEnvironment environment, Vps03RecoveryActor actor,
        WindowsHttpPendingWriteStore store, string @case, CancellationToken token)
    {
        var record = store.Load() ?? throw new InvalidOperationException("VPS03.MatrixFenceMissing");
        var session = new HttpDesktopSession(DesktopSessionOptions.Default, new Clock());
        using var handler = new ResolutionMutationHandler(environment.Origin, @case, record);
        using var transport = new HttpDesktopTransport(
            new HttpDesktopOptions(environment.Origin.GetLeftPart(UriPartial.Authority)), session, handler);
        var operations = new HttpDesktopOperations(transport, session,
            new HttpDesktopOperationSettings(HttpDesktopOperationMode.Web5), store);
        var login = await transport.LoginAsync(new LoginRequest(actor.Username, actor.Password), token)
            .ConfigureAwait(false);
        if (!login.IsSuccess || login.Data?.UserId != actor.UserId)
            throw new InvalidOperationException("VPS03.MatrixLiveLoginFailed");
        DesktopWriteRecoveryResult result;
        try { result = await operations.CheckAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (@case == "cancelled")
        { result = new DesktopWriteRecoveryResult("UNKNOWN", "Cancellation retained the fence."); }
        catch (HttpRequestException) when (@case == "timeout")
        { result = new DesktopWriteRecoveryResult("UNKNOWN", "Transport failure retained the fence."); }
        var retained = store.Load();
        if (handler.UpstreamResolutionRequests == 0 || retained == null ||
            retained.OperationId != record.OperationId || retained.Fingerprint != record.Fingerprint ||
            result.FenceReleased)
            throw new InvalidOperationException("VPS03.MatrixFenceReleased:" + @case);
        return new Vps03FailClosedResult(@case, "UNKNOWN", false);
    }

    private sealed class ResolutionMutationHandler : DelegatingHandler
    {
        private readonly string _case;
        private readonly HttpPendingWriteRecord _record;
        private readonly string _route;
        private int _upstreamResolutionRequests;

        internal ResolutionMutationHandler(Uri origin, string @case, HttpPendingWriteRecord record)
            : base(Vps03LostAckProxy.CreatePassThrough())
        {
            if (origin.Scheme != Uri.UriSchemeHttps) throw new ArgumentException(nameof(origin));
            _case = @case;
            _record = record;
            _route = "/api/v2/operations/" + record.OperationId + "/resolution";
        }

        internal int UpstreamResolutionRequests => Volatile.Read(ref _upstreamResolutionRequests);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(request.RequestUri?.AbsolutePath, _route, StringComparison.Ordinal))
                return response;
            Interlocked.Increment(ref _upstreamResolutionRequests);
            response.Dispose();
            if (_case == "cancelled") throw new OperationCanceledException(cancellationToken);
            if (_case == "timeout") throw new HttpRequestException("VPS03 deterministic post-upstream timeout.");
            if (_case == "missing" || _case == "http-404")
                return Json(HttpStatusCode.NotFound, Error("Operation.NotObserved"));
            if (_case == "http-500")
                return Json(HttpStatusCode.InternalServerError, Error("Server.Error"));
            if (_case == "http-503")
                return Json(HttpStatusCode.ServiceUnavailable, Error("Server.Unavailable"));

            var state = _case == "expired" ? "RESULT_EXPIRED" :
                _case == "unavailable" ? "RESULT_UNAVAILABLE" : "INDETERMINATE";
            var actor = _case == "actor-mismatch" ? _record.ActorUserId + 1 : _record.ActorUserId;
            var epoch = _case == "epoch-mismatch" ? Guid.NewGuid().ToString("D") : _record.DeploymentEpoch;
            var operation = _case == "operation-mismatch" ? Guid.NewGuid().ToString("D") : _record.OperationId;
            var fingerprint = _case == "fingerprint-mismatch" ? new string('b', 64) : _record.Fingerprint;
            var kind = _case == "kind-mismatch" ? "Receipt.Post" : _record.Kind;
            var method = _case == "method-mismatch" ? "PUT" : _record.Method;
            var scope = _case == "scope-mismatch" ? "/api/v1/receipts/{saleId}/post" :
                "/api/v1/sales-receipts/{saleId}/post";
            var target = _case == "target-mismatch" ? (_record.TargetId!.Value + 1).ToString(CultureInfo.InvariantCulture) :
                _record.TargetId!.Value.ToString(CultureInfo.InvariantCulture);
            var contract = _case == "contract-mismatch" ? "web5-resolution-v2" : "web5-resolution-v1";
            var body = "{\"data\":{\"contractVersion\":\"" + contract + "\",\"actorUserId\":" +
                actor.ToString(CultureInfo.InvariantCulture) + ",\"deploymentEpoch\":\"" + epoch +
                "\",\"operationId\":\"" + operation + "\",\"operationKind\":\"" + kind +
                "\",\"method\":\"" + method + "\",\"routeScope\":\"" + scope + "\"," +
                "\"requestTargetId\":\"" + target + "\",\"requestFingerprint\":\"" + fingerprint +
                "\",\"state\":\"" + state + "\",\"outcome\":null,\"terminal\":null}}";
            return Json(HttpStatusCode.OK, body);
        }

        private static string Error(string code) =>
            "{\"error\":{\"code\":\"" + code + "\",\"message\":\"Unavailable\",\"details\":[]}}";

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class Clock : IMonotonicClock { public TimeSpan Elapsed => TimeSpan.Zero; }
}
