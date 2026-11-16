using System;
using System.Threading;
using System.Threading.Tasks;

namespace MyDmsVn.Desktop.Application
{
    /// <summary>Read-only support identity. No credentials or command payloads are retained.</summary>
    public sealed class DesktopPendingWriteInfo
    {
        public DesktopPendingWriteInfo(string operationId, int actorUserId, string protocol,
            string kind, string method, string route, long? targetId,
            string? deploymentEpoch, string fingerprint, DateTime createdUtc)
        {
            OperationId = operationId;
            ActorUserId = actorUserId;
            Protocol = protocol;
            Kind = kind;
            Method = method;
            Route = route;
            TargetId = targetId;
            DeploymentEpoch = deploymentEpoch;
            Fingerprint = fingerprint;
            CreatedUtc = createdUtc;
        }

        public string OperationId { get; }
        public int ActorUserId { get; }
        public string Protocol { get; }
        public string Kind { get; }
        public string Method { get; }
        public string Route { get; }
        public long? TargetId { get; }
        public string? DeploymentEpoch { get; }
        public string Fingerprint { get; }
        public DateTime CreatedUtc { get; }
    }

    public sealed class DesktopWriteRecoveryResult
    {
        public DesktopWriteRecoveryResult(string state, string explanation,
            bool fenceReleased = false, string? caseId = null)
        {
            State = state;
            Explanation = explanation;
            FenceReleased = fenceReleased;
            CaseId = caseId;
        }

        public string State { get; }
        public string Explanation { get; }
        public bool FenceReleased { get; }
        public string? CaseId { get; }
    }

    /// <summary>
    /// Attended recovery only. Checking an operation may NEVER resend a mutation.
    /// No method authorizes an unverified local reset of an UNKNOWN operation.
    /// </summary>
    public interface IDesktopWriteRecovery
    {
        event EventHandler? PendingChanged;
        DesktopPendingWriteInfo? Pending { get; }
        Task<DesktopWriteRecoveryResult> CheckAsync(CancellationToken cancellationToken);
        Task<DesktopWriteRecoveryResult> AcceptCertifiedNotExecutedAsync(CancellationToken cancellationToken);
    }
}
