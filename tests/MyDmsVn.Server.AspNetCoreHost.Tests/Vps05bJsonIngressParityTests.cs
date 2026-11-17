using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MyDmsVn.Contracts;
using MyDmsVn.Server.Application.Sales;
using MyDmsVn.Server.Application.Security;
using MyDmsVn.Server.HttpHost;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps05bJsonIngressParityTests
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    [Theory]
    [InlineData("{\"value\":1,\"value\":2}")]
    [InlineData("{\"parent\":{\"child\":1,\"child\":2}}")]
    [InlineData("{\"value\":1,\"\\u0076alue\":2}")]
    [InlineData("{\"value\":1,}")]
    [InlineData("{\"value\": [1,]}")]
    [InlineData("{'value':1}")]
    [InlineData("{\"value\":1}{}")]
    [InlineData("/*comment*/{\"value\":1}")]
    [InlineData("{\"value\":\"\\uD800\"}")]
    [InlineData("{\"value\":\"\\uDC00\"}")]
    [InlineData("{\"value\":01}")]
    [InlineData("{\"value\":1e}")]
    public void Strict_ingress_rejects_extensions_ambiguous_members_and_invalid_unicode(string payload)
    {
        Assert.Throws<WireInputException>(() => StrictWireJson.Read(Utf8.GetBytes(payload)));
    }

    [Fact]
    public void Strict_ingress_rejects_invalid_raw_utf8_without_replacement()
    {
        Assert.Throws<WireInputException>(() => StrictWireJson.Read(new byte[]
        {
            (byte)'{', (byte)'"', (byte)'x', (byte)'"', (byte)':',
            (byte)'"', 0xC3, 0x28, (byte)'"', (byte)'}'
        }));
    }

    [Fact]
    public void Strict_ingress_distinguishes_missing_null_and_large_integer_string()
    {
        const string raw = "{\"receiptId\":\"9007199254740993\",\"note\":null,\"active\":true,\"quantity\":\"1.2500\"}";
        var input = StrictWireJson.Read(Utf8.GetBytes(raw));
        Assert.False(input.ContainsKey("missing"));
        Assert.True(input.ContainsKey("note"));
        Assert.Equal(WireTokenType.Null, input["note"]!.Type);
        Assert.Equal(WireTokenType.String, input["receiptId"]!.Type);
        Assert.Equal("9007199254740993", (string)input["receiptId"]!);
        Assert.Equal("1.2500", (string)input["quantity"]!);
        Assert.True((bool)input["active"]!);
        Assert.Equal("{\"data\":" + raw + "}", Net10WireJson.SerializeEnvelope(ApiResponse<object>.Success(input)));
    }

    [Fact]
    public void STJ_object_projection_retains_WEB5_first_member_and_explicit_null()
    {
        var projection = WireObject.Parse(Utf8.GetBytes("{\"operationId\":\"9007199254740993\",\"outcome\":null}"));
        projection.AddFirst(new WireProperty("actorUserId", 7));
        projection.AddFirst(new WireProperty("contractVersion", "web5-resolution-v1"));
        Assert.Equal(
            "{\"data\":{\"contractVersion\":\"web5-resolution-v1\",\"actorUserId\":7,\"operationId\":\"9007199254740993\",\"outcome\":null}}",
            Net10WireJson.SerializeEnvelope(ApiResponse<object>.Success(projection)));
    }

    [Fact]
    public void STJ_canonicalizer_is_input_immutable_and_matches_all_pinned_WEB4_cases()
    {
        var file = Path.Combine(FindRoot(), "docs", "contracts", "web4-v1", "fingerprint-vectors.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(file));
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        Assert.True(vectors.Length >= 6);
        foreach (var vector in vectors)
        {
            var intent = vector.GetProperty("intent");
            var kind = WebOperationWireInput.ParseKind(intent.GetProperty("operationKind").GetString()!);
            var target = intent.GetProperty("requestTargetId");
            long? targetId = target.ValueKind == JsonValueKind.Null ? null :
                long.Parse(target.GetString()!, CultureInfo.InvariantCulture);
            var body = WireObject.Parse(Utf8.GetBytes(intent.GetProperty("body").GetRawText()));
            var original = body.Node!.ToJsonString();
            var canonical = WebOperationWireInput.Canonicalize(kind, targetId, body);
            Assert.Equal(original, body.Node!.ToJsonString());
            Assert.Equal(vector.GetProperty("canonical").GetString(), canonical);
            Assert.Equal(vector.GetProperty("sha256").GetString(),
                Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(canonical))).ToLowerInvariant());
        }
    }

    [Fact]
    public void Sales_cursor_reuses_exact_legacy_filter_hash_and_opaque_base64()
    {
        var request = new SalesReceiptListRequest(new DateTime(2026, 11, 17), null,
            1, 2, 3, "Draft", 4, "Đơn \"hàng\" — thử", 50);
        var cursor = SalesReceiptCursor.Parse(request);
        var encoded = cursor.Encode(9007199254740993L, false);
        var normalized = new JObject
        {
            ["FromDate"] = "2026-11-17",
            ["ToDate"] = JValue.CreateNull(),
            ["CustomerId"] = 1,
            ["WarehouseId"] = 2,
            ["EmployeeId"] = 3,
            ["Status"] = "Draft",
            ["ProductId"] = 4,
            ["Search"] = "Đơn \"hàng\" — thử",
            ["PageSize"] = 50
        };
        var hash = Convert.ToBase64String(SHA256.HashData(Utf8.GetBytes(normalized.ToString(Formatting.None))))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var legacy = new JObject
        {
            ["v"] = 1,
            ["order"] = "SaleIdDesc",
            ["direction"] = "next",
            ["anchor"] = 9007199254740993L,
            ["pageSize"] = 50,
            ["filterHash"] = hash
        };
        var expected = Convert.ToBase64String(Utf8.GetBytes(legacy.ToString(Formatting.None)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expected, encoded);
        var decoded = SalesReceiptCursor.Parse(new SalesReceiptListRequest(
            request.FromDate, request.ToDate, request.CustomerId, request.WarehouseId,
            request.EmployeeId, request.Status, request.ProductId, request.Search, 50, expected));
        Assert.Equal(9007199254740993L, decoded.Anchor);
    }

    [Fact]
    public void Permission_import_rejects_escaped_duplicate_names_and_accepts_known_schema()
    {
        var parser = new PermissionAssignmentImportParser(PermissionRegistryComposition.CreateDefault());
        const string valid = "{\"schemaVersion\":1,\"targetType\":\"User\",\"targetId\":7,\"entries\":[{\"permissionKey\":\"Security.Users.Manage\",\"decision\":\"Deny\"}]}";
        Assert.True(parser.Parse(valid).IsValid);
        Assert.False(parser.Parse(valid.Replace("\"targetType\"", "\"targetType\":\"User\",\"\\u0074argetType\"")).IsValid);
        Assert.False(parser.Parse(valid.Replace("\"decision\":\"Deny\"", "\"decision\":\"Deny\",\"decision\":\"Allow\"")).IsValid);
        Assert.False(parser.Parse(valid + "{}").IsValid);
        Assert.False(parser.Parse(valid.Replace("\"schemaVersion\":1", "\"schemaVersion\":1.0")).IsValid);
    }

    private static string FindRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current != null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "docs", "contracts", "web4-v1", "fingerprint-vectors.json")))
                return current.FullName;
        throw new InvalidOperationException("Frozen independent WEB4 vectors were not found.");
    }
}
