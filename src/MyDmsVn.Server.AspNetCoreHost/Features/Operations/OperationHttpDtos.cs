using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MyDmsVn.Server.Application.Operations;

namespace MyDmsVn.Server.HttpHost.Features.Operations;

public sealed record OperationContextResponse(string DeploymentEpoch, int ActorUserId, string ContractVersion);
public sealed record ResolutionContextResponse(string ContractVersion, string DeploymentEpoch, int ActorUserId, bool SameKeyResendEnabled);
public sealed record OperationStatusResponse(string DeploymentEpoch, string OperationId, string OperationKind,
    string Method, string RouteScope, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? RequestTargetId,
    string RequestFingerprint, string State, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] JsonElement? Outcome);
public sealed record TerminalResponse(string DecisionId, string CaseId, string DecidedAtUtc);
public sealed record ResolutionResponse(string ContractVersion, int ActorUserId, string DeploymentEpoch, string OperationId,
    string OperationKind, string Method, string RouteScope,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? RequestTargetId, string RequestFingerprint, string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] JsonElement? Outcome,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] TerminalResponse? Terminal);
public sealed record ResendContextResponse(string ContractVersion, string CandidateId, string DeploymentEpoch, int ActorUserId,
    string ServerUtc, long MaximumEligibilitySeconds, bool EligibilityEnabled);
public sealed record ResendReceiptResponse(string ContractVersion, string CandidateId, string DeploymentEpoch, int ActorUserId,
    string OperationId, string OperationKind, string Method, string RouteScope,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? RequestTargetId, string RequestFingerprint,
    string IssuedAtUtc, string EligibleUntilUtc, string Receipt);
public sealed record ResendEligibilityResponse(string ContractVersion, string CandidateId, string DeploymentEpoch, int ActorUserId,
    string OperationId, string OperationKind, string Method, string RouteScope,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? RequestTargetId, string RequestFingerprint,
    string Decision, string Reason, string ServerUtc, string EligibleUntilUtc);

internal static class OperationHttpMapper
{
    internal const string ResolutionVersion = "web5-resolution-v1";
    internal static string? Target(long? id) => id?.ToString(CultureInfo.InvariantCulture);
    internal static string Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        .ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    private static WebOperationException Unavailable() => new("Operation.Unavailable");

    internal static OperationStatusResponse Status(WebOperationStatus status, Guid epoch, Guid key, byte[] fingerprint)
    {
        var create = status.Kind is WebOperationKind.SalesCreate or WebOperationKind.ReceiptCreate;
        if (status.DeploymentEpoch != epoch || status.OperationId != key ||
            !status.RequestFingerprint.SequenceEqual(fingerprint) ||
            (create ? status.RequestTargetId.HasValue : !status.RequestTargetId.HasValue || status.RequestTargetId.Value <= 0))
            throw Unavailable();
        var state = status.State;
        JsonElement? outcome = null;
        if (state == "COMMITTED")
        {
            try
            {
                var stored = new V1OperationResultCodec().DecodeEnvelope(status.Kind, status.ResultBytes!);
                var sales = status.Kind is WebOperationKind.SalesCreate or WebOperationKind.SalesUpdate or WebOperationKind.SalesPost;
                var resultId = stored.GetProperty("data").GetProperty(sales ? "saleId" : "receiptId").GetString();
                if (!status.ResultDocumentId.HasValue || resultId != Target(status.ResultDocumentId) ||
                    (!create && status.ResultDocumentId != status.RequestTargetId))
                    throw new WebOperationException("Operation.ResultUnavailable");
                outcome = stored;
            }
            catch (WebOperationException error) when (error.Code == "Operation.ResultUnavailable")
            {
                state = "RESULT_UNAVAILABLE";
                outcome = null;
            }
        }
        else if (state is not ("RESULT_EXPIRED" or "RESULT_UNAVAILABLE")) throw Unavailable();
        var description = V1OperationCanonicalizer.Describe(status.Kind);
        return new(epoch.ToString("D"), key.ToString("D"), WebOperationWireInput.KindName(status.Kind),
            description.Method, description.Route, Target(status.RequestTargetId),
            Convert.ToHexString(status.RequestFingerprint).ToLowerInvariant(), state, outcome);
    }

