using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MyDmsVn.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MyDmsVn.Desktop.Infrastructure.Http
{
    /// <summary>Conservative WEB4/WEB5 journal boundary: ambiguous dispatch must be resolved, never retried.</summary>
    public sealed class HttpDesktopOperations
    {
        private readonly HttpDesktopTransport _transport;
        private readonly HttpDesktopSession _session;
        private readonly HttpDesktopOperationSettings _settings;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private PendingOperation? _pending;

        public HttpDesktopOperations(HttpDesktopTransport transport,
            HttpDesktopSession session, HttpDesktopOperationSettings settings)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public string? PendingOperationId
        {
            get { return _pending?.OperationId; }
        }

        public async Task<ApiResponse<T>> SendAsync<T>(
            string kind, long? target, HttpMethod method, string route,
            JObject body, long? expectedVersion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_pending != null)
                    return Pending<T>();
                if (!_session.TryCapture(expectedVersion, out var generation,
                    out _, out _))
                    return HttpDesktopTransport.SessionChanged<T>();

                var keyed = _settings.Mode != HttpDesktopOperationMode.LegacyWeb11;
                string? epoch = null;
                string? fingerprint = null;
                string operationId = Guid.NewGuid().ToString("D");
                if (keyed)
                {
                    // WEB5 must have the v2 contract, not merely a WEB4-compatible
                    // v1 endpoint. A mode mismatch is a hard admission failure.
                    var web5 = _settings.Mode == HttpDesktopOperationMode.Web5;
                    var context = await _transport.SendAsync<JObject>(HttpMethod.Get,
                        web5 ? "/api/v2/operations/context" : "/api/v1/operations/context",
                        null, cancellationToken, generation).ConfigureAwait(false);
                    if (!context.IsSuccess)
                        return ApiResponse<T>.Failure(context.Error!);
                    epoch = (string?)context.Data!["deploymentEpoch"];
                    var contract = (string?)context.Data["contractVersion"];
                    var actorId = (int?)context.Data["actorUserId"];
                    if (!Guid.TryParseExact(epoch, "D", out var parsedEpoch) ||
                        epoch != parsedEpoch.ToString("D") ||
                        (web5
                            ? contract != "web5-resolution-v1" ||
                              (bool?)context.Data["sameKeyResendEnabled"] != false
                            : contract != "web4-v1") ||
                        actorId != _session.CurrentUser?.UserId)
                        return Fail<T>("Operation.InvalidContext",
                            "The server operation contract is not compatible.");
                    try
                    {
                        fingerprint = HttpDesktopOperationFingerprint.Compute(kind, target, body);
                    }
                    catch (Exception exception) when (exception is ArgumentException ||
                        exception is FormatException || exception is OverflowException)
                    {
                        return Fail<T>("Operation.InvalidRequest",
                            "The operation body cannot be encoded safely.");
                    }
                }

                var scope = kind switch
                {
                    "Sales.Create" => "/api/v1/sales-receipts",
                    "Sales.Update" => "/api/v1/sales-receipts/{saleId}/draft",
                    "Sales.Post" => "/api/v1/sales-receipts/{saleId}/post",
                    "Receipt.Create" => "/api/v1/goods-receipts",
                    "Receipt.Update" => "/api/v1/goods-receipts/{receiptId}/draft",
                    "Receipt.Post" => "/api/v1/goods-receipts/{receiptId}/post",
                    _ => throw new ArgumentException("Unknown operation kind.", nameof(kind)),
                };
                var headers = keyed
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["Idempotency-Key"] = operationId,
                        ["X-MyDmsVn-Deployment-Epoch"] = epoch!,
                    }
                    : null;

                // Set before dispatch. Cancellation/timeout/connection loss may
                // occur after the server has committed and before an ACK arrives.
                var pending = new PendingOperation(
                    generation, operationId, epoch, fingerprint, kind, scope, target);
                _pending = pending;
                ApiResponse<T> result;
                try
                {
                    result = await _transport.SendAsync<T>(method, route, body,
                        cancellationToken, generation, headers).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw; // Retain pending record: outcome is not known.
                }

                if (!result.IsSuccess && Uncertain(result.Error!))
                    return Unknown<T>(pending);

                _pending = null;
                return result;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Read a frozen outcome under the current actor/epoch/grant.
        /// COMMITTED and matching original envelope are the ONLY positive resolution.
        /// 404/503/timeout never mean NOT_EXECUTED.
        /// </summary>
        public async Task<ApiResponse<T>> ResolvePendingAsync<T>(
            CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var pending = _pending;
                if (pending == null)
                    return Fail<T>("Operation.NoPending", "There is no pending operation.");
                if (pending.Epoch == null || pending.Fingerprint == null)
                    return Unknown<T>(pending); // Legacy host has no status readback.

                var headers = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["X-MyDmsVn-Deployment-Epoch"] = pending.Epoch,
                    ["X-MyDmsVn-Operation-Fingerprint"] = pending.Fingerprint,
                };
                var result = await _transport.SendAsync<JObject>(HttpMethod.Get,
                    "/api/v1/operations/" + pending.OperationId, null,
                    cancellationToken, pending.Generation, headers).ConfigureAwait(false);

                if (!result.IsSuccess)
                    return Unknown<T>(pending);
                var data = result.Data!;
                if ((string?)data["deploymentEpoch"] != pending.Epoch ||
                    (string?)data["operationId"] != pending.OperationId ||
                    (string?)data["operationKind"] != pending.Kind ||
                    (string?)data["routeScope"] != pending.RouteScope ||
                    (string?)data["requestFingerprint"] != pending.Fingerprint ||
                    (string?)data["requestTargetId"] != pending.Target?.ToString(CultureInfo.InvariantCulture) ||
                    (string?)data["state"] != "COMMITTED" ||
                    !(data["outcome"] is JObject outcome))
                    return Unknown<T>(pending);

                ApiResponse<T> decoded;
                try
                {
                    decoded = ApiJson.DeserializeResponse<T>(
                        HttpDesktopWire.NormalizeResponse<T>(outcome.ToString(Formatting.None)),
                        ApiStatusCode.Ok);
                }
                catch (Exception exception) when (exception is JsonException ||
                    exception is ArgumentException || exception is FormatException ||
                    exception is NullReferenceException || exception is OverflowException)
                {
                    return Unknown<T>(pending);
                }
                if (!decoded.IsSuccess) return Unknown<T>(pending);
                _pending = null;
                return decoded;
            }
            finally
            {
                _gate.Release();
            }
        }

        private static bool Uncertain(ApiError error) =>
            error.Status == ApiStatusCode.InternalServerError ||
            (int)error.Status == 503 ||
            error.Code == "Http.Timeout" ||
            error.Code == "Http.Unavailable" ||
            error.Code == "Http.InvalidResponse";

        private static ApiResponse<T> Pending<T>() =>
            Fail<T>("Operation.PendingRecovery",
                "A previous write has an unknown outcome. Resolve that write before continuing.");

        private static ApiResponse<T> Unknown<T>(PendingOperation pending) =>
            ApiResponse<T>.Failure(new ApiError(ApiStatusCode.Conflict,
                "Operation.OutcomeUnknown",
                "The write outcome is unknown. Do not resend this operation.",
                correlationId: pending.OperationId));

        private static ApiResponse<T> Fail<T>(string code, string message) =>
            HttpDesktopTransport.Fail<T>(ApiStatusCode.Conflict, code, message);

        private sealed class PendingOperation
        {
            public PendingOperation(long generation, string id, string? epoch,
                string? fingerprint, string kind, string scope, long? target)
            {
                Generation = generation; OperationId = id;
                Epoch = epoch; Fingerprint = fingerprint;
                Kind = kind; RouteScope = scope; Target = target;
            }
            public long Generation { get; }
            public string OperationId { get; }
            public string? Epoch { get; }
            public string? Fingerprint { get; }
            public string Kind { get; }
            public string RouteScope { get; }
            public long? Target { get; }
        }
    }
}
