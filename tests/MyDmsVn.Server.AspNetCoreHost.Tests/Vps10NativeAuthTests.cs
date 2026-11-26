using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyDmsVn.Server.Application.Identity;
using MyDmsVn.Server.HttpHost;
using NativeAuth = MyDmsVn.Server.HttpHost.Features.Auth.AuthController;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using LiveFixture = MyDmsVn.Server.AspNetCoreHost.Tests.Vps05cDesktopRouteLiveParityTests.DesktopLiveFixture;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps10NativeAuthTests
{
    private const string Valid = "{\"username\":\"alice\",\"password\":\"correct-secret\"}";
    private const string Cookie = "__Host-mydmsvn-rt=22222222-2222-4222-8222-222222222222";

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"username\":\"alice\"}")]
    [InlineData("{\"username\":\"alice\",\"username\":\"alice\"}")]
    [InlineData("{\"Username\":\"alice\",\"password\":\"test-secret\"}")]
    [InlineData("{\"username\":\"alice\",\"password\":null}")]
    [InlineData("{\"username\":\"alice\",\"password\":1}")]
    [InlineData("{\"username\":\" \",\"password\":\"test-secret\"}")]
    [InlineData("{\"username\":\"alice\",\"password\":\"\"}")]
    [InlineData("{\"username\":\"alice\",\"password\":\"test-secret\",\"extra\":false}")]
    [InlineData("{\"username\":\"alice\",\"password\":\"test-secret\",}")]
    [InlineData("{/* comment */\"username\":\"alice\",\"password\":\"test-secret\"}")]
    [InlineData("{\"username\":\"alice\",\"password\":\"ééééééééééééééééééééééééééééééééééééé\"}")]
    public async Task Strict_login_binds_no_invalid_credentials(string? body)
    {
        await using var fixture = await LiveFixture.StartAsync(authControlProfile: true);
        using var response = await fixture.SendControlAsync("POST", "/api/v1/auth/login", body, false, false);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ValidationError", json.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("application/xml")]
    [InlineData("application/json; charset=iso-8859-1")]
    [InlineData("text/plain")]
    public async Task Login_invalid_content_type_uses_legacy_400(string media)
    {
        await using var fixture = await LiveFixture.StartAsync(authControlProfile: true);
        using var response = await fixture.SendControlAsync("POST", "/api/v1/auth/login", Valid, false, false, contentType: media);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("POST", "/api/v1/auth/login?extra=1", Valid, false)]
    [InlineData("POST", "/api/v1/auth/refresh", "{}", false)]
    [InlineData("POST", "/api/v1/auth/logout?x=1", null, false)]
    [InlineData("GET", "/api/v1/auth/me", "{}", true)]
    [InlineData("GET", "/api/v1/auth/capabilities?feature=sales&feature=sales", null, true)]
    [InlineData("GET", "/api/v1/auth/capabilities?FEATURE=sales", null, true)]
    public async Task Auth_keeps_raw_query_and_body_grammar(string verb, string path, string? body, bool authenticated)
    {
        await using var fixture = await LiveFixture.StartAsync(authControlProfile: true);
        using var response = await fixture.SendControlAsync(verb, path, body, authenticated, false);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("X-Refresh-Token", 400)]
    [InlineData("Refresh-Token", 400)]
    [InlineData("Origin", 403)]
    [InlineData("X-Requested-With", 403)]
    [InlineData("Sec-Fetch-Site", 403)]
    public async Task Refresh_rejects_noncanonical_token_and_transport_headers(string header, int status)
    {
        await using var fixture = await LiveFixture.StartAsync(authControlProfile: true);
        using var response = await fixture.SendControlAsync("POST", "/api/v1/auth/refresh", null, false, true,
            extraHeader: header, extraValues: new[] { "unexpected", "other" });
        Assert.Equal(status, (int)response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData(Cookie + "; " + Cookie)]
    [InlineData("__Host-mydmsvn-rt=22222222-2222-1222-8222-222222222222")]
    [InlineData("__Host-mydmsvn-rt=AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA")]
    [InlineData("__Host-mydmsvn-rt=00000000-0000-0000-0000-000000000000")]
    public async Task Refresh_invalid_cookie_never_issues_credentials(string value)
    {
        await using var fixture = await LiveFixture.StartAsync(authControlProfile: true);
        using var response = await fixture.SendControlAsync("POST", "/api/v1/auth/refresh", null, false, false,
            extraHeader: "Cookie", extraValues: new[] { value });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("sales", true)]
    [InlineData("goods-receipts", true)]
    [InlineData("inventory-balances", false)]
    [InlineData("inventory-stock-card", false)]
    public async Task Capabilities_preserve_exact_shape(string feature, bool writable)
    {
        await using var fixture = await LiveFixture.StartAsync(authControlProfile: true);
        using var response = await fixture.SendControlAsync("GET", "/api/v1/auth/capabilities?feature=" + feature, null, true, false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("read", out _));
        Assert.Equal(writable, data.TryGetProperty("write", out _));
        Assert.Equal(writable, data.TryGetProperty("post", out _));
        Assert.Equal(writable ? 3 : 1, data.EnumerateObject().Count());
    }

    [Theory]
    [InlineData("/api/v1/health")]
    [InlineData("/API/V1/HEALTH/")]
    public async Task Health_optional_bearer_requires_valid_active_session(string path)
    {
        await using var fixture = await LiveFixture.StartAsync(authControlProfile: true);
        using var anonymous = await fixture.SendControlAsync("GET", path, null, false, false);
        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
        Assert.Equal(0, fixture.SessionValidationCalls);
        using var invalid = await fixture.SendWithTokenAsync(path, "invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.Equal(0, fixture.SessionValidationCalls);
        using var active = await fixture.SendControlAsync("GET", path, null, true, false);
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        Assert.Equal(1, fixture.SessionValidationCalls);
        using var logout = await fixture.SendControlAsync("POST", "/api/v1/auth/logout", null, false, true);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        using var revoked = await fixture.SendControlAsync("GET", path, null, true, false);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.Equal(2, fixture.SessionValidationCalls);
    }

    [Theory]
    [InlineData("POST", "/api/v1/auth/refresh", "VPS01-AUTH-REFRESH")]
    [InlineData("POST", "/api/v1/auth/logout", "VPS01-AUTH-LOGOUT")]
    public async Task Session_store_failure_does_not_emit_cookie_or_secret(string verb, string path, string failure)
    {
        await using var fixture = await LiveFixture.StartAsync(authControlProfile: true, controlFailure: failure);
        using var response = await fixture.SendControlAsync(verb, path, null, false, true);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.False(response.Headers.Contains("Location"));
        Assert.DoesNotContain("secret SQL", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_and_logout_cookie_attributes_are_exact()
    {
        await using var fixture = await LiveFixture.StartAsync(authControlProfile: true);
        using var refresh = await fixture.SendControlAsync("POST", "/api/v1/auth/refresh", null, false, true);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var cookie = Assert.Single(refresh.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("__Host-mydmsvn-rt=33333333-3333-4333-8333-333333333333; Max-Age=", cookie, StringComparison.Ordinal);
        Assert.EndsWith("; Path=/; Secure; HttpOnly; SameSite=Strict", cookie, StringComparison.Ordinal);
        using var logout = await fixture.SendControlAsync("POST", "/api/v1/auth/logout", null, false, true);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal("__Host-mydmsvn-rt=; Max-Age=0; Path=/; Secure; HttpOnly; SameSite=Strict",
            Assert.Single(logout.Headers.GetValues("Set-Cookie")));
    }

    [Fact]
    public async Task Valid_login_binds_credentials_and_issues_cookie_without_duplicate_validation()
    {
        await using var fixture = await LiveFixture.StartAsync(authControlProfile: true);
        using var login = await fixture.SendControlAsync("POST", "/api/v1/auth/login", Valid, false, false);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(0, fixture.SessionValidationCalls);
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty("accessToken", out _));
        Assert.False(data.TryGetProperty("refreshToken", out _));
        Assert.Equal(1, data.GetProperty("user").GetProperty("userId").GetInt32());
        Assert.StartsWith(Cookie + "; Max-Age=", Assert.Single(login.Headers.GetValues("Set-Cookie")), StringComparison.Ordinal);
    }

    // Direct deterministic post-commit fault oracle: cancellation is triggered
    // by the session store only AFTER it records the outcome. This is not a
    // claim of a real network disconnect/SQL commit.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Post_commit_session_outcome_is_not_rewritten_by_request_cancellation(bool logout)
    {
        using var cancellation = new CancellationTokenSource();
        var store = new CommittingStore(cancellation);
        var audit = new RecordingAudit();
        var clock = new FixedClock();
        var options = new WebHostOptions("https://localhost", "https://localhost", "native-auth-test",
            "test", new Dictionary<string, byte[]> { ["test"] = Enumerable.Repeat((byte)0x5A, 32).ToArray() });
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        context.Request.Body = Stream.Null;
        context.Request.Headers.Cookie = Cookie;
        var controller = new NativeAuth(null!, store, clock, null!, new HttpCorrelationAccessor("auth_cancel"),
            audit, options, new AdmitAll()) { ControllerContext = new ControllerContext { HttpContext = context } };

        var result = logout ? await controller.Logout(cancellation.Token) : await controller.Refresh(cancellation.Token);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, store.Commits);
        Assert.Equal(1, audit.Calls);
        Assert.False(audit.ObservedToken.CanBeCanceled);
        Assert.Equal(logout, audit.CommittedFlag);
        Assert.Equal(200, Assert.IsType<JsonResult>(result).StatusCode);
        Assert.True(context.Response.Headers.ContainsKey("Set-Cookie"));
    }

    private sealed class FixedClock : IUtcClock
    {
        public DateTime UtcNow => new(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc);
    }

    private sealed class AdmitAll : INativeAuthAttemptAdmission
    {
        public bool TryAdmit(string operation, string subject, string remoteIp, DateTime now) => true;
    }

    private sealed class RecordingAudit : ISecurityAuditWriter
    {
        public int Calls { get; private set; }
        public CancellationToken ObservedToken { get; private set; }
        public bool CommittedFlag { get; private set; }

        public Task WriteAsync(SecurityAuditEntry entry, bool businessOperationCommitted, CancellationToken cancellationToken)
        {
            Calls++;
            ObservedToken = cancellationToken;
            CommittedFlag = businessOperationCommitted;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class CommittingStore(CancellationTokenSource cancellation) : IWebAuthSessionStore
    {
        public int Commits { get; private set; }

        public Task<WebAuthSessionToken?> IssueAsync(int userId, CancellationToken token) => throw new NotSupportedException();
        public Task<CurrentUser?> GetActiveUserAsync(Guid sessionId, int userId, CancellationToken token) => throw new NotSupportedException();

        public Task<WebAuthSessionToken?> RotateAsync(string refreshToken, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Commits++;
            cancellation.Cancel();
            return Task.FromResult<WebAuthSessionToken?>(new WebAuthSessionToken(
                Guid.Parse("11111111-1111-4111-8111-111111111111"),
                CurrentUser.Authenticated(1, "alice", "Alice", true),
                "33333333-3333-4333-8333-333333333333",
                new DateTime(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 12, 7, 9, 0, 0, DateTimeKind.Utc)));
        }

        public Task RevokeAsync(string refreshToken, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Commits++;
            cancellation.Cancel();
            return Task.CompletedTask;
        }
    }
}
