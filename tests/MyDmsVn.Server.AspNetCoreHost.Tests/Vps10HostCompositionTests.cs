using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.Application.Identity;
using MyDmsVn.Server.HttpHost;
using MyDmsVn.Server.Infrastructure.Operations;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps10HostCompositionTests
{
    private const string Origin = "https://app.example.test";
    private const string Connection = "Server=localhost;Database=composition_only;Encrypt=True;TrustServerCertificate=False";
    private const string Username = "composition-user";
    private const string ProxyToken = "Composition_Proxy_Token_1234567890abcd";

    private static Dictionary<string, string?> Settings(string mode = "legacy-web11") => new()
    {
        ["MYDMSVN_WEB_ORIGIN"] = Origin,
        ["MYDMSVN_WEB_JWT_ISSUER"] = Origin,
        ["MYDMSVN_WEB_JWT_AUDIENCE"] = "composition",
        ["MYDMSVN_WEB_JWT_ACTIVE_KID"] = "test-key",
        ["MYDMSVN_WEB_JWT_KEYS"] = "test-key:" + Convert.ToBase64String(new byte[32]),
        ["MYDMSVN_WEB_OPERATION_MODE"] = mode,
        ["MYDMSVN_WEB_SQL_CONNECTION_STRING"] = Connection,
        ["MYDMSVN_WEB_SUPERUSER_USERNAME"] = Username
    };

    private static IConfiguration Config(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static void EnableProxy(Dictionary<string, string?> settings)
    {
        settings["MYDMSVN_VPS08_PROXY_MODE"] = "loopback-nginx-v1";
        settings["MYDMSVN_VPS08_PROXY_TOKEN"] = ProxyToken;
        settings["ASPNETCORE_URLS"] = LoopbackProxyBoundary.LoopbackUrl;
    }

    private static string EnableProtectedAudit(Dictionary<string, string?> settings)
    {
        var root = Path.Combine(Path.GetTempPath(), "vps10-composition-" + Guid.NewGuid().ToString("N"));
        settings["MYDMSVN_WEB6_RESEND_ELIGIBILITY"] = "receipt-v1";
        settings["MYDMSVN_WEB6_CANDIDATE_ID"] = "composition";
        settings["MYDMSVN_WEB6_CANDIDATE_ROOT"] = root;
        settings["MYDMSVN_WEB_SECURITY_AUDIT_PATH"] = Path.Combine(root, "audit.jsonl");
        return root;
    }

    private static WebApplicationBuilder Builder(IConfiguration configuration)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
            Args = Array.Empty<string>()
        });
        Program.RequireScopeValidation(builder);
        builder.Services.AddVps10HostServices(configuration);
        HostOptionsRegistration.ConfigureListener(builder, configuration);
        return builder;
    }

    [Theory]
    [InlineData("legacy-web11", false, false, typeof(SqlWebSchema11CompatibilityChecker))]
    [InlineData("web4", true, false, typeof(SqlWebSchemaCompatibilityChecker))]
    [InlineData("web5", true, true, typeof(SqlWeb5SchemaCompatibilityChecker))]
    public void Composition_preserves_modes_and_defaults(string mode, bool journal, bool resolution, Type checker)
    {
        var builder = Builder(Config(Settings(mode)));
        using var provider = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var options = provider.GetRequiredService<WebHostOptions>();
        var sessions = provider.GetRequiredService<WebAuthSessionOptions>();
        Assert.Equal(journal, options.Web4Enabled);
        Assert.Equal(resolution, options.Web5Enabled);
        Assert.False(options.DesktopApiEnabled);
        Assert.False(options.Web6ResendEligibilityEnabled);
        Assert.Equal(10, options.AccessLifetimeMinutes);
        Assert.Equal(1048576, options.MaximumRequestBytes);
        Assert.Equal(30, options.ClockSkewSeconds);
        Assert.Equal(7, sessions.IdleLifetimeDays);
        Assert.Equal(30, sessions.AbsoluteLifetimeDays);
        Assert.Equal(checker, provider.GetRequiredService<ISchemaCompatibilityChecker>().GetType());
        Assert.IsType<SqlWebRuntimePrincipalValidator>(provider.GetRequiredService<IRuntimeSqlPrincipalValidator>());
        Assert.IsType<SqlSuperuserBindingValidator>(provider.GetRequiredService<ISuperuserBindingValidator>());
        Assert.IsType<TraceSecurityAuditSink>(provider.GetRequiredService<ISecurityAuditSink>());
        Assert.Null(provider.GetRequiredService<HostDeploymentOptions>().Proxy);
    }

    [Fact]
    public void Deployment_snapshot_reads_exact_names_once_and_preserves_empty_values()
    {
        var settings = Settings("web5");
        settings["MYDMSVN_WEB_DESKTOP_API"] = "enabled";
        settings["MYDMSVN_WEB_ACCESS_LIFETIME_MINUTES"] = "4";
        settings["MYDMSVN_WEB_REFRESH_IDLE_DAYS"] = "2";
        settings["MYDMSVN_WEB_REFRESH_ABSOLUTE_DAYS"] = "9";
        settings["MYDMSVN_WEB6_CANDIDATE_ID"] = "";
        settings["mydmsvn_web_origin"] = "https://wrong.example.test";
        var calls = new List<string>();
        var configuration = HostOptionsRegistration.Snapshot(key =>
        {
            calls.Add(key);
            return settings.GetValueOrDefault(key);
        });
        settings["MYDMSVN_WEB_ORIGIN"] = "https://changed.example.test";
        var deployment = HostOptionsRegistration.Read(configuration);
        Assert.Equal(calls.Count, calls.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("mydmsvn_web_origin", calls);
        Assert.Equal("", configuration["MYDMSVN_WEB6_CANDIDATE_ID"]);
        Assert.Equal(Origin, deployment.Web.PublicOrigin);
        Assert.True(deployment.Web.DesktopApiEnabled);
        Assert.Equal(4, deployment.Web.AccessLifetimeMinutes);
        Assert.Equal(2, deployment.Sessions.IdleLifetimeDays);
        Assert.Equal(9, deployment.Sessions.AbsoluteLifetimeDays);
        Assert.DoesNotContain(Connection, deployment.ToString()!);
    }

    [Theory]
    [InlineData("MYDMSVN_WEB_ORIGIN", "http://app.example.test")]
    [InlineData("MYDMSVN_WEB_JWT_KEYS", "invalid")]
    [InlineData("MYDMSVN_WEB_OPERATION_MODE", "WEB5")]
    [InlineData("MYDMSVN_WEB_ACCESS_LIFETIME_MINUTES", "+1")]
    [InlineData("MYDMSVN_WEB_REFRESH_IDLE_DAYS", "8")]
    [InlineData("MYDMSVN_WEB_DESKTOP_API", "")]
    [InlineData("MYDMSVN_WEB_SQL_CONNECTION_STRING", " ")]
    [InlineData("MYDMSVN_WEB_SUPERUSER_USERNAME", null)]
    [InlineData("MYDMSVN_WEB6_RESEND_ELIGIBILITY", "receipt-v1")]
    [InlineData("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true")]
    public void Invalid_deployment_refuses_composition_before_startup(string key, string? value)
    {
        var settings = Settings("web5");
        settings[key] = value;
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddVps10HostServices(Config(settings)));
        Assert.Empty(services);
    }

    [Theory]
    [InlineData("ASPNETCORE_URLS", "http://0.0.0.0:5080")]
    [InlineData("MYDMSVN_VPS08_PROXY_TOKEN", "short")]
    [InlineData("MYDMSVN_WEB_SQL_CONNECTION_STRING", "Server=localhost;Encrypt=False")]
    [InlineData("MYDMSVN_WEB_SQL_CONNECTION_STRING", "Server=localhost;Encrypt=True;TrustServerCertificate=True")]
    public void Proxy_configuration_refuses_unsafe_listener_or_sql_transport(string key, string value)
    {
        var settings = Settings();
        EnableProxy(settings);
        settings[key] = value;
        Assert.Throws<InvalidOperationException>(() => HostOptionsRegistration.Read(Config(settings)));
    }

    [Theory]
    [InlineData("schema", "schema")]
    [InlineData("principal", "schema,principal")]
    [InlineData("superuser", "schema,principal,superuser")]
    [InlineData("audit", "schema,principal,superuser,audit")]
    public async Task Refused_startup_stops_before_listener_and_later_checks(string failure, string expected)
    {
        var settings = Settings("web5");
        EnableProtectedAudit(settings);
        var builder = Builder(Config(settings));
        var probe = new StartupProbe(failure);
        ReplaceSqlChecks(builder.Services, probe);
        builder.Services.Replace(ServiceDescriptor.Singleton<ISecurityAuditSink>(_ =>
        {
            probe.Visit("audit");
            return new TraceSecurityAuditSink();
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Program.BuildAndValidateAsync(builder, CancellationToken.None));
        Assert.Equal(expected, string.Join(",", probe.Visits));
        Assert.False(probe.ListenerStarted);
    }

    [Fact]
    public async Task Missing_protected_audit_file_fails_real_sink_before_listener_without_creating_it()
    {
        Assert.True(OperatingSystem.IsLinux());
        var settings = Settings("web5");
        var root = EnableProtectedAudit(settings);
        var builder = Builder(Config(settings));
        var probe = new StartupProbe();
        ReplaceSqlChecks(builder.Services, probe);
        Assert.False(Directory.Exists(root));
        await Assert.ThrowsAsync<IOException>(() =>
            Program.BuildAndValidateAsync(builder, CancellationToken.None));
        Assert.Equal(new[] { "schema", "principal", "superuser" }, probe.Visits);
        Assert.False(probe.ListenerStarted);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task Canceled_startup_does_not_execute_checks_or_start_listener()
    {
        var builder = Builder(Config(Settings()));
        var probe = new StartupProbe();
        ReplaceSqlChecks(builder.Services, probe);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Program.BuildAndValidateAsync(builder, cancel.Token));
        Assert.Empty(probe.Visits);
        Assert.False(probe.ListenerStarted);
    }

    [Fact]
    public async Task Production_scope_validation_and_identity_correlation_are_isolated()
    {
        var builder = Builder(Config(Settings()));
        var probe = new StartupProbe();
        ReplaceSqlChecks(builder.Services, probe);
        await using var app = await Program.BuildAndValidateAsync(builder, CancellationToken.None);
        Assert.False(probe.ListenerStarted);
        Assert.False(app.Lifetime.ApplicationStarted.IsCancellationRequested);
        Assert.Equal(new[] { "schema", "principal", "superuser" }, probe.Visits);
        Assert.Throws<InvalidOperationException>(() => app.Services.GetRequiredService<IMediator>());
        Assert.Throws<InvalidOperationException>(() => app.Services.GetRequiredService<RequestIdentityAdmission>());
        Assert.Throws<InvalidOperationException>(() => app.Services.GetRequiredService<ISecurityAuditWriter>());
        using var first = app.Services.CreateScope();
        using var second = app.Services.CreateScope();
        var a = first.ServiceProvider.GetRequiredService<RequestIdentityAdmission>();
        var b = second.ServiceProvider.GetRequiredService<RequestIdentityAdmission>();
        Assert.NotSame(a, b);
        Assert.Throws<InvalidOperationException>(() => first.ServiceProvider.GetRequiredService<ICurrentUserAccessor>());
        Assert.Throws<InvalidOperationException>(() => second.ServiceProvider.GetRequiredService<IRequestCorrelationAccessor>());
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        a.BeginCorrelation("request_a");
        a.Admit(CurrentUser.Authenticated(31, "a", "A", true), sessionA);
        b.BeginCorrelation("request_b");
        b.Admit(CurrentUser.Authenticated(42, "b", "B", true), sessionB);
        Assert.Equal(31, first.ServiceProvider.GetRequiredService<ICurrentUserAccessor>().Current.UserId);
        Assert.Equal(42, second.ServiceProvider.GetRequiredService<ICurrentUserAccessor>().Current.UserId);
        Assert.Equal("request_a", first.ServiceProvider.GetRequiredService<IRequestCorrelationAccessor>().CorrelationId);
        Assert.Equal("request_b", second.ServiceProvider.GetRequiredService<IRequestCorrelationAccessor>().CorrelationId);
        Assert.Equal(sessionA, a.SessionId);
        Assert.Equal(sessionB, b.SessionId);
        Assert.Throws<InvalidOperationException>(() => a.BeginCorrelation("replacement"));
        Assert.Throws<InvalidOperationException>(() => a.Admit(CurrentUser.Anonymous));
        Assert.NotSame(first.ServiceProvider.GetRequiredService<IMediator>(), second.ServiceProvider.GetRequiredService<IMediator>());
        Assert.NotSame(first.ServiceProvider.GetRequiredService<ISecurityAuditWriter>(), second.ServiceProvider.GetRequiredService<ISecurityAuditWriter>());
    }

    [Fact]
    public async Task Production_pipeline_rejects_untrusted_proxy_then_admits_health_with_distinct_correlations()
    {
        Assert.True(OperatingSystem.IsLinux());
        var settings = Settings();
        EnableProxy(settings);
        var builder = Builder(Config(settings));
        // Disposable test listener only. Production ConfigureListener uses the fixed port.
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var probe = new StartupProbe();
        ReplaceSqlChecks(builder.Services, probe);
        await using var app = await Program.BuildAndValidateAsync(builder, CancellationToken.None);
        Assert.False(probe.ListenerStarted);
        await app.StartAsync();
        try
        {
            Assert.True(probe.ListenerStarted);
            var url = Assert.Single(app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            async Task Check(string correlation, bool trusted)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url + "/api/v1/health");
                request.Headers.Host = "app.example.test";
                request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
                request.Headers.TryAddWithoutValidation("X-Forwarded-For", "198.51.100.17");
                request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlation);
                if (trusted)
                    request.Headers.TryAddWithoutValidation("X-MyDmsVn-Proxy-Token", ProxyToken);
                using var response = await client.SendAsync(request);
                Assert.Equal(trusted ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
                if (trusted)
                {
                    Assert.Equal(correlation, Assert.Single(response.Headers.GetValues("X-Correlation-ID")));
                    Assert.Contains("\"status\":\"ok\"", await response.Content.ReadAsStringAsync());
                }
                else
                    Assert.False(response.Headers.Contains("X-Correlation-ID"));
            }

            await Check("untrusted", false);
            await Task.WhenAll(Check("request_a", true), Check("request_b", true));
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static void ReplaceSqlChecks(IServiceCollection services, StartupProbe probe)
    {
        services.Replace(ServiceDescriptor.Singleton<ISchemaCompatibilityChecker>(probe));
        services.Replace(ServiceDescriptor.Singleton<IRuntimeSqlPrincipalValidator>(probe));
        services.Replace(ServiceDescriptor.Singleton<ISuperuserBindingValidator>(probe));
        services.AddSingleton<IHostedService>(probe);
    }

    private sealed class StartupProbe : ISchemaCompatibilityChecker, IRuntimeSqlPrincipalValidator,
        ISuperuserBindingValidator, IHostedService
    {
        private readonly string? _failure;
        private CancellationToken? _sqlToken;
        internal StartupProbe(string? failure = null) => _failure = failure;
        internal List<string> Visits { get; } = new();
        internal bool ListenerStarted { get; private set; }

        internal void Visit(string stage)
        {
            Visits.Add(stage);
            if (_failure == stage && stage != "schema")
                throw new InvalidOperationException("Synthetic startup refusal.");
        }

        private void CheckSqlToken(CancellationToken token)
        {
            Assert.True(token.CanBeCanceled);
            Assert.False(token.IsCancellationRequested);
            if (_sqlToken.HasValue)
                Assert.Equal(_sqlToken.Value, token);
            _sqlToken = token;
        }

        public Task<SchemaCompatibilityResult> CheckAsync(CancellationToken cancellationToken)
        {
            CheckSqlToken(cancellationToken);
            Visit("schema");
            return Task.FromResult(new SchemaCompatibilityResult(
                _failure == "schema" ? SchemaCompatibilityStatus.Partial : SchemaCompatibilityStatus.Compatible,
                "Schema.Test", "Synthetic test result."));
        }

        public Task ValidateAsync(string connectionString, CancellationToken cancellationToken)
        {
            Assert.Equal(Connection, connectionString);
            CheckSqlToken(cancellationToken);
            Visit("principal");
            return Task.CompletedTask;
        }

        public Task ValidateAsync(string connectionString, string configuredUsername, CancellationToken cancellationToken)
        {
            Assert.Equal(Connection, connectionString);
            Assert.Equal(Username, configuredUsername);
            CheckSqlToken(cancellationToken);
            Visit("superuser");
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            ListenerStarted = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
