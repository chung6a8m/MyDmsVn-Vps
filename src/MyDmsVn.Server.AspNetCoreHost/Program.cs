using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MyDmsVn.Server.HttpHost;

// Composition root: configuration, registrations, pre-listen validation and pipeline.
internal static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--list-routes")
        {
            Console.WriteLine(NativeEndpointRoutes.ToInventoryJson());
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

    // Retain fail-closed request identity/correlation lifetimes in Production.
    internal static void RequireScopeValidation(WebApplicationBuilder builder) =>
        builder.Host.UseDefaultServiceProvider(options => options.ValidateScopes = true);

    private static async Task<WebApplication> CreateAppAsync(string[] args)
    {
        var configuration = HostOptionsRegistration.ReadEnvironment();
        var builder = WebApplication.CreateBuilder(args);
        RequireScopeValidation(builder);
        builder.Services.AddVps10HostServices(configuration);
        HostOptionsRegistration.ConfigureListener(builder, configuration);
        return await BuildAndValidateAsync(builder, CancellationToken.None).ConfigureAwait(false);
    }

    internal static async Task<WebApplication> BuildAndValidateAsync(
        WebApplicationBuilder builder, CancellationToken cancellationToken)
    {
        var app = builder.Build();
        try
        {
            await HostStartupChecks.ValidateAsync(app.Services, cancellationToken).ConfigureAwait(false);
            ConfigurePipeline(app);
            return app;
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static void ConfigurePipeline(WebApplication app)
    {
        var proxy = app.Services.GetRequiredService<HostDeploymentOptions>().Proxy;
        if (proxy != null)
        {
            app.Use((context, next) => LoopbackProxyBoundary.AdmitAsync(context, next, proxy));
            app.UseForwardedHeaders();
            app.Use(async (context, next) =>
            {
                // ASP.NET Core stores consumed values as X-Original-*; prevent them
                // from entering the admitted HTTP boundary.
                context.Request.Headers.Remove("X-Original-For");
                context.Request.Headers.Remove("X-Original-Proto");
                await next().ConfigureAwait(false);
            });
        }
        // Correlation, transport, CSRF, body size, Bearer+SQL session and
        // audit readiness run after routing, before endpoint dispatch.
        app.UseRouting();
        app.UseMiddleware<CoreApiAdmissionMiddleware>();
        app.Use((context, next) => NativeApiRegistration.AdmitDesktopFeatureAsync(context, next));
        NativeEndpointRoutes.Map(app);
        NativeApiRegistration.MapNativeOpenApi(app);
        NativeEndpointRoutes.MapFallback(app);
    }
}
