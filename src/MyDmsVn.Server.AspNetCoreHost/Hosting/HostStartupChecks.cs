using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Server.Application.Identity;
using MyDmsVn.Server.Infrastructure.Operations;

namespace MyDmsVn.Server.HttpHost;

internal static class HostStartupChecks
{
    // This is an explicit pre-listen barrier, not an IHostedService or a
    // readiness probe that can race a listening Kestrel server.
    internal static async Task ValidateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deployment = services.GetRequiredService<HostDeploymentOptions>();
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            // Keep one 30-second budget for all three existing SQL checks.
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var compatibility = await services.GetRequiredService<ISchemaCompatibilityChecker>()
                .CheckAsync(timeout.Token).ConfigureAwait(false);
            if (!compatibility.IsCompatible)
                throw new InvalidOperationException("Incompatible web database schema.");
            await services.GetRequiredService<IRuntimeSqlPrincipalValidator>()
                .ValidateAsync(deployment.ConnectionString, timeout.Token).ConfigureAwait(false);
            await services.GetRequiredService<ISuperuserBindingValidator>()
                .ValidateAsync(deployment.ConnectionString, deployment.SuperuserUsername, timeout.Token)
                .ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Constructor proves the pre-provisioned 0700 root and 0600 file
        // with no-follow descriptor checks and durable fsync.
        if (deployment.Web.SecurityAuditPath != null)
            _ = services.GetRequiredService<ISecurityAuditSink>();
    }
}
