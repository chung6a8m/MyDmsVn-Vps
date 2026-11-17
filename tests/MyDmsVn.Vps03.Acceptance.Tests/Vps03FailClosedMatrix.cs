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
using Newtonsoft.Json.Linq;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal static class Vps03FailClosedMatrix
{
    private const string Origin = "https://issue7-matrix.invalid";
    private const string Epoch = "11111111-2222-4333-8444-555555555555";

    internal static async Task<IReadOnlyList<Vps03FailClosedResult>> RunAsync(CancellationToken token)
    {
        var cases = new[]
        {
            "cancelled", "timeout", "missing", "expired", "unavailable", "indeterminate",
            "http-404", "http-500", "http-503", "actor-mismatch", "epoch-mismatch",
            "operation-mismatch", "fingerprint-mismatch", "kind-mismatch", "method-mismatch",
            "scope-mismatch", "target-mismatch", "contract-mismatch",
        };
        var results = new List<Vps03FailClosedResult>(cases.Length);
        foreach (var @case in cases)
            results.Add(await RunCaseAsync(@case, token).ConfigureAwait(false));
        return results;
    }

    private static async Task<Vps03FailClosedResult> RunCaseAsync(string @case, CancellationToken token)
    {
        var store = new Store(Record());
        var session = new HttpDesktopSession(DesktopSessionOptions.Default, new Clock());
        using var transport = new HttpDesktopTransport(new HttpDesktopOptions(Origin), session,
            new Handler((request, cancellation) => Reply(@case, store.Record, request, cancellation)));
        var operations = new HttpDesktopOperations(transport, session,
            new HttpDesktopOperationSettings(HttpDesktopOperationMode.Web5), store);
        var login = await transport.LoginAsync(new LoginRequest("actor", "bounded"), token)
            .ConfigureAwait(false);
        if (!login.IsSuccess) throw new InvalidOperationException("VPS03.MatrixLoginFailed");
        DesktopWriteRecoveryResult result;
        try { result = await operations.CheckAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (@case == "cancelled")
        { result = new DesktopWriteRecoveryResult("UNKNOWN", "Cancellation retained the fence."); }
        if (store.Load() == null || result.FenceReleased)
            throw new InvalidOperationException("VPS03.MatrixFenceReleased:" + @case);
        return new Vps03FailClosedResult(@case, "UNKNOWN", false);
    }

    private static Task<HttpResponseMessage> Reply(string @case, HttpPendingWriteRecord record,
        HttpRequestMessage request, CancellationToken token)
    {
        if (request.RequestUri!.AbsolutePath == "/api/v1/auth/login")
            return Task.FromResult(Json(HttpStatusCode.OK,
                "{\"data\":{\"accessToken\":\"test.token.value\",\"expiresAtUtc\":\"2099-01-01T00:00:00Z\",\"user\":{\"userId\":7303,\"username\":\"actor\",\"displayName\":\"Actor\"}}}"));
        if (@case == "cancelled") throw new OperationCanceledException(token);
        if (@case == "timeout") throw new HttpRequestException("deterministic timeout");
        if (@case == "missing" || @case == "http-404")
            return Task.FromResult(Json(HttpStatusCode.NotFound, Error("Operation.NotObserved")));
        if (@case == "http-500") return Task.FromResult(Json(HttpStatusCode.InternalServerError, Error("Server.Error")));
        if (@case == "http-503") return Task.FromResult(Json(HttpStatusCode.ServiceUnavailable, Error("Server.Unavailable")));
        var state = @case == "expired" ? "RESULT_EXPIRED" :
            @case == "unavailable" ? "RESULT_UNAVAILABLE" : "INDETERMINATE";
        var actor = @case == "actor-mismatch" ? 7304 : record.ActorUserId;
        var epoch = @case == "epoch-mismatch" ? Guid.NewGuid().ToString("D") : record.DeploymentEpoch;
        var operation = @case == "operation-mismatch" ? Guid.NewGuid().ToString("D") : record.OperationId;
        var fingerprint = @case == "fingerprint-mismatch" ? new string('b', 64) : record.Fingerprint;
        var kind = @case == "kind-mismatch" ? "Receipt.Post" : record.Kind;
        var method = @case == "method-mismatch" ? "PUT" : record.Method;
        var scope = @case == "scope-mismatch" ? "/api/v1/receipts/{saleId}/post" :
            "/api/v1/sales-receipts/{saleId}/post";
        var target = @case == "target-mismatch" ? "7304" : "7303";
        var contract = @case == "contract-mismatch" ? "web5-resolution-v2" : "web5-resolution-v1";
        var body = "{\"data\":{\"contractVersion\":\"" + contract + "\",\"actorUserId\":" +
            actor.ToString(CultureInfo.InvariantCulture) + ",\"deploymentEpoch\":\"" + epoch +
            "\",\"operationId\":\"" + operation + "\",\"operationKind\":\"" + kind +
            "\",\"method\":\"" + method + "\",\"routeScope\":\"" + scope + "\"," +
            "\"requestTargetId\":\"" + target + "\",\"requestFingerprint\":\"" + fingerprint +
            "\",\"state\":\"" + state + "\",\"outcome\":null,\"terminal\":null}}";
        return Task.FromResult(Json(HttpStatusCode.OK, body));
    }

    private static string Error(string code) =>
        "{\"error\":{\"code\":\"" + code + "\",\"message\":\"Unavailable\",\"details\":[]}}";

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpPendingWriteRecord Record() => new()
    {
        OperationId = Guid.NewGuid().ToString("D"), ActorUserId = 7303,
        Protocol = "web5", Kind = "Sales.Post", Method = "POST",
        Route = "/api/v1/sales-receipts/7303/post", TargetId = 7303,
        DeploymentEpoch = Epoch, Fingerprint = new string('a', 64), CreatedUtc = DateTime.UtcNow,
    };

    private sealed class Store : IHttpPendingWriteStore
    {
        private HttpPendingWriteRecord? _record;
        internal Store(HttpPendingWriteRecord record) => _record = record;
        internal HttpPendingWriteRecord Record => _record!;
        public HttpPendingWriteRecord? Load() => _record;
        public void Save(HttpPendingWriteRecord record) => throw new InvalidOperationException();
        public void Clear(HttpPendingWriteRecord expected) => _record = null;
    }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _reply;
        internal Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) => _reply = reply;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _reply(request, cancellationToken);
    }

    private sealed class Clock : IMonotonicClock { public TimeSpan Elapsed => TimeSpan.Zero; }
}
