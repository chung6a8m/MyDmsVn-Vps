using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using MyDmsVn.Contracts.Security;
using Newtonsoft.Json.Linq;

namespace MyDmsVn.Server.HttpHost;

// Strict wire-boundary decoding. The application pipeline still owns semantic
// validation, authorization and optimistic revision checks.
internal static class DesktopParityWireInput
{
    public static async Task<JObject> Body(HttpRequestMessage request)
    {
        SalesWireInput.NoQuery(request);
        return (await StrictJsonRequest.Read(request, value => value).ConfigureAwait(false)).Value;
    }

    public static string QueryValue(HttpRequestMessage request, string name, int maximum)
    {
        var query = SalesWireInput.Query(request, new[] { name });
        if (!query.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value) || value.Length > maximum ||
            value.IndexOf('\0') >= 0)
            throw new WireInputException(name);
        return value;
    }

    public static SecurityAdministrationMutationRequest Mutation(JObject json)
    {
        Fields(json,
            new[] { "target", "operation", "targetId", "relatedId", "name", "displayName", "email",
                "isActive", "password", "permissionKey", "decision", "expectedRevision",
                "expectedRelatedRevision", "correlationId" },
            new[] { "target", "operation" });
        var target = (SecurityAdministrationTargetDto)EnumInteger(json, "target", 0, 1);
        var operation = (SecurityAdministrationOperationDto)EnumInteger(json, "operation", 0, 5);
        var correlationId = OptionalGuid(json, "correlationId");
        return new SecurityAdministrationMutationRequest(
            target, operation, OptionalInt(json, "targetId") ?? 0,
            OptionalInt(json, "relatedId") ?? 0,
            OptionalText(json, "name", 100), OptionalText(json, "displayName", 100),
            OptionalText(json, "email", 254), OptionalBool(json, "isActive"),
            OptionalText(json, "password", 72), OptionalText(json, "permissionKey", 100),
            OptionalBool(json, "decision"), OptionalLong(json, "expectedRevision"),
            OptionalLong(json, "expectedRelatedRevision"), correlationId);
    }

    public static (PermissionImportTargetTypeDto targetType, int targetId, string json) ImportPreview(JObject source)
    {
        Fields(source, new[] { "targetType", "targetId", "json" },
            new[] { "targetType", "targetId", "json" });
        return ((PermissionImportTargetTypeDto)EnumInteger(source, "targetType", 0, 1),
            RequiredInt(source, "targetId"), RequiredText(source, "json", PermissionAssignmentImportLimits.MaximumUtf8Bytes));
    }

    public static (PermissionImportTargetTypeDto targetType, int targetId, PermissionAssignmentImportRequest request) ImportApply(JObject source)
    {
        Fields(source, new[] { "targetType", "targetId", "json", "expectedTargetRevision", "expectedCatalogDigest" },
            new[] { "targetType", "targetId", "json", "expectedTargetRevision", "expectedCatalogDigest" });
        return ((PermissionImportTargetTypeDto)EnumInteger(source, "targetType", 0, 1),
            RequiredInt(source, "targetId"),
            new PermissionAssignmentImportRequest(
                RequiredText(source, "json", PermissionAssignmentImportLimits.MaximumUtf8Bytes),
                RequiredLong(source, "expectedTargetRevision"),
                RequiredText(source, "expectedCatalogDigest", 256)));
    }

    private static void Fields(JObject source, string[] allowed, string[] required)
    {
        if (source.Properties().Any(p => !allowed.Contains(p.Name, StringComparer.Ordinal)))
            throw new WireInputException();
        foreach (var key in required)
            if (!source.ContainsKey(key)) throw new WireInputException(key);
    }
    private static JToken? Optional(JObject source, string key) =>
        source.TryGetValue(key, StringComparison.Ordinal, out var value) && value.Type != JTokenType.Null ? value : null;
    private static int EnumInteger(JObject source, string key, int min, int max)
    {
        var raw = Optional(source, key);
        if (raw?.Type != JTokenType.Integer ||
            !int.TryParse(raw.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var result) ||
            result < min || result > max)
            throw new WireInputException(key);
        return result;
    }
    private static int RequiredInt(JObject source, string key) => OptionalInt(source, key) ?? throw new WireInputException(key);
    private static int? OptionalInt(JObject source, string key)
    {
        var raw = Optional(source, key);
        if (raw == null) return null;
        if (raw.Type != JTokenType.Integer || !int.TryParse(raw.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new WireInputException(key);
        return value;
    }
    private static long RequiredLong(JObject source, string key) => OptionalLong(source, key) ?? throw new WireInputException(key);
    private static long? OptionalLong(JObject source, string key)
    {
        var raw = Optional(source, key);
        if (raw == null) return null;
        if (raw.Type != JTokenType.Integer || !long.TryParse(raw.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new WireInputException(key);
        return value;
    }
    private static bool? OptionalBool(JObject source, string key)
    {
        var raw = Optional(source, key);
        if (raw == null) return null;
        if (raw.Type != JTokenType.Boolean) throw new WireInputException(key);
        return (bool)raw;
    }
    private static string RequiredText(JObject source, string key, int max) =>
        OptionalText(source, key, max) ?? throw new WireInputException(key);
    private static string? OptionalText(JObject source, string key, int max)
    {
        var raw = Optional(source, key);
        if (raw == null) return null;
        if (raw.Type != JTokenType.String) throw new WireInputException(key);
        var result = (string)raw!;
        if (result.Length > max || result.IndexOf('\0') >= 0) throw new WireInputException(key);
        return result;
    }
    private static Guid? OptionalGuid(JObject source, string key)
    {
        var value = OptionalText(source, key, 36);
        if (value == null) return null;
        if (!Guid.TryParseExact(value, "D", out var guid) || guid == Guid.Empty ||
            !string.Equals(value, guid.ToString("D"), StringComparison.Ordinal))
            throw new WireInputException(key);
        return guid;
    }
}
