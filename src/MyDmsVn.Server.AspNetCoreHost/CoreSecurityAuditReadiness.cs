using System;
using System.Threading;
using MyDmsVn.Server.Application.Identity;

namespace MyDmsVn.Server.HttpHost;

// The native health gate is request-scoped middleware, not Web API 2 filters.
internal sealed class CoreSecurityAuditReadiness : ISecurityAuditReadiness
{
    private SecurityAuditReadinessSnapshot _snapshot = new();

    public SecurityAuditReadinessSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public void MarkDegraded(DateTime failedAtUtc) =>
        Volatile.Write(ref _snapshot, new SecurityAuditReadinessSnapshot(failedAtUtc));
}
