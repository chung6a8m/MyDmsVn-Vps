using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyDmsVn.Contracts.Security;
using MyDmsVn.Server.Application.Security;

namespace MyDmsVn.Server.HttpHost.Features.Security;

// Numeric revisions are JSON integer tokens, not the text IDs used by WEB2/3.
// No actor/current-user property is accepted. CorrelationId is historical
// mutation audit metadata, not an authentication or authorization assertion.
public sealed record SecurityMutationBody(
    [property: JsonRequired] int Target,
    [property: JsonRequired] int Operation,
    int? TargetId,
    int? RelatedId,
    string? Name,
    string? DisplayName,
    string? Email,
    bool? IsActive,
    string? Password,
    string? PermissionKey,
    bool? Decision,
    long? ExpectedRevision,
    long? ExpectedRelatedRevision,
    Guid? CorrelationId)
{
    internal MutateSecurityAdministrationCommand ToCommand() => new(
        (SecurityAdministrationTarget)Target, (SecurityAdministrationOperation)Operation,
        TargetId ?? 0, RelatedId ?? 0, Name, DisplayName, Email, IsActive, Password, PermissionKey,
        Decision, ExpectedRevision, ExpectedRelatedRevision, CorrelationId);

    // Never include the password or other user-supplied values in diagnostics.
    public override string ToString() => nameof(SecurityMutationBody);
}

public sealed record PermissionImportPreviewBody(
    [property: JsonRequired] int TargetType,
    [property: JsonRequired] int TargetId,
    [property: JsonRequired] string Json)
{
    internal PreviewPermissionAssignmentImportQuery ToQuery() =>
        new((PermissionImportTargetTypeDto)TargetType, TargetId, Json);
}

public sealed record PermissionImportApplyBody(
    [property: JsonRequired] int TargetType,
    [property: JsonRequired] int TargetId,
    [property: JsonRequired] string Json,
    [property: JsonRequired] long ExpectedTargetRevision,
    [property: JsonRequired] string ExpectedCatalogDigest)
{
    internal ApplyPermissionAssignmentImportCommand ToCommand() =>
        new((PermissionImportTargetTypeDto)TargetType, TargetId, Json,
            ExpectedTargetRevision, ExpectedCatalogDigest);
}

internal static class SecurityRequestReader
{
    internal static bool Supports(Type type) =>
        type == typeof(SecurityMutationBody) || type == typeof(PermissionImportPreviewBody) ||
        type == typeof(PermissionImportApplyBody);

    internal static object Read(Type type, JsonElement body)
    {
        if (type == typeof(SecurityMutationBody))
        {
            Fields(body, new[] { "target", "operation", "targetId", "relatedId", "name", "displayName", "email",
                "isActive", "password", "permissionKey", "decision", "expectedRevision",
                "expectedRelatedRevision", "correlationId" }, new[] { "target", "operation" });
            var target = EnumInteger(body, "target", 1);
            var operation = EnumInteger(body, "operation", 5);
            var correlationId = OptionalGuid(body, "correlationId");
            return new SecurityMutationBody(target, operation, OptionalInt(body, "targetId"),
                OptionalInt(body, "relatedId"), OptionalText(body, "name", 100),
                OptionalText(body, "displayName", 100), OptionalText(body, "email", 254),
                OptionalBool(body, "isActive"), OptionalText(body, "password", 72),
                OptionalText(body, "permissionKey", 100), OptionalBool(body, "decision"),
                OptionalLong(body, "expectedRevision"), OptionalLong(body, "expectedRelatedRevision"), correlationId);
        }
        if (type == typeof(PermissionImportPreviewBody))
        {
            var fields = new[] { "targetType", "targetId", "json" };
            Fields(body, fields, fields);
            return new PermissionImportPreviewBody(EnumInteger(body, "targetType", 1),
                RequiredInt(body, "targetId"), RequiredText(body, "json", PermissionAssignmentImportLimits.MaximumUtf8Bytes));
        }
        if (type == typeof(PermissionImportApplyBody))
        {
            var fields = new[] { "targetType", "targetId", "json", "expectedTargetRevision", "expectedCatalogDigest" };
            Fields(body, fields, fields);
            return new PermissionImportApplyBody(EnumInteger(body, "targetType", 1),
                RequiredInt(body, "targetId"), RequiredText(body, "json", PermissionAssignmentImportLimits.MaximumUtf8Bytes),
                OptionalLong(body, "expectedTargetRevision") ?? throw new V1BindingException("expectedTargetRevision"),
                RequiredText(body, "expectedCatalogDigest", 256));
        }
        throw new InvalidOperationException("Unsupported Security input.");
    }

    private static void Fields(JsonElement body, string[] allowed, string[] required)
    {
        if (body.ValueKind != JsonValueKind.Object ||
            body.EnumerateObject().Any(p => !allowed.Contains(p.Name, StringComparer.Ordinal)))
            throw new V1BindingException();
        foreach (var name in required)
            if (!body.TryGetProperty(name, out _)) throw new V1BindingException(name);
    }

    private static JsonElement? Optional(JsonElement body, string field) =>
        body.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;

    private static int EnumInteger(JsonElement body, string field, int max)
    {
        var value = RequiredInt(body, field);
        return value <= max ? value : throw new V1BindingException(field);
    }

    private static int RequiredInt(JsonElement body, string field) =>
        OptionalInt(body, field) ?? throw new V1BindingException(field);

    private static int? OptionalInt(JsonElement body, string field)
    {
        if (Optional(body, field) is not JsonElement raw) return null;
        if (raw.ValueKind != JsonValueKind.Number ||
            !int.TryParse(raw.GetRawText(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new V1BindingException(field);
        return value;
    }

    private static long? OptionalLong(JsonElement body, string field)
    {
        if (Optional(body, field) is not JsonElement raw) return null;
        if (raw.ValueKind != JsonValueKind.Number ||
            !long.TryParse(raw.GetRawText(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new V1BindingException(field);
        return value;
    }

    private static bool? OptionalBool(JsonElement body, string field)
    {
        if (Optional(body, field) is not JsonElement raw) return null;
        if (raw.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new V1BindingException(field);
        return raw.GetBoolean();
    }

    private static string RequiredText(JsonElement body, string field, int maximum) =>
        OptionalText(body, field, maximum) ?? throw new V1BindingException(field);

    private static string? OptionalText(JsonElement body, string field, int maximum)
    {
        if (Optional(body, field) is not JsonElement raw) return null;
        if (raw.ValueKind != JsonValueKind.String) throw new V1BindingException(field);
        var value = raw.GetString()!;
        V1QueryPolicy.ValidateText(value, field, maximum);
        return value;
    }

    private static Guid? OptionalGuid(JsonElement body, string field)
    {
        var value = OptionalText(body, field, 36);
        if (value == null) return null;
        if (!Guid.TryParseExact(value, "D", out var guid) || guid == Guid.Empty ||
            !string.Equals(value, guid.ToString("D"), StringComparison.Ordinal))
            throw new V1BindingException(field);
        return guid;
    }
}
