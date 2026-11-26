using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyDmsVn.Contracts;
using MyDmsVn.Desktop.Application;
using MyDmsVn.Desktop.Infrastructure.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MyDmsVn.Desktop.Session.Tests
{
    public sealed class Vps03PendingRecoveryTests
    {
        private const string Origin = "https://pending-recovery.example";
        private const string Epoch = "11111111-2222-4333-8444-555555555555";
        private const string Body = "{\"expectedVersion\":\"AAAAAAAAAAE=\"}";

        [Fact]
        public async Task Lost_ack_restart_second_process_is_fenced_until_exact_web5_committed_read()
        {
            var store = new PendingStore();
            var sent = 0;
            using (var first = new Fixture(store, (request, _) =>
            {
                if (IsLogin(request)) return Task.FromResult(Login(7));
                if (request.RequestUri!.AbsolutePath == "/api/v2/operations/context")
                    return Task.FromResult(Context());
                if (request.Method == HttpMethod.Post)
                {
                    sent++;
                    throw new HttpRequestException("ACK was lost after SQL commit");
                }
                throw new InvalidOperationException("Unexpected route.");
            }))
            {
                await first.SignIn();
                var response = await first.Operations.SendAsync<JObject>("Sales.Post", 1,
                    HttpMethod.Post, "/api/v1/sales-receipts/1/post",
                    JObject.Parse(Body), null, CancellationToken.None);
                Assert.Equal("Operation.OutcomeUnknown", response.Error!.Code);
                Assert.Equal(1, sent);
            }

            var original = store.Load()!;
            Assert.NotNull(original);
            using (var restarted = new Fixture(store, (request, _) =>
            {
                if (IsLogin(request)) return Task.FromResult(Login(7));
                if (request.RequestUri!.AbsolutePath.EndsWith("/resolution", StringComparison.Ordinal))
                    return Task.FromResult(Status(store.Load()!, "COMMITTED"));
                throw new InvalidOperationException("No business write or context request permitted.");
            }))
            {
                Assert.Equal(original.OperationId, restarted.Operations.PendingOperationId);
                await restarted.SignIn();
                var blocked = await restarted.Operations.SendAsync<JObject>("Sales.Post", 1,
                    HttpMethod.Post, "/api/v1/sales-receipts/1/post",
                    JObject.Parse(Body), null, CancellationToken.None);
                Assert.Equal("Operation.PendingRecovery", blocked.Error!.Code);
                var result = await restarted.Operations.CheckAsync(CancellationToken.None);
                Assert.Equal("COMMITTED", result.State);
                Assert.True(result.FenceReleased);
                Assert.Null(store.Load());
            }
            Assert.Equal(1, sent); // no double Post / simulated stock or COGS side effect
        }

        [Theory]
        [InlineData("RESULT_EXPIRED")]
        [InlineData("RESULT_UNAVAILABLE")]
        [InlineData("INDETERMINATE")]
        [InlineData("NOT_EXECUTED")]
        public async Task Noncommitted_or_expired_status_preserves_pending_fence(string state)
        {
            var store = PendingStore.Journal();
            using (var fixture = new Fixture(store, (request, _) =>
            {
                if (IsLogin(request)) return Task.FromResult(Login(7));
                if (request.RequestUri!.AbsolutePath.EndsWith("/resolution", StringComparison.Ordinal))
                    return Task.FromResult(Status(store.Load()!, state));
                throw new InvalidOperationException("A write must never be replayed.");
            }))
            {
                await fixture.SignIn();
                var response = await fixture.Operations.ResolvePendingAsync<JObject>(CancellationToken.None);
                Assert.Equal("Operation.OutcomeUnknown", response.Error!.Code);
                Assert.NotNull(store.Load());
            }
        }

        [Fact]
        public async Task Missing_status_and_different_actor_cannot_release_or_replay()
        {
            var store = PendingStore.Journal();
            var statusRequests = 0;
            using (var otherActor = new Fixture(store, (request, _) =>
            {
                if (IsLogin(request)) return Task.FromResult(Login(8));
                statusRequests++;
                return Task.FromResult(Json(HttpStatusCode.NotFound, "{\"error\":{\"code\":\"Operation.NotObserved\",\"message\":\"Absent\",\"details\":[]}}"));
            }))
            {
                await otherActor.SignIn();
                var result = await otherActor.Operations.CheckAsync(CancellationToken.None);
                Assert.Equal("UNKNOWN", result.State);
                Assert.Equal(0, statusRequests);
            }
            using (var originalActor = new Fixture(store, (request, _) =>
            {
                if (IsLogin(request)) return Task.FromResult(Login(7));
                statusRequests++;
                return Task.FromResult(Json(HttpStatusCode.NotFound, "{\"error\":{\"code\":\"Operation.NotObserved\",\"message\":\"Absent\",\"details\":[]}}"));
            }))
            {
                await originalActor.SignIn();
                var result = await originalActor.Operations.CheckAsync(CancellationToken.None);
                Assert.Equal("UNKNOWN", result.State);
                Assert.NotNull(store.Load());
                Assert.True(statusRequests > 0);
            }
        }

        [Fact]
        public async Task Web5_operator_terminal_certificate_requires_attended_fresh_read()
        {
            var store = PendingStore.Journal();
            var reads = 0;
            using (var fixture = new Fixture(store, (request, _) =>
            {
                if (IsLogin(request)) return Task.FromResult(Login(7));
                if (request.RequestUri!.AbsolutePath.EndsWith("/resolution", StringComparison.Ordinal))
                {
                    reads++;
                    return Task.FromResult(Status(store.Load()!, "NOT_EXECUTED"));
                }
                throw new InvalidOperationException("No write or resend permitted.");
            }))
            {
                await fixture.SignIn();
                var inspected = await fixture.Operations.CheckAsync(CancellationToken.None);
                Assert.Equal("NOT_EXECUTED", inspected.State);
                Assert.Equal("CASE_7", inspected.CaseId);
                Assert.False(inspected.FenceReleased);
                Assert.NotNull(store.Load());
                var settled = await fixture.Operations.AcceptCertifiedNotExecutedAsync(CancellationToken.None);
                Assert.Equal("NOT_EXECUTED", settled.State);
                Assert.True(settled.FenceReleased);
                Assert.Null(store.Load());
                Assert.True(reads >= 3);
            }
        }

        [Fact]
        public async Task Cancellation_after_dispatch_retains_record_across_constructor_restart()
        {
            var store = new PendingStore();
            using (var cts = new CancellationTokenSource())
            using (var fixture = new Fixture(store, (request, _) =>
            {
                if (IsLogin(request)) return Task.FromResult(Login(7));
                if (request.RequestUri!.AbsolutePath == "/api/v2/operations/context")
                    return Task.FromResult(Context());
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            }))
            {
                await fixture.SignIn();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    await fixture.Operations.SendAsync<JObject>("Receipt.Post", 22,
                        HttpMethod.Post, "/api/v1/goods-receipts/22/post",
                        JObject.Parse(Body), null, cts.Token));
                var recovered = new HttpDesktopOperations(fixture.Transport, fixture.Session,
                    new HttpDesktopOperationSettings(HttpDesktopOperationMode.Web5), store);
                Assert.Equal(store.Load()!.OperationId, recovered.PendingOperationId);
            }
        }

        [Fact]
        public async Task Catalog_readback_does_not_turn_an_ambiguous_write_into_a_success()
        {
            var store = new PendingStore();
            var readbacks = 0;
            using (var fixture = new Fixture(store, (request, _) =>
            {
                if (IsLogin(request)) return Task.FromResult(Login(7));
                if (request.Method == HttpMethod.Put)
                    throw new HttpRequestException("ACK lost after catalog update");
                if (request.Method == HttpMethod.Get &&
                    request.RequestUri!.AbsolutePath == "/api/v1/catalog/products/2")
                {
                    readbacks++;
                    return Task.FromResult(Json(HttpStatusCode.OK, "{\"data\":{\"id\":2}}"));
                }
                throw new InvalidOperationException("Unexpected mutation.");
            }))
            {
                await fixture.SignIn();
                var result = await fixture.Operations.SendUnjournaledAsync<JObject>(
                    "Catalog.products.Update", 2, HttpMethod.Put,
                    "/api/v1/catalog/products/2",
                    new { name = "a", password = "must-not-persist" }, null, CancellationToken.None);
                Assert.Equal("Operation.OutcomeUnknown", result.Error!.Code);
                var persisted = store.Load()!;
                Assert.Equal("readback", persisted.Protocol);
                Assert.Equal(64, persisted.Fingerprint.Length);
                Assert.DoesNotContain("must-not-persist", Newtonsoft.Json.JsonConvert.SerializeObject(persisted));
                var read = await fixture.Operations.CheckAsync(CancellationToken.None);
                Assert.Equal("READBACK_OBSERVED", read.State);
                Assert.False(read.FenceReleased);
                Assert.NotNull(store.Load());
                Assert.Equal(1, readbacks);
            }
        }

        [Fact]
        public void Windows_user_dpapi_intent_survives_new_store_and_is_removed_only_by_match()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            var options = new HttpDesktopOptions("https://recovery-" + Guid.NewGuid().ToString("N") + ".example");
            var first = new WindowsHttpPendingWriteStore(options);
            var record = PendingStore.Record();
            first.Save(record);
            var second = new WindowsHttpPendingWriteStore(options);
            Assert.Equal(record.OperationId, second.Load()!.OperationId);
            Assert.Throws<InvalidOperationException>(() => first.Save(PendingStore.Record()));
            second.Clear(record);
            Assert.Null(first.Load());
        }

        [Theory]
        [InlineData("kind")]
        [InlineData("route")]
        [InlineData("fingerprint")]
        public void Pending_record_rejects_metadata_outside_its_bounded_identity(string field)
        {
            var record = PendingStore.Record();
            if (field == "kind") record.Kind = new string('A', 65);
            else if (field == "route") record.Route = "/api/v1/" + new string('a', 249);
            else record.Fingerprint = new string('A', 64);

            var error = Assert.Throws<InvalidOperationException>(() => record.Validate());
            Assert.Equal("Desktop.PendingIntentInvalid", error.Message);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Historical_receipt_outcome_recovers_typed_values_only_for_matching_id(bool wrongId)
        {
            var store = new PendingStore();
            var record = PendingStore.Record();
            record.Kind = "Receipt.Update";
            record.Method = "PUT";
            record.Route = "/api/v1/goods-receipts/9007199254740993/draft";
            record.TargetId = 9007199254740993L;
            store.Save(record);
            var outcome = HttpDesktopWire.ParseObjectPreservingDates(
                Vps10TypedWireTests.Read("web3-v1/receipt-update-response.json"));
            if (wrongId) outcome["data"]!["receiptId"] = "9007199254740994";
            var envelope = new JObject
            {
                ["data"] = new JObject
                {
                    ["contractVersion"] = "web5-resolution-v1",
                    ["actorUserId"] = record.ActorUserId,
                    ["deploymentEpoch"] = record.DeploymentEpoch,
                    ["operationId"] = record.OperationId,
                    ["operationKind"] = record.Kind,
                    ["method"] = record.Method,
                    ["routeScope"] = "/api/v1/goods-receipts/{receiptId}/draft",
                    ["requestTargetId"] = record.TargetId.Value.ToString(CultureInfo.InvariantCulture),
                    ["requestFingerprint"] = record.Fingerprint,
                    ["state"] = "COMMITTED",
                    ["outcome"] = outcome,
                    ["terminal"] = JValue.CreateNull(),
                },
            };
            using (var restarted = new Fixture(store, (request, _) =>
            {
                if (IsLogin(request)) return Task.FromResult(Login(7));
                if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/resolution", StringComparison.Ordinal))
                    return Task.FromResult(Json(HttpStatusCode.OK, envelope.ToString(Formatting.None)));
                throw new InvalidOperationException("No business write may be replayed.");
            }))
            {
                await restarted.SignIn();
                var result = await restarted.Operations.ResolvePendingAsync<GoodsReceiptDto>(CancellationToken.None);
                if (wrongId)
                {
                    Assert.Equal("Operation.OutcomeUnknown", result.Error!.Code);
                    Assert.NotNull(store.Load());
                }
                else
                {
                    Assert.True(result.IsSuccess);
                    Assert.Equal(record.TargetId.Value, result.Data!.Id);
                    Assert.Equal(9007199254740996L, result.Data.Lines[0].Id);
                    Assert.Equal(12.3456m, result.Data.Lines[1].UnitCost);
                    Assert.Equal("Draft", result.Data.Status);
                    Assert.Null(result.Data.PostedAtUtc);
                    Assert.Equal("AAAAAAAAAAI=", result.Data.Version);
                    Assert.Null(store.Load());
                }
            }
        }

        private static bool IsLogin(HttpRequestMessage request) =>
            request.RequestUri!.AbsolutePath == "/api/v1/auth/login";

        private static HttpResponseMessage Login(int actor) =>
            Json(HttpStatusCode.OK, "{\"data\":{\"accessToken\":\"test.token.value\",\"expiresAtUtc\":\"2099-01-01T00:00:00Z\",\"user\":{\"userId\":" +
                actor.ToString(CultureInfo.InvariantCulture) +
                ",\"username\":\"operator\",\"displayName\":\"Operator\"}}}");

        private static HttpResponseMessage Context() =>
            Json(HttpStatusCode.OK, "{\"data\":{\"deploymentEpoch\":\"" + Epoch +
                "\",\"actorUserId\":7,\"contractVersion\":\"web5-resolution-v1\",\"sameKeyResendEnabled\":false}}");

        private static HttpResponseMessage Status(HttpPendingWriteRecord record, string state)
        {
            var terminal = state == "NOT_EXECUTED"
                ? "{\"decisionId\":\"aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee\",\"caseId\":\"CASE_7\",\"decidedAtUtc\":\"2026-11-16T00:00:00.0000000Z\"}"
                : "null";
            var outcome = state == "COMMITTED" ? "{\"data\":{\"saleId\":\"1\"}}" : "null";
            return Json(HttpStatusCode.OK, "{\"data\":{\"contractVersion\":\"web5-resolution-v1\",\"actorUserId\":7," +
                "\"deploymentEpoch\":\"" + record.DeploymentEpoch + "\",\"operationId\":\"" + record.OperationId +
                "\",\"operationKind\":\"" + record.Kind + "\",\"method\":\"" + record.Method +
                "\",\"routeScope\":\"/api/v1/sales-receipts/{saleId}/post\",\"requestTargetId\":\"1\"," +
                "\"requestFingerprint\":\"" + record.Fingerprint + "\",\"state\":\"" + state +
                "\",\"outcome\":" + outcome + ",\"terminal\":" + terminal + "}}");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string content) =>
            new HttpResponseMessage(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

        private sealed class Fixture : IDisposable
        {
            public Fixture(PendingStore store,
                Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply)
            {
                Session = new HttpDesktopSession(DesktopSessionOptions.Default, new ZeroClock());
                Transport = new HttpDesktopTransport(new HttpDesktopOptions(Origin), Session, new Handler(reply));
                Operations = new HttpDesktopOperations(Transport, Session,
                    new HttpDesktopOperationSettings(HttpDesktopOperationMode.Web5), store);
            }
            public HttpDesktopSession Session { get; }
            public HttpDesktopTransport Transport { get; }
            public HttpDesktopOperations Operations { get; }
            public async Task SignIn()
            {
                var result = await Transport.LoginAsync(new LoginRequest("operator", "example"), CancellationToken.None);
                Assert.True(result.IsSuccess);
            }
            public void Dispose() => Transport.Dispose();
        }

        private sealed class Handler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _reply;
            public Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) { _reply = reply; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => _reply(request, token);
        }

        private sealed class ZeroClock : IMonotonicClock
        {
            public TimeSpan Elapsed => TimeSpan.Zero;
        }

        private sealed class PendingStore : IHttpPendingWriteStore
        {
            private HttpPendingWriteRecord? _record;
            public HttpPendingWriteRecord? Load() => _record;
            public void Save(HttpPendingWriteRecord record)
            {
                if (_record != null) throw new InvalidOperationException("Pending write exists");
                record.Validate();
                _record = record;
            }
            public void Clear(HttpPendingWriteRecord expected)
            {
                if (_record?.OperationId != expected.OperationId ||
                    _record?.Fingerprint != expected.Fingerprint)
                    throw new InvalidOperationException("Original intent mismatch");
                _record = null;
            }
            public static PendingStore Journal()
            {
                var result = new PendingStore();
                result.Save(Record());
                return result;
            }
            public static HttpPendingWriteRecord Record() =>
                new HttpPendingWriteRecord
                {
                    OperationId = Guid.NewGuid().ToString("D"), ActorUserId = 7,
                    Protocol = "web5", Kind = "Sales.Post", Method = "POST",
                    Route = "/api/v1/sales-receipts/1/post", TargetId = 1,
                    DeploymentEpoch = Epoch, Fingerprint = new string('a', 64),
                    CreatedUtc = DateTime.UtcNow
                };
        }
    }
}
