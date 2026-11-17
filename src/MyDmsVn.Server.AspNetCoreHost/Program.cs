using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.Application.Identity;
using MyDmsVn.Server.Infrastructure;
using MyDmsVn.Server.Infrastructure.Operations;

namespace MyDmsVn.Server.HttpHost;

// VPS-04A migration host. It is intentionally not the production VPS-08 deployment entry point.
// The legacy Web API 2/OWIN host remains a separate net48 build until the VPS-04B gates pass.
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
            Console.Error.WriteLine("VPS-04A host startup refused. Verify SQL/schema, secrets, audit and transport configuration.");
            Environment.ExitCode = 1;
        }
    }

    private static async Task<WebApplication> CreateAppAsync(string[] args)
    {
        var options = WebHostOptions.FromEnvironment();
        // WEB6 protected file audit uses a Windows-only ACL contract. Until the portable
        // fail-closed audit sink is implemented in VPS-04B, do not silently weaken it.
        if (options.SecurityAuditPath != null)
            throw new InvalidOperationException("Portable protected audit storage has not been admitted.");

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
        builder.Services.AddServerApplication(superuser).AddHttpRequestIdentity();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(sessions);
        builder.Services.AddSingleton<AuthAttemptLimiter>();
        builder.Services.Replace(ServiceDescriptor.Singleton<ISecurityAuditSink, TraceSecurityAuditSink>());
        builder.Services.AddSqlPersistence(connection!);

        var app = builder.Build();
        app.Use(CoreApiBoundary.HandleAsync);
        Net10RouteTable.Map(app);
        Net10RouteTable.MapFallback(app);
        return app;
    }
}