    internal static ResolutionResponse Resolution(WebOperationResolution resolution, int actor, Guid epoch, Guid key, byte[] fingerprint)
    {
        if (actor <= 0 || resolution == null) throw Unavailable();
        var status = resolution.Status;
        var negative = status.State == "NOT_EXECUTED";
        var indeterminate = status.State == "INDETERMINATE";
        if ((negative || indeterminate) && (status.ResultBytes != null || status.ResultDocumentId != null)) throw Unavailable();
        if (negative != (resolution.Terminal != null)) throw Unavailable();
        var original = negative || indeterminate ? new WebOperationStatus(status.OperationId, status.DeploymentEpoch, status.Kind,
            status.RequestTargetId, status.RequestFingerprint, null, status.CompletedAtUtc, "RESULT_UNAVAILABLE", null) : status;
        var result = Status(original, epoch, key, fingerprint);
        TerminalResponse? terminal = null;
        if (resolution.Terminal is WebOperationTerminalDecision decision)
        {
            if (decision.DecidedAtUtc.Kind != DateTimeKind.Utc || decision.DecidedAtUtc != status.CompletedAtUtc ||
                decision.CaseId == null || !Regex.IsMatch(decision.CaseId, @"\A[A-Za-z0-9_-]{1,128}\z", RegexOptions.CultureInvariant) ||
                !Regex.IsMatch(decision.DecisionId.ToString("D"), @"\A[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\z"))
                throw Unavailable();
            terminal = new(decision.DecisionId.ToString("D"), decision.CaseId, Utc(decision.DecidedAtUtc));
        }
        return new(ResolutionVersion, actor, result.DeploymentEpoch, result.OperationId, result.OperationKind,
            result.Method, result.RouteScope, result.RequestTargetId, result.RequestFingerprint,
            negative || indeterminate ? status.State : result.State, result.Outcome, terminal);
    }

    internal static ResendContextResponse ResendContext(WebHostOptions options, Guid epoch, int actor, DateTime now)
    {
        if (!options.Web6ResendEligibilityEnabled || options.Web6CandidateId == null || epoch == Guid.Empty || actor <= 0)
            throw Unavailable();
        return new(Web6ResendReceiptCodec.ContractVersion, options.Web6CandidateId, epoch.ToString("D"), actor,
            Utc(now), Web6ResendReceiptCodec.LifetimeSeconds, true);
    }

    internal static ResendReceiptResponse Receipt(Web6ResendIdentity identity, IssuedWeb6ResendReceipt receipt) =>
        new(Web6ResendReceiptCodec.ContractVersion, identity.CandidateId, identity.DeploymentEpoch.ToString("D"), identity.ActorUserId,
            identity.OperationId.ToString("D"), identity.OperationKind, identity.Method, identity.RouteScope,
            Target(identity.RequestTargetId), identity.FingerprintHex, Utc(receipt.IssuedAtUtc), Utc(receipt.EligibleUntilUtc), receipt.Token);

    internal static ResendEligibilityResponse Eligibility(Web6ResendIdentity identity, ValidatedWeb6ResendReceipt receipt,
        WebOperationResolution? resolution, DateTime now)
    {
        string decision, reason;
        if (receipt.IsExpired || now >= receipt.EligibleUntilUtc)
            (decision, reason) = ("DENY", "RECEIPT_EXPIRED");
        else if (resolution == null)
            (decision, reason) = ("ALLOW", "ORIGINAL_NOT_OBSERVED_RECEIPT_VALID");
        else
        {
            var status = resolution.Status;
            if (status.OperationId != identity.OperationId || status.DeploymentEpoch != identity.DeploymentEpoch ||
                status.Kind != identity.Kind || status.RequestTargetId != identity.RequestTargetId ||
                !status.RequestFingerprint.SequenceEqual(identity.Fingerprint)) throw Unavailable();
            (decision, reason) = status.State switch
            {
                "COMMITTED" when status.ResultDocumentId.HasValue && status.ResultBytes is { Length: > 0 } && resolution.Terminal == null
                    => ("DENY", "COMMITTED_RESULT_AVAILABLE"),
                "RESULT_UNAVAILABLE" when status.ResultBytes == null && resolution.Terminal == null
                    => ("DENY", "COMMITTED_RESULT_UNAVAILABLE"),
                "RESULT_EXPIRED" when status.ResultBytes == null && resolution.Terminal == null => ("DENY", "RESULT_EXPIRED"),
                "NOT_EXECUTED" when status.ResultBytes == null && !status.ResultDocumentId.HasValue && resolution.Terminal != null
                    => ("DENY", "NOT_EXECUTED"),
                "INDETERMINATE" when status.ResultBytes == null && !status.ResultDocumentId.HasValue && resolution.Terminal == null
                    => ("INDETERMINATE", "TERMINAL_AUDIT_INCOMPLETE"),
                _ => throw Unavailable()
            };
        }
        return new(Web6ResendReceiptCodec.ContractVersion, identity.CandidateId, identity.DeploymentEpoch.ToString("D"),
            identity.ActorUserId, identity.OperationId.ToString("D"), identity.OperationKind, identity.Method, identity.RouteScope,
            Target(identity.RequestTargetId), identity.FingerprintHex, decision, reason, Utc(now), Utc(receipt.EligibleUntilUtc));
    }
}
