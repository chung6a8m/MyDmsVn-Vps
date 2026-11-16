using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Contracts;
using MyDmsVn.Desktop.Application;
using MyDmsVn.Desktop.Infrastructure.Http;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MyDmsVn.Desktop.Session.Tests
{
    public sealed class Vps03HttpAdapterTests
    {
        private const string Origin = "https://desktop-api.test";
        private const string LoginJson = "{\"data\":{\"accessToken\":\"example.jwt.value\","
            + "\"expiresAtUtc\":\"2099-01-01T00:00:00Z\","
            + "\"user\":{\"userId\":7,\"username\":\"operator\",\"displayName\":\"Operator\"}}}";
        private const string UserJson =
            "{\"data\":{\"userId\":7,\"username\":\"operator\",\"displayName\":\"Operator\"}}";

        [Fact]
        public void Api_origin_is_required_and_only_canonical_https_is_accepted()
        {
            Assert.Throws<ArgumentException>(() => new HttpDesktopOptions("http://desktop-api.test"));
            Assert.Throws<ArgumentException>(() => new HttpDesktopOptions("https://desktop-api.test/path"));
            Assert.Throws<ArgumentException>(() => new HttpDesktopOptions("https://x:secret@desktop-api.test"));
            Assert.Equal("https://desktop-api.test/", new HttpDesktopOptions(Origin).Origin.AbsoluteUri);
        }

        [Fact]
        public void Http_composition_resolves_all_desktop_interfaces_without_local_runtime()
        {
            var services = new ServiceCollection();
            services.AddHttpDesktopAdapter(new HttpDesktopOptions(Origin),
                new HttpDesktopOperationSettings(HttpDesktopOperationMode.Web5));
            using (var provider = services.BuildServiceProvider())
            {
                Assert.IsType<HttpDesktopSession>(provider.GetRequiredService<IDesktopSession>());
                Assert.NotNull(provider.GetRequiredService<IIdentityApiClient>());
                Assert.NotNull(provider.GetRequiredService<IFoundationApiClient>());
                Assert.NotNull(provider.GetRequiredService<IProductApiClient>());
                Assert.NotNull(provider.GetRequiredService<IWarehouseApiClient>());
                Assert.NotNull(provider.GetRequiredService<IEmployeeApiClient>());
                Assert.NotNull(provider.GetRequiredService<ICustomerApiClient>());
                Assert.NotNull(provider.GetRequiredService<IGoodsReceiptApiClient>());
                Assert.NotNull(provider.GetRequiredService<ISalesReceiptApiClient>());
                Assert.NotNull(provider.GetRequiredService<IInventoryApiClient>());
                Assert.NotNull(provider.GetRequiredService<IPermissionApiClient>());
                Assert.NotNull(provider.GetRequiredService<IPermissionCatalogApiClient>());
                Assert.NotNull(provider.GetRequiredService<ISecurityAdministrationApiClient>());
                Assert.IsType<HttpDesktopPreviewCalculator>(
                    provider.GetRequiredService<ISalesReceiptPreviewCalculator>());
            }
        }

        [Fact]
        public async Task Anonymous_feature_requests_fail_without_sending_http()
        {
            var sent = 0;
            using (var fixture = Create(_ => { sent++; return Json(HttpStatusCode.OK, UserJson); }))
            {
                var response = await fixture.Transport.GetCurrentUserAsync(CancellationToken.None);
                Assert.False(response.IsSuccess);
                Assert.Equal("Auth.SessionChanged", response.Error!.Code);
                Assert.Equal(0, sent);
            }
        }

        [Fact]
        public async Task Login_requires_exact_origin_and_cookie_header_then_sends_bearer_on_get()
        {
            var requests = new List<string>();
            using (var fixture = Create(request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/login", StringComparison.Ordinal))
                {
                    Assert.Equal(HttpMethod.Post, request.Method);
                    Assert.Equal(Origin, request.Headers.GetValues("Origin").Single());
                    Assert.Equal("XMLHttpRequest", request.Headers.GetValues("X-Requested-With").Single());
                    Assert.Null(request.Headers.Authorization);
                    var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    Assert.Contains("\"username\":\"operator\"", payload);
                    Assert.DoesNotContain("accessToken", payload);
                    requests.Add("login");
                    return Json(HttpStatusCode.OK, LoginJson);
                }

                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal("example.jwt.value", request.Headers.Authorization?.Parameter);
                Assert.False(request.Headers.Contains("X-Requested-With"));
                requests.Add("me");
                return Json(HttpStatusCode.OK, UserJson);
            }))
            {
                var login = await fixture.Transport.LoginAsync(
                    new LoginRequest("operator", "example-password"), CancellationToken.None);
                Assert.True(login.IsSuccess);
                Assert.Equal(7, fixture.Session.CurrentUser!.UserId);
                var me = await fixture.Transport.GetCurrentUserAsync(CancellationToken.None);
                Assert.True(me.IsSuccess);
                Assert.Equal(7, me.Data!.UserId);
                Assert.Equal(new[] { "login", "me" }, requests);
            }
        }

        [Fact]
        public async Task In_flight_response_after_sign_out_cannot_repopulate_session()
        {
            var requestStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var fixture = Create(request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/login", StringComparison.Ordinal))
                    return Task.FromResult(Json(HttpStatusCode.OK, LoginJson));
                if (request.RequestUri.AbsolutePath.EndsWith("/logout", StringComparison.Ordinal))
                    return Task.FromResult(Json(HttpStatusCode.OK, "{\"data\":{}}"));
                requestStarted.TrySetResult(true);
                return WaitReply();
                async Task<HttpResponseMessage> WaitReply()
                {
                    await release.Task;
                    return Json(HttpStatusCode.OK, UserJson);
                }
            }))
            {
                Assert.True((await fixture.Transport.LoginAsync(
                    new LoginRequest("operator", "secret"), CancellationToken.None)).IsSuccess);
                var pending = fixture.Transport.GetCurrentUserAsync(CancellationToken.None);
                await requestStarted.Task;
                fixture.Session.SignOut();
                release.TrySetResult(true);
                var result = await pending;
                Assert.False(result.IsSuccess);
                Assert.Equal("Auth.SessionChanged", result.Error!.Code);
                Assert.False(fixture.Session.IsAuthenticated);
            }
        }

        [Fact]
        public async Task Web4_write_has_key_and_epoch_and_does_not_replay_server_rejection()
        {
            var writes = 0;
            using (var fixture = Create(request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/login", StringComparison.Ordinal))
                    return Json(HttpStatusCode.OK, LoginJson);
                if (request.RequestUri.AbsolutePath == "/api/v1/operations/context")
                    return Json(HttpStatusCode.OK,
                        "{\"data\":{\"deploymentEpoch\":\"11111111-2222-4333-8444-555555555555\","
                        + "\"actorUserId\":7,\"contractVersion\":\"web4-v1\"}}");
                writes++;
                Assert.Equal("11111111-2222-4333-8444-555555555555",
                    request.Headers.GetValues("X-MyDmsVn-Deployment-Epoch").Single());
                Assert.True(Guid.TryParse(request.Headers.GetValues("Idempotency-Key").Single(), out _));
                return Json(HttpStatusCode.Forbidden,
                    "{\"error\":{\"code\":\"Auth.Forbidden\",\"message\":\"Denied\",\"details\":[]}}");
            }))
            {
                Assert.True((await fixture.Transport.LoginAsync(
                    new LoginRequest("operator", "secret"), CancellationToken.None)).IsSuccess);
                var operation = new HttpDesktopOperations(fixture.Transport, fixture.Session,
                    new HttpDesktopOperationSettings(HttpDesktopOperationMode.Web4));
                var body = JObject.Parse("{\"expectedVersion\":\"AAAAAAAAAAE=\"}");
                var response = await operation.SendAsync<JObject>(
                    "Sales.Post", 1, HttpMethod.Post, "/api/v1/sales-receipts/1/post",
                    body, fixture.Session.Version, CancellationToken.None);
                Assert.False(response.IsSuccess);
                Assert.Equal("Auth.Forbidden", response.Error!.Code);
                Assert.Null(operation.PendingOperationId);
                Assert.Equal(1, writes);
            }
        }

        [Fact]
        public async Task Lost_ack_fences_further_mutations_and_never_resends_original_write()
        {
            var writes = 0;
            using (var fixture = Create(request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/login", StringComparison.Ordinal))
                    return Json(HttpStatusCode.OK, LoginJson);
                if (request.RequestUri.AbsolutePath == "/api/v1/operations/context")
                    return Json(HttpStatusCode.OK,
                        "{\"data\":{\"deploymentEpoch\":\"11111111-2222-4333-8444-555555555555\","
                        + "\"actorUserId\":7,\"contractVersion\":\"web4-v1\"}}");
                writes++;
                throw new HttpRequestException("simulated lost ACK");
            }))
            {
                Assert.True((await fixture.Transport.LoginAsync(
                    new LoginRequest("operator", "secret"), CancellationToken.None)).IsSuccess);
                var operation = new HttpDesktopOperations(fixture.Transport, fixture.Session,
                    new HttpDesktopOperationSettings(HttpDesktopOperationMode.Web5));
                var body = JObject.Parse("{\"expectedVersion\":\"AAAAAAAAAAE=\"}");
                var first = await operation.SendAsync<JObject>(
                    "Sales.Post", 1, HttpMethod.Post, "/api/v1/sales-receipts/1/post",
                    body, null, CancellationToken.None);
                var second = await operation.SendAsync<JObject>(
                    "Sales.Post", 1, HttpMethod.Post, "/api/v1/sales-receipts/1/post",
                    body, null, CancellationToken.None);

                Assert.Equal("Operation.OutcomeUnknown", first.Error!.Code);
                Assert.Equal("Operation.PendingRecovery", second.Error!.Code);
                Assert.NotNull(operation.PendingOperationId);
                Assert.Equal(1, writes);
            }
        }

        [Theory]
        [InlineData("Sales.Post", 9223372036854775807L, "{\"expectedVersion\":\"AAAAAAAAAAI=\"}",
            "02dd986466df1b938b3a1304543b17d1bfbea182b2096713e4ab72397929029c")]
        [InlineData("Receipt.Post", 9007199254740993L, "{\"expectedVersion\":\"AAAAAAAAAAI=\"}",
            "519d7bcfa0f068db0a89cc031f00eed1b7327835fe4dd9ab4ddd4afe4341803f")]
        public void Frozen_web4_fingerprint_vectors_match_independent_corpus(
            string kind, long id, string json, string expected)
        {
            Assert.Equal(expected, HttpDesktopOperationFingerprint.Compute(
                kind, id, JObject.Parse(json)));
        }

        [Fact]
        public void Sales_preview_matches_fixed_point_half_up_and_gift_rules()
        {
            var preview = new HttpDesktopPreviewCalculator();
            Assert.Equal(0.4444m, preview.CalculateRevenue(1.3333m, 0.3333m, false));
            Assert.Equal(0m, preview.CalculateRevenue(1.3333m, 0.3333m, true));
            Assert.Equal(0.4444m, preview.SumAmounts(new[] { 0.2222m, 0.2222m }));
        }


        [Fact]
        public void Frozen_web4_all_six_command_fingerprints_match_independent_corpus()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "MyDmsVn.sln")))
                root = root.Parent;
            Assert.NotNull(root);
            var path = Path.Combine(root!.FullName, "docs", "contracts",
                "web4-v1", "fingerprint-vectors.json");
            var corpus = HttpDesktopWire.ParseObjectPreservingDates(File.ReadAllText(path));
            var wanted = new HashSet<string>(StringComparer.Ordinal)
            {
                "sales-create", "sales-update", "sales-post",
                "receipt-create", "receipt-update", "receipt-post",
            };
            var matched = 0;
            foreach (var vector in ((Newtonsoft.Json.Linq.JArray)corpus["vectors"]!).OfType<JObject>())
            {
                var name = (string?)vector["name"];
                if (name == null || !wanted.Contains(name)) continue;
                var intent = (JObject)vector["intent"]!;
                var targetText = (string?)intent["requestTargetId"];
                long? target = targetText == null
                    ? (long?)null
                    : long.Parse(targetText, CultureInfo.InvariantCulture);
                var kind = (string)intent["operationKind"]!;
                var body = (JObject)intent["body"]!;
                Assert.Equal((string)vector["sha256"]!,
                    HttpDesktopOperationFingerprint.Compute(kind, target, body));
                matched++;
            }

            Assert.Equal(6, matched);
        }

        [Fact]
        public void Receipt_wire_rename_never_changes_a_calendar_date_or_bigint_string()
        {
            var original = "{\"data\":{\"receiptId\":\"9007199254740993\",\"receiptDate\":\"2024-02-29\",\"lines\":[{\"receiptLineId\":\"9007199254740994\"}]}}";
            var normalized = HttpDesktopWire.NormalizeResponse<GoodsReceiptDto>(original);
            Assert.Contains("\"receiptDate\":\"2024-02-29\"", normalized);
            Assert.Contains("\"id\":\"9007199254740993\"", normalized);
            Assert.Contains("\"id\":\"9007199254740994\"", normalized);
            Assert.DoesNotContain("receiptLineId", normalized);
        }

        private static Fixture Create(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
            Create(request => Task.FromResult(handler(request)));

        private static Fixture Create(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            var session = new HttpDesktopSession(DesktopSessionOptions.Default, new Clock());
            var transport = new HttpDesktopTransport(new HttpDesktopOptions(Origin),
                session, new ReplyHandler(handler));
            return new Fixture(session, transport);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

        private sealed class Fixture : IDisposable
        {
            public Fixture(HttpDesktopSession session, HttpDesktopTransport transport)
            { Session = session; Transport = transport; }
            public HttpDesktopSession Session { get; }
            public HttpDesktopTransport Transport { get; }
            public void Dispose() => Transport.Dispose();
        }

        private sealed class Clock : IMonotonicClock
        {
            public TimeSpan Elapsed => TimeSpan.Zero;
        }

        private sealed class ReplyHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _reply;
            public ReplyHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> reply) { _reply = reply; }
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken) => _reply(request);
        }
    }
}
