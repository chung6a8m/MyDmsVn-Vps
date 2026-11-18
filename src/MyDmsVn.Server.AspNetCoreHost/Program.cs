using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.Application.Identity;
using MyDmsVn.Server.Infrastructure;
using MyDmsVn.Server.Infrastructure.Operations;

namespace MyDmsVn.Server.HttpHost;

// VPS-04B: Linux direct-HTTPS host with portable protected WEB6 audit.
// Reverse proxy trust, systemd deployment and production approval are VPS-08 gates.
internal static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--list-routes")
        {
            Console.WriteLine(Net10RouteTable.ToInventoryJson());
            return;
        }

        try
        {
            var app = await CreateAppAsync(args).ConfigureAwait(false);
            await app.RunAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Do not expose SQL connection details, JWT key material or deployment configuration.
            Console.Error.WriteLine("VPS-04B host startup refused. Verify SQL/schema, secrets, audit and transport configuration.");
            Environment.ExitCode = 1;
        }
    }

    // The Web API 2 host used ValidateScopes=true in every environment.
    // ASP.NET Core enables it by default only in Development; retain fail-closed
    // request identity/correlation lifetimes on a Linux Production deployment.
    internal static void RequireScopeValidation(WebApplicationBuilder builder) =>
        builder.Host.UseDefaultServiceProvider(options => options.ValidateScopes = true);

    private static async Task<WebApplication> CreateAppAsync(string[] args)
    {
        var options = WebHostOptions.FromEnvironment();
        var sessions = WebAuthSessionConfiguration.Read(Environment.GetEnvironmentVariable);
        var connection = Environment.GetEnvironmentVariable("MYDMSVN_WEB_SQL_CONNECTION_STRING");
        var superuser = Environment.GetEnvironmentVariable("MYDMSVN_WEB_SUPERUSER_USERNAME");
        if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(superuser))
            throw new InvalidOperationException("Missing restricted web deployment settings.");

        // Keep the exact legacy schema/principal/superuser refusal gates, before opening a listener.
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            var compatibility = await (options.Web5Enabled
                ? new SqlWeb5SchemaCompatibilityChecker(connection!).CheckAsync(timeout.Token)
                : options.Web4Enabled
                    ? new SqlWebSchemaCompatibilityChecker(connection!).CheckAsync(timeout.Token)
                    : new SqlWebSchema11CompatibilityChecker(connection!).CheckAsync(timeout.Token))
                .ConfigureAwait(false);
            if (!compatibility.IsCompatible)
                throw new InvalidOperationException("Incompatible web database schema.");
            await new SqlWebRuntimePrincipalValidator(
                requireJournal: options.Web4Enabled,
                requireResolution: options.Web5Enabled,
                requireDesktopApi: options.DesktopApiEnabled)
                .ValidateAsync(connection!, timeout.Token).ConfigureAwait(false);
            await new SqlSuperuserBindingValidator()
                .ValidateAsync(connection!, superuser!, timeout.Token).ConfigureAwait(false);
        }

        var builder = WebApplication.CreateBuilder(args);
        RequireScopeValidation(builder);
        builder.Services.AddServerApplication(superuser).AddHttpRequestIdentity();
        builder.Services.AddProductionGeneratedMediator();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(sessions);
        builder.Services.AddSingleton<AuthAttemptLimiter>();
        builder.Services.AddScoped<CoreApiAdmissionMiddleware>();
        builder.Services.AddSingleton<ISecurityAuditReadiness, CoreSecurityAuditReadiness>();
        if (options.SecurityAuditPath != null)
        {
            if (!OperatingSystem.IsLinux())
                throw new InvalidOperationException("Portable protected audit is Linux-only.");
            // Constructor proves a pre-provisioned 0700 root and 0600 file via
            // no-follow descriptor checks and a durable fsync, before listening.
            builder.Services.Replace(ServiceDescriptor.Singleton<ISecurityAuditSink>(
                new PortableProtectedSecurityAuditSink(options)));
        }
        else
        {
            if (options.Web6ResendEligibilityEnabled)
                throw new InvalidOperationException("WEB6 requires protected audit storage.");
            builder.Services.Replace(ServiceDescriptor.Singleton<ISecurityAuditSink, TraceSecurityAuditSink>());
        }
        builder.Services.AddSqlPersistence(connection!);

        var app = builder.Build();
        // Correlation, transport, strict CSRF, body size, Bearer+SQL session and
        // audit readiness must run after routing, before any endpoint dispatch.
        app.UseRouting();
        app.UseMiddleware<CoreApiAdmissionMiddleware>();
        Net10RouteTable.Map(app);
        Net10RouteTable.MapFallback(app);
        return app;
    }
}
