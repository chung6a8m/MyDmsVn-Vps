using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Contracts;
using MyDmsVn.Desktop.Application;
using MyDmsVn.Desktop.Infrastructure.Http;
using MyDmsVn.Desktop.Infrastructure.Http.Wire;
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
        public async Task Sign_out_completes_server_logout_when_the_ui_thread_is_waiting()
        {
            var completed = false;
            var session = new HttpDesktopSession(DesktopSessionOptions.Default, new Clock());
            using (var transport = new HttpDesktopTransport(new HttpDesktopOptions(Origin), session,
                new ReplyHandler(async (request, cancellationToken) =>
                {
                    if (request.RequestUri!.AbsolutePath == "/api/v1/auth/login")
                        return Json(HttpStatusCode.OK, LoginJson);

                    Assert.Equal("/api/v1/auth/logout", request.RequestUri.AbsolutePath);
                    Assert.Equal(DesktopSessionState.LoggingOut, session.State);
                    Assert.Null(session.CurrentUser);
                    var responseReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    using (cancellationToken.Register(() => responseReady.TrySetCanceled()))
                    {
                        var context = SynchronizationContext.Current;
                        if (context == null)
                            responseReady.TrySetResult(true);
                        else
                            context.Post(_ => responseReady.TrySetResult(true), null);
                        await responseReady.Task.ConfigureAwait(false);
                    }
                    completed = true;
                    return Json(HttpStatusCode.OK, "{\"data\":{}}");
                })))
            {
                var login = await transport.LoginAsync(new LoginRequest("operator", "secret"), CancellationToken.None);
                Assert.True(login.IsSuccess);

                var originalContext = SynchronizationContext.Current;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(new WaitingUiContext());
                    session.SignOut();
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(originalContext);
                }

                Assert.True(completed, "The logout HTTP request must finish before SignOut returns.");
                Assert.Equal(DesktopSessionState.Unauthenticated, session.State);
            }
        }

        [Fact]
        public async Task Sign_out_waits_for_an_in_flight_refresh_before_sending_logout()
        {
            var refreshStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRefresh = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var loggingOut = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var logoutBeforeRefresh = false;
            var logoutRequests = 0;
            using (var fixture = Create(async request =>
            {
                switch (request.RequestUri!.AbsolutePath)
                {
                    case "/api/v1/auth/login":
                        return Json(HttpStatusCode.OK, LoginJson.Replace("2099-01-01T00:00:00Z",
                            DateTime.UtcNow.AddSeconds(20).ToString("O", CultureInfo.InvariantCulture)));
                    case "/api/v1/auth/refresh":
                        refreshStarted.SetResult(true);
                        await releaseRefresh.Task.ConfigureAwait(false);
                        return Json(HttpStatusCode.OK, LoginJson);
                    case "/api/v1/auth/logout":
                        logoutBeforeRefresh = !releaseRefresh.Task.IsCompleted;
                        logoutRequests++;
                        return Json(HttpStatusCode.OK, "{\"data\":{}}");
                    default:
                        throw new InvalidOperationException("No business read may use the signed-out session.");
                }
            }))
            {
                fixture.Session.SessionChanged += (_, _) =>
                {
                    if (fixture.Session.State == DesktopSessionState.LoggingOut)
                        loggingOut.TrySetResult(true);
                };
                Assert.True((await fixture.Transport.LoginAsync(
                    new LoginRequest("operator", "secret"), CancellationToken.None)).IsSuccess);
                var read = fixture.Transport.GetCurrentUserAsync(CancellationToken.None);
                await refreshStarted.Task;

                var signOut = Task.Run(() => fixture.Session.SignOut());
                await loggingOut.Task;
                // A broken transport completes sign-out while rotation is still in flight.
                await Task.WhenAny(signOut, Task.Delay(200));
                releaseRefresh.SetResult(true);

                Assert.Equal("Auth.SessionChanged", (await read).Error!.Code);
                await signOut;
                Assert.False(logoutBeforeRefresh);
                Assert.Equal(1, logoutRequests);
            }
        }

        [Theory]
        [InlineData(false, true, false)]
        [InlineData(true, true, false)]
        [InlineData(true, false, false)]
        [InlineData(false, true, true)]
        public async Task Failed_refresh_preserves_unsent_sales_draft_for_manual_save_after_login(
            bool web5, bool sameActor, bool clearFails)
        {
            var logins = 0;
            var writes = 0;
            using var fixture = Create(request =>
            {
                switch (request.RequestUri!.AbsolutePath)
                {
                    case "/api/v1/auth/login":
                        return Json(HttpStatusCode.OK, ++logins == 1
                            ? LoginJson.Replace("2099-01-01T00:00:00Z",
                                DateTime.UtcNow.AddSeconds(20).ToString("O", CultureInfo.InvariantCulture))
                            : sameActor ? LoginJson : LoginJson.Replace("\"userId\":7", "\"userId\":8"));
                    case "/api/v1/auth/refresh":
                        throw new HttpRequestException("Connection unavailable");
                    case "/api/v2/operations/context":
                        return Json(HttpStatusCode.OK, "{\"data\":{\"deploymentEpoch\":\"35f336a2-af15-4da2-aa43-9ff46828ffad\","
                            + "\"contractVersion\":\"web5-resolution-v1\",\"sameKeyResendEnabled\":false,\"actorUserId\":7}}");
                    case "/api/v1/sales-receipts":
                        writes++;
                        return Json(HttpStatusCode.BadRequest, "{\"error\":{\"code\":\"ValidationError\",\"message\":\"Test response\"}}");
                    default:
                        throw new InvalidOperationException("Unexpected request");
                }
            });
            var credentials = new LoginRequest("operator", "secret");
            Assert.True((await fixture.Transport.LoginAsync(credentials, CancellationToken.None)).IsSuccess);
            using var recovery = new SalesDraftRecovery(fixture.Session);
            var store = new TestPendingWriteStore { ClearFails = clearFails };
            var operations = new HttpDesktopOperations(fixture.Transport, fixture.Session,
                new HttpDesktopOperationSettings(web5 ? HttpDesktopOperationMode.Web5 : HttpDesktopOperationMode.LegacyWeb11), store);
            var sales = new HttpSalesReceiptApiClient(fixture.Transport, operations);
            SalesReceiptEditorViewModel OpenEditor()
            {
                var vm = new SalesReceiptEditorViewModel(sales, fixture.Session, new ImmediateUiDispatcher(),
                    new HttpDesktopPreviewCalculator(), new MyDmsVn.Desktop.Tests.SalesBuffers(), recovery);
                vm.SetAccess(true, true, true);
                return vm;
            }

            using (var vm = OpenEditor())
            {
                vm.CustomerId = 1; vm.WarehouseId = 2; vm.EmployeeId = 3; vm.Note = "unsent sales draft";
                var line = vm.AddLine()!;
                line.ProductId = 4; line.Quantity = 2m; line.UnitPrice = 12.3456m;
                await vm.SaveAsync();
                Assert.Equal(DesktopSessionState.Expired, fixture.Session.State);
                Assert.Equal(0, writes);
                if (clearFails) Assert.NotNull(store.Load());
                else Assert.Null(store.Load());
                Assert.True(recovery.HasDraft);
            }

            Assert.True((await fixture.Transport.LoginAsync(credentials, CancellationToken.None)).IsSuccess);
            using var reopened = OpenEditor();
            if (!sameActor)
            {
                Assert.False(recovery.HasDraft);
                Assert.False(reopened.TryRestoreDraft());
                Assert.Empty(reopened.Lines);
                Assert.Null(reopened.Note);
                Assert.Equal(0, writes);
                return;
            }
            reopened.SetAccess(true, false, false);
            Assert.False(reopened.TryRestoreDraft());
            reopened.SetAccess(true, true, true);
            Assert.True(reopened.TryRestoreDraft());
            Assert.True(reopened.IsDirty);
            Assert.Equal("unsent sales draft", reopened.Note);
            Assert.Equal(1, reopened.CustomerId);
            Assert.Equal(2, reopened.WarehouseId);
            Assert.Equal(3, reopened.EmployeeId);
            var restoredLine = Assert.Single(reopened.Lines);
            Assert.Equal(4, restoredLine.ProductId);
            Assert.Equal(2m, restoredLine.Quantity);
            Assert.Equal(12.3456m, restoredLine.UnitPrice);
            Assert.Equal(0, writes);
            await reopened.SaveAsync();
            Assert.Equal(clearFails ? 0 : 1, writes);
        }

        [Fact]
        public async Task Sign_out_sends_logout_after_a_stalled_refresh_body_times_out()
        {
            using var body = new StalledResponseBody();
            var logoutRequests = 0;
            var session = new HttpDesktopSession(DesktopSessionOptions.Default, new Clock());
            using var transport = new HttpDesktopTransport(
                new HttpDesktopOptions(Origin, TimeSpan.FromSeconds(2)), session,
                new ReplyHandler(request => Task.FromResult(request.RequestUri!.AbsolutePath switch
                {
                    "/api/v1/auth/login" => Json(HttpStatusCode.OK, ShortLivedLogin()),
                    "/api/v1/auth/refresh" => new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StreamContent(body)
                        {
                            Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") }
                        }
                    },
                    "/api/v1/auth/logout" => Logout(),
                    _ => throw new InvalidOperationException("Unexpected request"),
                })));

            HttpResponseMessage Logout()
            {
                Interlocked.Increment(ref logoutRequests);
                return Json(HttpStatusCode.OK, "{\"data\":{}}");
            }

            Assert.True((await transport.LoginAsync(new LoginRequest("operator", "secret"),
                CancellationToken.None)).IsSuccess);
            var read = transport.GetCurrentUserAsync(CancellationToken.None);
            try
            {
                Assert.Same(body.ReadStarted.Task, await Task.WhenAny(body.ReadStarted.Task, Task.Delay(5000)));
                var signOut = session.SignOutAsync();
                Assert.Same(signOut, await Task.WhenAny(signOut, Task.Delay(7000)));
                await signOut;
                Assert.Equal(1, logoutRequests);
                Assert.Equal(DesktopSessionState.Unauthenticated, session.State);
                Assert.Equal("Auth.SessionChanged", (await read).Error!.Code);
            }
            finally
            {
                body.ReleaseRead.TrySetResult(0);
                await read;
            }
        }

        [Fact]
        public async Task New_login_waits_until_a_refresh_from_the_old_generation_finishes()
        {
            var enteredRefresh = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRefresh = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondLoginSent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var logins = 0;
            var clock = new Clock();
            using var fixture = Create(async request =>
            {
                switch (request.RequestUri!.AbsolutePath)
                {
                    case "/api/v1/auth/login":
                        if (++logins == 1) return Json(HttpStatusCode.OK, ShortLivedLogin());
                        secondLoginSent.TrySetResult(true);
                        return Json(HttpStatusCode.OK, LoginJson);
                    case "/api/v1/auth/refresh":
                        enteredRefresh.TrySetResult(true);
                        await releaseRefresh.Task;
                        return Json(HttpStatusCode.OK, LoginJson);
                    default:
                        throw new InvalidOperationException("Unexpected request");
                }
            }, clock);
            var credentials = new LoginRequest("operator", "secret");
            Assert.True((await fixture.Transport.LoginAsync(credentials, CancellationToken.None)).IsSuccess);
            var oldRead = fixture.Transport.GetCurrentUserAsync(CancellationToken.None);
            Assert.Same(enteredRefresh.Task, await Task.WhenAny(enteredRefresh.Task, Task.Delay(5000)));
            clock.Elapsed = DesktopSessionOptions.Default.IdleTimeout;
            Assert.True(fixture.Session.ExpireIfIdle());
            var newLogin = fixture.Transport.LoginAsync(credentials, CancellationToken.None);
            try
            {
                Assert.NotSame(secondLoginSent.Task, await Task.WhenAny(secondLoginSent.Task, Task.Delay(150)));
            }
            finally
            {
                releaseRefresh.TrySetResult(true);
                await Task.WhenAll(oldRead, newLogin);
            }
            Assert.Equal("Auth.SessionChanged", (await oldRead).Error!.Code);
            Assert.True((await newLogin).IsSuccess);
            Assert.True(fixture.Session.IsAuthenticated);
        }

        [Theory]
        [InlineData("Auth.Unauthorized", "Auth.Unauthorized")]
        [InlineData("Validation.Required", "Validation.Required")]
        [InlineData("Auth.unrecognized_private_value", "UnrecognizedErrorCode")]
        [InlineData("eyJhbGciOiJIUzI1NiJ9.c2VjcmV0.c2ln", "UnrecognizedErrorCode")]
        public void Operation_and_logout_diagnostics_only_keep_recognized_codes(string code, string expected)
        {
            var diagnostics = typeof(HttpDesktopTransport).Assembly.GetType(
                "MyDmsVn.Desktop.Infrastructure.Http.HttpDesktopOperationDiagnostics", throwOnError: true)!;
            var safeCode = diagnostics.GetMethod("SafeCode",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            Assert.Equal(expected, safeCode.Invoke(null, new object[] { code }));
        }

        private static string ShortLivedLogin() => LoginJson.Replace("2099-01-01T00:00:00Z",
            DateTime.UtcNow.AddSeconds(20).ToString("O", CultureInfo.InvariantCulture));

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
                    new HttpDesktopOperationSettings(HttpDesktopOperationMode.Web4), new TestPendingWriteStore());
                var body = JObject.Parse("{\"expectedVersion\":\"AAAAAAAAAAE=\"}");
                var response = await operation.SendAsync<JObject>(
                    "Sales.Post", 1, HttpMethod.Post, "/api/v1/sales-receipts/1/post",
                    body, fixture.Session.Version, CancellationToken.None);
                Assert.False(response.IsSuccess);
                Assert.Equal("Operation.OutcomeUnknown", response.Error!.Code);
                Assert.NotNull(operation.PendingOperationId); // 403 is not a commit/rollback certificate.
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
                if (request.RequestUri.AbsolutePath == "/api/v2/operations/context")
                    return Json(HttpStatusCode.OK,
                        "{\"data\":{\"deploymentEpoch\":\"11111111-2222-4333-8444-555555555555\","
                        + "\"actorUserId\":7,\"contractVersion\":\"web5-resolution-v1\","
                        + "\"sameKeyResendEnabled\":false}}");
                writes++;
                throw new HttpRequestException("simulated lost ACK");
            }))
            {
                Assert.True((await fixture.Transport.LoginAsync(
                    new LoginRequest("operator", "secret"), CancellationToken.None)).IsSuccess);
                var operation = new HttpDesktopOperations(fixture.Transport, fixture.Session,
                    new HttpDesktopOperationSettings(HttpDesktopOperationMode.Web5), new TestPendingWriteStore());
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
        public async Task Web5_mode_fails_closed_if_server_does_not_support_v2_context()
        {
            var writes = 0;
            using (var fixture = Create(request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/login", StringComparison.Ordinal))
                    return Json(HttpStatusCode.OK, LoginJson);
                if (request.RequestUri.AbsolutePath == "/api/v2/operations/context")
                    return Json(HttpStatusCode.NotFound,
                        "{\"error\":{\"code\":\"NotFound\",\"message\":\"Missing\",\"details\":[]}}");
                writes++;
                throw new InvalidOperationException("No write is allowed against a WEB4-only host.");
            }))
            {
                Assert.True((await fixture.Transport.LoginAsync(
                    new LoginRequest("operator", "secret"), CancellationToken.None)).IsSuccess);
                var ops = new HttpDesktopOperations(fixture.Transport, fixture.Session,
                    new HttpDesktopOperationSettings(HttpDesktopOperationMode.Web5), new TestPendingWriteStore());
                var response = await ops.SendAsync<JObject>(
                    "Sales.Post", 1, HttpMethod.Post, "/api/v1/sales-receipts/1/post",
                    JObject.Parse("{\"expectedVersion\":\"AAAAAAAAAAE=\"}"),
                    null, CancellationToken.None);

                Assert.False(response.IsSuccess);
                Assert.Equal(ApiStatusCode.NotFound, response.Error!.Status);
                Assert.Null(ops.PendingOperationId);
                Assert.Equal(0, writes);
            }
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
        public void Receipt_wire_mapping_never_changes_a_calendar_date_or_bigint_string()
        {
            var original = "{\"data\":{\"receiptId\":\"9007199254740993\",\"receiptDate\":\"2024-02-29\",\"receiptNumber\":\"GR1\",\"status\":\"Draft\",\"version\":\"AAAAAAAAAAE=\",\"lines\":[{\"receiptLineId\":\"9007199254740994\",\"quantity\":\"1.2500\",\"unitCost\":\"2.0000\"}]}}";
            var response = V1WireMapper.DecodeResponse<GoodsReceiptDto>(original, ApiStatusCode.Ok, true);
            Assert.True(response.IsSuccess);
            Assert.Equal(9007199254740993L, response.Data!.Id);
            Assert.Equal(9007199254740994L, Assert.Single(response.Data.Lines).Id);
            Assert.Equal(new DateTime(2024, 2, 29), response.Data.ReceiptDate);
            Assert.Equal(DateTimeKind.Unspecified, response.Data.ReceiptDate.Kind);
        }

        [Theory]
        [InlineData(HttpDesktopOperationMode.LegacyWeb11, "sales-create")]
        [InlineData(HttpDesktopOperationMode.LegacyWeb11, "sales-update")]
        [InlineData(HttpDesktopOperationMode.LegacyWeb11, "sales-post")]
        [InlineData(HttpDesktopOperationMode.LegacyWeb11, "receipt-create")]
        [InlineData(HttpDesktopOperationMode.LegacyWeb11, "receipt-update")]
        [InlineData(HttpDesktopOperationMode.LegacyWeb11, "receipt-post")]
        [InlineData(HttpDesktopOperationMode.Web4, "sales-create")]
        [InlineData(HttpDesktopOperationMode.Web4, "sales-update")]
        [InlineData(HttpDesktopOperationMode.Web4, "sales-post")]
        [InlineData(HttpDesktopOperationMode.Web4, "receipt-create")]
        [InlineData(HttpDesktopOperationMode.Web4, "receipt-update")]
        [InlineData(HttpDesktopOperationMode.Web4, "receipt-post")]
        [InlineData(HttpDesktopOperationMode.Web5, "sales-create")]
        [InlineData(HttpDesktopOperationMode.Web5, "sales-update")]
        [InlineData(HttpDesktopOperationMode.Web5, "sales-post")]
        [InlineData(HttpDesktopOperationMode.Web5, "receipt-create")]
        [InlineData(HttpDesktopOperationMode.Web5, "receipt-update")]
        [InlineData(HttpDesktopOperationMode.Web5, "receipt-post")]
        public async Task Typed_feature_dispatch_matches_frozen_bytes_and_persisted_intent(
            HttpDesktopOperationMode mode, string vectorName)
        {
            // Expected bytes/hash come from the independent frozen corpus, never from the production mapper.
            var corpus = HttpDesktopWire.ParseObjectPreservingDates(
                Vps10TypedWireTests.Read("web4-v1/fingerprint-vectors.json"));
            var vector = corpus["vectors"]!.Single(item => (string?)item["name"] == vectorName);
            var intent = (JObject)vector["intent"]!;
            var expectedBody = intent["body"]!.ToString(Newtonsoft.Json.Formatting.None);
            var kind = (string)intent["operationKind"]!;
            var targetText = (string?)intent["requestTargetId"];
            long? target = targetText == null ? (long?)null : long.Parse(targetText, CultureInfo.InvariantCulture);
            var method = (string)intent["method"]!;
            var route = ((string)intent["routeScope"]!)
                .Replace("{saleId}", targetText ?? "")
                .Replace("{receiptId}", targetText ?? "");
            const string epoch = "11111111-2222-4333-8444-555555555555";
            var protocol = mode == HttpDesktopOperationMode.Web5 ? "web5" :
                mode == HttpDesktopOperationMode.Web4 ? "web4" : "legacy";
            string expectedFingerprint;
            if (protocol == "legacy")
            {
                using (var sha = SHA256.Create())
                    expectedFingerprint = BitConverter.ToString(sha.ComputeHash(
                        Encoding.UTF8.GetBytes(method + "\n" + route + "\n" + expectedBody)))
                        .Replace("-", "").ToLowerInvariant();
            }
            else
                expectedFingerprint = (string)vector["sha256"]!;

            var store = new TestPendingWriteStore();
            var writes = 0;
            var contexts = 0;
            using (var fixture = Create(request =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/v1/auth/login")
                    return Json(HttpStatusCode.OK, LoginJson);
                if (request.RequestUri.AbsolutePath.EndsWith("/operations/context", StringComparison.Ordinal))
                {
                    contexts++;
                    Assert.NotEqual("legacy", protocol);
                    Assert.Equal(protocol == "web5" ? "/api/v2/operations/context" : "/api/v1/operations/context",
                        request.RequestUri.AbsolutePath);
                    Assert.Equal(HttpMethod.Get, request.Method);
                    Assert.Null(store.Load());
                    return Json(HttpStatusCode.OK, "{\"data\":{\"deploymentEpoch\":\"" + epoch +
                        "\",\"actorUserId\":7,\"contractVersion\":\"" +
                        (protocol == "web5" ? "web5-resolution-v1" : "web4-v1") +
                        "\",\"sameKeyResendEnabled\":false}}");
                }

                writes++;
                Assert.Equal(route, request.RequestUri.AbsolutePath);
                Assert.Equal(method, request.Method.Method);
                Assert.Equal(expectedBody, request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                Assert.Equal("application/json", request.Content.Headers.ContentType!.MediaType);
                Assert.Equal("example.jwt.value", request.Headers.Authorization!.Parameter);
                var pending = store.Load();
                Assert.NotNull(pending);
                pending!.Validate();
                Assert.Equal(kind, pending.Kind);
                Assert.Equal(7, pending.ActorUserId);
                Assert.Equal(route, pending.Route);
                Assert.Equal(method, pending.Method);
                Assert.Equal(protocol, pending.Protocol);
                Assert.Equal(target, pending.TargetId);
                Assert.Equal(expectedFingerprint, pending.Fingerprint);
                if (protocol == "legacy")
                {
                    Assert.Null(pending.DeploymentEpoch);
                    Assert.False(request.Headers.Contains("Idempotency-Key"));
                    Assert.False(request.Headers.Contains("X-MyDmsVn-Deployment-Epoch"));
                }
                else
                {
                    Assert.Equal(epoch, pending.DeploymentEpoch);
                    Assert.Equal(pending.OperationId, request.Headers.GetValues("Idempotency-Key").Single());
                    Assert.Equal(epoch, request.Headers.GetValues("X-MyDmsVn-Deployment-Epoch").Single());
                }

                var family = vectorName.StartsWith("sales-", StringComparison.Ordinal) ? "web2-v1/" : "web3-v1/";
                return Json(vectorName.EndsWith("-create", StringComparison.Ordinal) ? HttpStatusCode.Created : HttpStatusCode.OK,
                    Vps10TypedWireTests.Read(family + vectorName + "-response.json"));
            }))
            {
                Assert.True((await fixture.Transport.LoginAsync(
                    new LoginRequest("operator", "secret"), CancellationToken.None)).IsSuccess);
                var operations = new HttpDesktopOperations(fixture.Transport, fixture.Session,
                    new HttpDesktopOperationSettings(mode), store);
                var sales = new HttpSalesReceiptApiClient(fixture.Transport, operations);
                var receipt = new HttpGoodsReceiptApiClient(fixture.Transport, operations);
                var date = new DateTime(2024, 2, 29);
                var saleCreateLines = new[]
                {
                    new SaveSalesReceiptLineRequest(0, 104, 1.2500m, 2.0000m, false),
                    new SaveSalesReceiptLineRequest(0, 104, 0.0001m, 0.5000m, false),
                    new SaveSalesReceiptLineRequest(0, 104, 2.0000m, 3.0000m, true),
                };
                var saleUpdateLines = new[]
                {
                    new SaveSalesReceiptLineRequest(9007199254740993L, 104, 1.2500m, 2.0000m, false),
                    new SaveSalesReceiptLineRequest(9007199254740994L, 104, 0.0001m, 0.5000m, false),
                    new SaveSalesReceiptLineRequest(long.MaxValue, 104, 2.0000m, 3.0000m, true),
                };
                switch (vectorName)
                {
                    case "sales-create":
                    {
                        var result = await sales.CreateDraftAsync(new SaveSalesReceiptRequest(date, 101, 102, 103,
                            "Phiếu thử — hàng tặng và dòng trùng sản phẩm", saleCreateLines), CancellationToken.None);
                        Assert.True(result.IsSuccess);
                        Assert.Equal(long.MaxValue, result.Data!.SaleId);
                        Assert.Equal(2.5001m, result.Data.TotalAmount);
                        Assert.Equal("Draft", result.Data.Status);
                        break;
                    }
                    case "sales-update":
                    {
                        var result = await sales.UpdateDraftAsync(new UpdateSalesReceiptDraftRequest(long.MaxValue,
                            "AAAAAAAAAAE=", date, 101, 102, 103, "Đã cập nhật", saleUpdateLines), CancellationToken.None);
                        Assert.True(result.IsSuccess);
                        Assert.Equal(long.MaxValue, result.Data!.SaleId);
                        Assert.Equal(long.MaxValue, result.Data.Lines[2].SaleLineId);
                        Assert.Equal("AAAAAAAAAAI=", result.Data.Version);
                        break;
                    }
                    case "sales-post":
                    {
                        var result = await sales.PostAsync(new PostSalesReceiptRequest(long.MaxValue, "AAAAAAAAAAI="),
                            CancellationToken.None);
                        Assert.True(result.IsSuccess);
                        Assert.Equal(long.MaxValue, result.Data!.SaleId);
                        Assert.Equal("Posted", result.Data.Status);
                        Assert.Equal("AAAAAAAAAAM=", result.Data.Version);
                        Assert.Equal(DateTimeKind.Utc, result.Data.PostedAtUtc.Kind);
                        break;
                    }
                    case "receipt-create":
                    {
                        var result = await receipt.CreateDraftAsync(new SaveGoodsReceiptRequest(date, 1, 2, null,
                            new[] { new SaveGoodsReceiptLineRequest(7, 1.2500m, 12.3456m),
                                new SaveGoodsReceiptLineRequest(8, 2.0000m, 0.0000m) }), CancellationToken.None);
                        Assert.True(result.IsSuccess);
                        Assert.Equal(9007199254740993L, result.Data!.Id);
                        Assert.Equal(9007199254740994L, result.Data.Lines[0].Id);
                        Assert.Equal(12.3456m, result.Data.Lines[0].UnitCost);
                        Assert.Null(result.Data.Note);
                        break;
                    }
                    case "receipt-update":
                    {
                        var result = await receipt.UpdateDraftAsync(new UpdateGoodsReceiptDraftRequest(9007199254740993L,
                            "AAAAAAAAAAE=", date, 1, 2, "Nhập hàng — đã kiểm đếm",
                            new[] { new SaveGoodsReceiptLineRequest(8, 3.0000m, 0.0000m),
                                new SaveGoodsReceiptLineRequest(7, 1.5000m, 12.3456m) }), CancellationToken.None);
                        Assert.True(result.IsSuccess);
                        Assert.Equal(9007199254740993L, result.Data!.Id);
                        Assert.Equal(9007199254740996L, result.Data.Lines[0].Id);
                        Assert.Equal("AAAAAAAAAAI=", result.Data.Version);
                        break;
                    }
                    case "receipt-post":
                    {
                        var result = await receipt.PostAsync(new PostGoodsReceiptRequest(9007199254740993L, "AAAAAAAAAAI="),
                            CancellationToken.None);
                        Assert.True(result.IsSuccess);
                        Assert.Equal(9007199254740993L, result.Data!.ReceiptId);
                        Assert.Equal("Posted", result.Data.Status);
                        Assert.Equal(new DateTime(2026, 11, 1, 2, 3, 4, DateTimeKind.Utc).AddTicks(1234567),
                            result.Data.PostedAtUtc);
                        Assert.Equal(DateTimeKind.Utc, result.Data.PostedAtUtc.Kind);
                        break;
                    }
                    default:
                        throw new InvalidOperationException("Unknown frozen vector.");
                }

                Assert.Equal(1, writes);
                Assert.Equal(protocol == "legacy" ? 0 : 1, contexts);
                Assert.Null(store.Load());
                Assert.Null(operations.PendingOperationId);
            }
        }

        private static Fixture Create(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
            Create(request => Task.FromResult(handler(request)));

        private static Fixture Create(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler, Clock? clock = null)
        {
            var session = new HttpDesktopSession(DesktopSessionOptions.Default, clock ?? new Clock());
            var transport = new HttpDesktopTransport(new HttpDesktopOptions(Origin),
                session, new ReplyHandler(handler));
            return new Fixture(session, transport);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

        private sealed class TestPendingWriteStore : IHttpPendingWriteStore
        {
            private HttpPendingWriteRecord? _record;
            public bool ClearFails { get; set; }
            public HttpPendingWriteRecord? Load() => _record;
            public void Save(HttpPendingWriteRecord record)
            {
                if (_record != null) throw new InvalidOperationException("Pending already exists");
                _record = record;
            }
            public void Clear(HttpPendingWriteRecord expected)
            {
                if (_record?.OperationId != expected.OperationId)
                    throw new InvalidOperationException("Pending changed");
                if (ClearFails) throw new IOException("Pending file could not be removed");
                _record = null;
            }
        }

        private sealed class StalledResponseBody : MemoryStream
        {
            public TaskCompletionSource<bool> ReadStarted { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<int> ReleaseRead { get; } =
                new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                ReadStarted.TrySetResult(true);
                return ReleaseRead.Task;
            }
        }

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
            public TimeSpan Elapsed { get; set; }
        }

        private sealed class WaitingUiContext : SynchronizationContext
        {
            // The UI cannot dispatch posted work while its SignOut call is waiting.
            public override void Post(SendOrPostCallback d, object? state) { }
        }

        private sealed class ReplyHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _reply;
            public ReplyHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> reply)
                : this((request, _) => reply(request)) { }
            public ReplyHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply)
            { _reply = reply; }
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken) => _reply(request, cancellationToken);
        }
    }
}
