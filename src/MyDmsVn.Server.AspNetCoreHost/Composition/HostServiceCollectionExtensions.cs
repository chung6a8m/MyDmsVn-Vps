using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.Application.Identity;
using MyDmsVn.Server.Infrastructure;
using MyDmsVn.Server.Infrastructure.Operations;

namespace MyDmsVn.Server.HttpHost;

internal static class HostServiceCollectionExtensions
{
    internal static IServiceCollection AddVps10HostServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        var deployment = HostOptionsRegistration.Read(configuration);
        var options = deployment.Web;

        services.AddServerApplication(deployment.SuperuserUsername).AddHttpRequestIdentity();
        services.AddProductionGeneratedMediator();
        services.AddSingleton(deployment);
        services.AddSingleton(options);
        services.AddSingleton(deployment.Sessions);
        services.AddSingleton<AuthAttemptLimiter>();
        services.AddScoped<CoreApiAdmissionMiddleware>();
        services.AddSingleton<ISecurityAuditReadiness, CoreSecurityAuditReadiness>();

        if (deployment.Proxy != null)
            services.Configure<ForwardedHeadersOptions>(LoopbackProxyBoundary.ConfigureForwardedHeaders);

        // The protected audit sink is resolved explicitly by HostStartupChecks
        // after the SQL gates, rather than deferred to the first HTTP request.
        if (options.SecurityAuditPath != null)
        {
            services.Replace(ServiceDescriptor.Singleton<ISecurityAuditSink>(_ =>
            {
                if (!OperatingSystem.IsLinux())
                    throw new InvalidOperationException("Portable protected audit is Linux-only.");
                return new PortableProtectedSecurityAuditSink(options);
            }));
        }
        else
        {
            if (options.Web6ResendEligibilityEnabled)
                throw new InvalidOperationException("WEB6 requires protected audit storage.");
            services.Replace(ServiceDescriptor.Singleton<ISecurityAuditSink, TraceSecurityAuditSink>());
        }

        services.AddSqlPersistence(deployment.ConnectionString);
        services.AddSingleton<ISchemaCompatibilityChecker>(_ =>
            options.Web5Enabled
                ? new SqlWeb5SchemaCompatibilityChecker(deployment.ConnectionString)
                : options.Web4Enabled
                    ? new SqlWebSchemaCompatibilityChecker(deployment.ConnectionString)
                    : new SqlWebSchema11CompatibilityChecker(deployment.ConnectionString));
        services.AddSingleton<IRuntimeSqlPrincipalValidator>(new SqlWebRuntimePrincipalValidator(
            requireJournal: options.Web4Enabled,
            requireResolution: options.Web5Enabled,
            requireDesktopApi: options.DesktopApiEnabled));
        services.AddSingleton<ISuperuserBindingValidator, SqlSuperuserBindingValidator>();
        return services;
    }
}
