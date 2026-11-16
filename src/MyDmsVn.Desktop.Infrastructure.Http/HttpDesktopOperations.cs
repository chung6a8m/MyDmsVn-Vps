using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyDmsVn.Contracts;
using MyDmsVn.Desktop.Application;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MyDmsVn.Desktop.Infrastructure.Http
{
    /// <summary>
    /// One Windows-owner-scoped durable write fence. No send or same-key replay can occur
    /// while a prior write is unresolved; even a process crash cannot release the fence.
    /// </summary>
    public sealed class HttpDesktopOperations : IDesktopWriteRecovery
    {
        private readonly HttpDesktopTransport _transport;
        private readonly HttpDesktopSession _session;
        private readonly HttpDesktopOperationSettings _settings;
        private readonly IHttpPendingWriteStore _store;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private HttpPendingWriteRecord? _pending;

        public HttpDesktopOperations(HttpDesktopTransport transport, HttpDesktopSession session,
            HttpDesktopOperationSettings settings, IHttpPendingWriteStore store)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _pending = _store.Load(); // fail closed on bad DPAPI / ACL / malformed data
        }

        public event EventHandler? PendingChanged;
        public string? PendingOperationId => Pending?.OperationId;

        public DesktopPendingWriteInfo? Pending
        {
            get
            {
                // Read through to detect another Desktop process; never trust a stale empty cache.
                var record = _store.Load();
                if (record == null) return null;
                return new DesktopPendingWriteInfo(record.OperationId, record.ActorUserId,
                    record.Protocol, record.Kind, record.Method, record.Route, record.TargetId,
                    record.DeploymentEpoch, record.Fingerprint, record.CreatedUtc);
            }
        }

        public async Task<ApiResponse<T>> SendAsync<T>(
            string kind, long? target, HttpMethod method, string route,
            JObject body, long? expectedVersion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (HasPending()) return PendingFailure<T>();
                if (!_session.TryCapture(expectedVersion, out var generation, out _, out _))
                    return HttpDesktopTransport.SessionChanged<T>();
                var actor = _session.CurrentUser?.UserId ?? 0;
                if (actor <= 0)
                    return HttpDesktopTransport.SessionChanged<T>();

                var protocol = _settings.Mode == HttpDesktopOperationMode.Web5 ? "web5" :
                    _settings.Mode == HttpDesktopOperationMode.Web4 ? "web4" : "legacy";
                string? epoch = null;
                string fingerprint;
                var scope = RouteScope(kind);
                if (protocol != "legacy")
                {
                    var web5 = protocol == "web5";
                    var context = await _transport.SendAsync<JObject>(HttpMethod.Get,
                        web5 ? "/api/v2/operations/context" : "/api/v1/operations/context",
                        null, cancellationToken, generation).ConfigureAwait(false);
                    if (!context.IsSuccess) return ApiResponse<T>.Failure(context.Error!);
                    epoch = (string?)context.Data!["deploymentEpoch"];
                    var contract = (string?)context.Data["contractVersion"];
                    var actorId = (int?)context.Data["actorUserId"];
                    if (!Guid.TryParseExact(epoch, "D", out var parsedEpoch) ||
                        parsedEpoch == Guid.Empty || epoch != parsedEpoch.ToString("D") ||
                        (web5 ? contract != "web5-resolution-v1" ||
                            (bool?)context.Data["sameKeyResendEnabled"] != false
                            : contract != "web4-v1") || actorId != actor)
                        return Fail<T>("Operation.InvalidContext",
                            "The server operation context is not compatible.");
                    try { fingerprint = HttpDesktopOperationFingerprint.Compute(kind, target, body); }
                    catch (Exception error) when (error is ArgumentException ||
                        error is FormatException || error is OverflowException)
                    {
                        return Fail<T>("Operation.InvalidRequest",
                            "The operation cannot be encoded safely.");
                    }
                }
                else
                    fingerprint = RequestFingerprint(method, route, body);

                var record = NewRecord(actor, protocol, kind, method, route, target, epoch, fingerprint);
                if (!Persist(record)) return Fail<T>("Operation.PersistenceUnavailable",
                    "The pending intent could not be preserved; the write was not dispatched.");
                var headers = protocol == "legacy" ? null :
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["Idempotency-Key"] = record.OperationId,
                        ["X-MyDmsVn-Deployment-Epoch"] = epoch!,
                    };
                return await DispatchAsync<T>(record, method, route, body, generation, headers, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }

        /// <summary>
        /// Catalog / Security writes have no server journal. Readback can assist an operator,
        /// but without the original body a GET is NOT a commit or rollback certificate.
        /// </summary>
        public async Task<ApiResponse<T>> SendUnjournaledAsync<T>(
            string kind, long? target, HttpMethod method, string route,
            object body, long? expectedVersion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (HasPending()) return PendingFailure<T>();
                if (!_session.TryCapture(expectedVersion, out var generation, out _, out _))
                    return HttpDesktopTransport.SessionChanged<T>();
                var actor = _session.CurrentUser?.UserId ?? 0;
                if (actor <= 0) return HttpDesktopTransport.SessionChanged<T>();
                var fingerprint = RequestFingerprint(method, route, body);
                var record = NewRecord(actor, "readback", kind, method, route, target, null, fingerprint);
                if (!Persist(record)) return Fail<T>("Operation.PersistenceUnavailable",
                    "The pending intent could not be preserved; the write was not dispatched.");
                return await DispatchAsync<T>(record, method, route, body, generation, null, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }

        /// <summary>
        /// All failures after dispatch are ambiguous unless accompanied by an independently
        /// verified terminal certificate. In particular 401/403/404/409/500/503/timeout
        /// MUST NOT erase a durable fence just because a response was received.
        /// </summary>
        private async Task<ApiResponse<T>> DispatchAsync<T>(
            HttpPendingWriteRecord record, HttpMethod method, string route, object body,
            long generation, IReadOnlyDictionary<string, string>? headers, CancellationToken token)
        {
            ApiResponse<T> result;
            try
            {
                result = await _transport.SendAsync<T>(method, route, body, token,
                    generation, headers).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw; // commit could have happened before cancellation
            }

            if (!result.IsSuccess) return Unknown<T>(record);
            Clear(record); // no silent success if the file cannot be removed
            return result;
        }

        /// <summary>Only a fully matching, retained COMMITTED journal outcome can recover a success.</summary>
        public async Task<ApiResponse<T>> ResolvePendingAsync<T>(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var record = Current();
                if (record == null) return Fail<T>("Operation.NoPending", "No pending write exists.");
                if (record.Protocol != "web4" && record.Protocol != "web5") return Unknown<T>(record);
                var result = await ReadJournalAsync(record, cancellationToken).ConfigureAwait(false);
                if (result == null || (string?)result["state"] != "COMMITTED" ||
                    !(result["outcome"] is JObject outcome))
                    return Unknown<T>(record);
                ApiResponse<T> decoded;
                try
                {
                    var raw = HttpDesktopWire.NormalizeResponse<T>(outcome.ToString(Formatting.None));
                    decoded = ApiJson.DeserializeResponse<T>(raw, ApiStatusCode.Ok);
                    if (!decoded.IsSuccess || !ValidResultId(record, outcome))
                        return Unknown<T>(record);
                }
                catch (Exception error) when (error is JsonException || error is ArgumentException ||
                    error is FormatException || error is NullReferenceException || error is OverflowException)
                { return Unknown<T>(record); }

                Clear(record);
                return decoded;
            }
            finally { _gate.Release(); }
        }

        public async Task<DesktopWriteRecoveryResult> CheckAsync(CancellationToken cancellationToken)
        {
            var snapshot = Pending;
            if (snapshot == null)
                return new DesktopWriteRecoveryResult("CLEAR", "There is no pending write.", true);
            if (_session.CurrentUser?.UserId != snapshot.ActorUserId || !_session.IsAuthenticated)
                return new DesktopWriteRecoveryResult("UNKNOWN",
                    "Sign in as the original active actor with the original operation grants. Do not post again.");
            if (snapshot.Protocol == "readback")
                return await ReadbackAsync(snapshot, cancellationToken).ConfigureAwait(false);
            if (snapshot.Protocol == "legacy")
                return new DesktopWriteRecoveryResult("UNKNOWN",
                    "Legacy writes have no journal. Authorized operator reconciliation is required.");

            var resolved = await ResolvePendingAsync<JObject>(cancellationToken).ConfigureAwait(false);
            if (resolved.IsSuccess)
                return new DesktopWriteRecoveryResult("COMMITTED",
                    "Exact retained journal result matched. The fence was cleared. Refresh the document from the server.",
                    true);
            var record = Current();
            if (record == null)
                return new DesktopWriteRecoveryResult("CLEAR", "The pending write has been cleared.", true);
            var envelope = await ReadJournalAsync(record, cancellationToken).ConfigureAwait(false);
            if (envelope != null && (string?)envelope["state"] == "NOT_EXECUTED" &&
                envelope["terminal"] is JObject terminal && ValidTerminal(terminal))
                return new DesktopWriteRecoveryResult("NOT_EXECUTED",
                    "An authenticated WEB5 terminal audit certificate exists. Confirm it explicitly before starting a NEW intent.",
                    false, (string?)terminal["caseId"]);
            return new DesktopWriteRecoveryResult("UNKNOWN",
                "No exact committed result or terminal certificate is available. Preserve the fence and escalate.");
        }

        public async Task<DesktopWriteRecoveryResult> AcceptCertifiedNotExecutedAsync(
            CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var record = Current();
                if (record == null)
                    return new DesktopWriteRecoveryResult("CLEAR", "No pending write exists.", true);
                if (record.Protocol != "web5")
                    return new DesktopWriteRecoveryResult("UNKNOWN", "Only WEB5 has an audited negative certificate.");
                var data = await ReadJournalAsync(record, cancellationToken).ConfigureAwait(false);
                if (data == null || (string?)data["state"] != "NOT_EXECUTED" ||
                    data["outcome"]?.Type != JTokenType.Null ||
                    !(data["terminal"] is JObject terminal) || !ValidTerminal(terminal))
                    return new DesktopWriteRecoveryResult("UNKNOWN",
                        "The original exact NOT_EXECUTED certificate cannot be authenticated.");
                var caseId = (string?)terminal["caseId"];
                Clear(record);
                return new DesktopWriteRecoveryResult("NOT_EXECUTED",
                    "The original intent is certified NOT_EXECUTED. Obtain a fresh authorized baseline and create a NEW key; never resend the old one.",
                    true, caseId);
            }
            finally { _gate.Release(); }
        }

        private async Task<DesktopWriteRecoveryResult> ReadbackAsync(
            DesktopPendingWriteInfo record, CancellationToken cancellationToken)
        {
            var route = record.Route;
            if (record.Kind.StartsWith("Catalog.", StringComparison.Ordinal) &&
                record.TargetId.HasValue)
            {
                if (route.EndsWith("/active", StringComparison.Ordinal))
                    route = route.Substring(0, route.Length - "/active".Length);
                var result = await _transport.SendAsync<JObject>(HttpMethod.Get,
                    route, null, cancellationToken).ConfigureAwait(false);
                if (result.IsSuccess)
                    return new DesktopWriteRecoveryResult("READBACK_OBSERVED",
                        "Authorized catalog readback succeeded. Compare the exact original intended change with server state and retain the fence pending an audited operator decision.");
            }
            else if (record.Kind.StartsWith("Security.", StringComparison.Ordinal))
            {
                var users = await _transport.SendAsync<JToken>(HttpMethod.Get,
                    "/api/v1/security/users", null, cancellationToken).ConfigureAwait(false);
                var roles = await _transport.SendAsync<JToken>(HttpMethod.Get,
                    "/api/v1/security/roles", null, cancellationToken).ConfigureAwait(false);
                if (users.IsSuccess && roles.IsSuccess)
                    return new DesktopWriteRecoveryResult("READBACK_OBSERVED",
                        "Authorized security readback succeeded. Compare authoritative assignments and revisions; an audited operator decision is still required.");
            }
            return new DesktopWriteRecoveryResult("UNKNOWN",
                "Readback unavailable, insufficient, or unauthorized. The fence remains in force.");
        }

        private async Task<JObject?> ReadJournalAsync(
            HttpPendingWriteRecord record, CancellationToken cancellationToken)
        {
            if (_session.CurrentUser?.UserId != record.ActorUserId ||
                !_session.TryCapture(null, out var generation, out _, out _))
                return null;
            var route = record.Protocol == "web5"
                ? "/api/v2/operations/" + record.OperationId + "/resolution"
                : "/api/v1/operations/" + record.OperationId;
            var headers = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["X-MyDmsVn-Deployment-Epoch"] = record.DeploymentEpoch!,
                ["X-MyDmsVn-Operation-Fingerprint"] = record.Fingerprint,
            };
            var status = await _transport.SendAsync<JObject>(HttpMethod.Get, route, null,
                cancellationToken, generation, headers).ConfigureAwait(false);
            if (!status.IsSuccess || status.Data == null) return null;
            var data = status.Data;
            if ((string?)data["deploymentEpoch"] != record.DeploymentEpoch ||
                (string?)data["operationId"] != record.OperationId ||
                (string?)data["operationKind"] != record.Kind ||
                (string?)data["method"] != record.Method ||
                (string?)data["routeScope"] != RouteScope(record.Kind) ||
                (string?)data["requestFingerprint"] != record.Fingerprint ||
                (string?)data["requestTargetId"] != record.TargetId?.ToString(CultureInfo.InvariantCulture))
                return null;
            if (record.Protocol == "web5" &&
                ((string?)data["contractVersion"] != "web5-resolution-v1" ||
                 (int?)data["actorUserId"] != record.ActorUserId))
                return null;
            return data;
        }

        private static bool ValidResultId(HttpPendingWriteRecord record, JObject envelope)
        {
            if (!(envelope["data"] is JObject data)) return false;
            var field = record.Kind.StartsWith("Sales.", StringComparison.Ordinal) ? "saleId" : "receiptId";
            var text = (string?)data[field];
            if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                return false;
            return !record.TargetId.HasValue || record.TargetId.Value == id;
        }

        private static bool ValidTerminal(JObject terminal)
        {
            if (terminal.Count != 3) return false;
            var decision = (string?)terminal["decisionId"];
            var caseId = (string?)terminal["caseId"];
            var date = (string?)terminal["decidedAtUtc"];
            if (!Guid.TryParseExact(decision, "D", out var guid) ||
                guid.ToString("D") != decision || decision![14] != '4' ||
                "89ab".IndexOf(decision[19]) < 0 ||
                string.IsNullOrEmpty(caseId) || caseId!.Length > 128 ||
                caseId.Any(c => !(char.IsLetterOrDigit(c) && c <= 127 || c == '_' || c == '-')))
                return false;
            return DateTime.TryParseExact(date, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var when) && when.Kind == DateTimeKind.Utc && when <= DateTime.UtcNow.AddMinutes(5);
        }

        private static HttpPendingWriteRecord NewRecord(int actor, string protocol, string kind,
            HttpMethod method, string route, long? target, string? epoch, string fingerprint) =>
            new HttpPendingWriteRecord
            {
                ActorUserId = actor, OperationId = Guid.NewGuid().ToString("D"),
                Protocol = protocol, Kind = kind, Method = method.Method,
                Route = route, TargetId = target, DeploymentEpoch = epoch,
                Fingerprint = fingerprint, CreatedUtc = DateTime.UtcNow,
            };

        private static string RequestFingerprint(HttpMethod method, string route, object body)
        {
            var value = method.Method + "\n" + route + "\n" +
                JsonConvert.SerializeObject(body, ApiJson.CreateSerializerSettings());
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value)))
                    .Replace("-", "").ToLowerInvariant();
        }

        private bool HasPending()
        {
            _pending = _store.Load();
            return _pending != null;
        }

        private HttpPendingWriteRecord? Current()
        {
            _pending = _store.Load();
            return _pending;
        }

        private bool Persist(HttpPendingWriteRecord record)
        {
            try { _store.Save(record); }
            catch (Exception error) when (error is IOException ||
                error is UnauthorizedAccessException || error is InvalidOperationException ||
                error is CryptographicException)
            { return false; }
            _pending = record;
            PendingChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private void Clear(HttpPendingWriteRecord record)
        {
            _store.Clear(record);
            _pending = null;
            PendingChanged?.Invoke(this, EventArgs.Empty);
        }

        private static string RouteScope(string kind) => kind switch
        {
            "Sales.Create" => "/api/v1/sales-receipts",
            "Sales.Update" => "/api/v1/sales-receipts/{saleId}/draft",
            "Sales.Post" => "/api/v1/sales-receipts/{saleId}/post",
            "Receipt.Create" => "/api/v1/goods-receipts",
            "Receipt.Update" => "/api/v1/goods-receipts/{receiptId}/draft",
            "Receipt.Post" => "/api/v1/goods-receipts/{receiptId}/post",
            _ => throw new ArgumentException("Unknown journal operation kind.", nameof(kind)),
        };

        private static ApiResponse<T> PendingFailure<T>() =>
            Fail<T>("Operation.PendingRecovery",
                "An earlier write has an UNKNOWN outcome. Do not post again; inspect the recovery case.");

        private static ApiResponse<T> Unknown<T>(HttpPendingWriteRecord record) =>
            ApiResponse<T>.Failure(new ApiError(ApiStatusCode.Conflict, "Operation.OutcomeUnknown",
                "The outcome remains UNKNOWN. Never retry or resend the original write.",
                correlationId: record.OperationId));

        private static ApiResponse<T> Fail<T>(string code, string message) =>
            HttpDesktopTransport.Fail<T>(ApiStatusCode.Conflict, code, message);
    }
}
