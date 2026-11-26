using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MyDmsVn.Server.HttpHost;
using MyDmsVn.Server.Application.Catalog;
using Xunit;
using LiveFixture = MyDmsVn.Server.AspNetCoreHost.Tests.Vps05cDesktopRouteLiveParityTests.DesktopLiveFixture;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

// Characterization only: expected responses come from the frozen net48 corpus
// or explicit legacy protocol rules. No production code or golden data changes.
public sealed class Vps10ContractCharacterizationTests
{
    private const string Lookup = "/api/v1/catalog/products/lookup";
    private const string Sales = "/api/v1/sales-receipts";
    private const string SalesPost = Sales + "/9223372036854775807/post";
    private const string ReceiptPost = "/api/v1/goods-receipts/9007199254740993/post";
    private const string ValidSales = """{"saleDate":"2024-02-29","customerId":101,"warehouseId":102,"employeeId":103,"note":null,"lines":[{"saleLineId":"0","productId":104,"qty":"1.2500","unitPrice":"2.0000","isGift":false}]}""";

    [Fact]
    public void Manifest_covers_every_frozen_and_compiled_route_once_with_exact_order_and_owner()
    {
        using var manifest = Read("vps-10-route-migration.json");
        using var old = Read("vps-01-route-inventory.json");
        using var added = Read("vps-02-route-additions.json");
        using var actual = JsonDocument.Parse(Net10RouteTable.ToInventoryJson());
        var rows = manifest.RootElement.GetProperty("routes").EnumerateArray().ToArray();
        var frozen = old.RootElement.GetProperty("endpoints").EnumerateArray()
            .Concat(added.RootElement.GetProperty("endpoints").EnumerateArray()).ToArray();
        var compiled = actual.RootElement.EnumerateArray().ToArray();
        var uniquePairs = rows.Select(Key).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(1, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(57, uniquePairs.Count);
        Assert.Equal(57, rows.Length);
        Assert.Equal(57, compiled.Length);
        Assert.Equal(57, frozen.Length);
        Assert.Equal(uniquePairs.OrderBy(x => x, StringComparer.Ordinal), frozen.Select(Key).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(uniquePairs.OrderBy(x => x, StringComparer.Ordinal), compiled.Select(Key).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(52, rows.Count(r => Text(r, "path").StartsWith("/api/v1/", StringComparison.Ordinal)));
        Assert.Equal(2, rows.Count(r => Text(r, "path").StartsWith("/api/v2/", StringComparison.Ordinal)));
        Assert.Equal(3, rows.Count(r => Text(r, "path").StartsWith("/api/v3/", StringComparison.Ordinal)));
        var expectedCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["VPS-10.2"] = 4, ["VPS-10.4A"] = 2, ["VPS-10.4B"] = 3,
            ["VPS-10.5A"] = 4, ["VPS-10.5B"] = 3, ["VPS-10.6A"] = 20,
            ["VPS-10.6B"] = 8, ["VPS-10.6C"] = 6, ["VPS-10.6D"] = 7
        };
        Assert.Equal(expectedCounts.Count, rows.Select(r => Text(r, "owningIncrement")).Distinct().Count());
        foreach (var group in rows.GroupBy(r => Text(r, "owningIncrement")))
            Assert.Equal(expectedCounts[group.Key], group.Count());

        foreach (var row in rows)
        {
            var key = Key(row);
            var source = Assert.Single(frozen, r => Key(r) == key);
            var endpoint = Assert.Single(compiled, r => Key(r) == key);
            Assert.Equal(key, Text(row, "id"));
            Assert.Equal(Text(source, "controller"), Text(row, "baselineController"));
            Assert.Equal(Text(source, "action"), Text(row, "baselineAction"));
            Assert.Equal(Text(source, "authentication"), Text(row, "authentication"));
            Assert.Equal(Text(endpoint, "controller"), Text(row, "baselineController"));
            Assert.Equal(Text(endpoint, "action"), Text(row, "baselineAction"));
            Assert.Equal(endpoint.GetProperty("order").GetInt32(), row.GetProperty("order").GetInt32());
            var native = Text(row, "owningIncrement") is "VPS-10.2" or "VPS-10.4A" or "VPS-10.4B" or "VPS-10.5A" or "VPS-10.5B" or "VPS-10.6A" or "VPS-10.6B";
            Assert.Equal(native ? "native" : "bridge", Text(row, "activeOwner"));
            Assert.Equal(Text(row, "activeOwner"), Text(endpoint, "activeOwner"));
            Assert.Equal(Text(row, "actualController"), Text(endpoint, "actualController"));
            Assert.Equal(Text(row, "actualAction"), Text(endpoint, "actualAction"));
            Assert.False(string.IsNullOrWhiteSpace(Text(row, "contractFamily")));
            Assert.Equal(source.TryGetProperty("desktopApiMode", out var mode) && mode.GetString() == "enabled",
                row.GetProperty("featureFlags").EnumerateArray().Any(flag => flag.GetString() == "DesktopApiEnabled:required"));
        }
    }

    [Fact]
    public void Manifest_pins_all_293_independent_variants_and_46_remote_Desktop_methods()
    {
        using var manifest = Read("vps-10-route-migration.json");
        using var corpus = Read("vps-05c-desktop-route-net48.json");
        using var desktop = Read("vps-02-desktop-method-matrix.json");
        var rows = manifest.RootElement.GetProperty("routes").EnumerateArray().ToArray();
        var observed = corpus.RootElement.EnumerateArray().ToArray();
        var methods = desktop.RootElement.GetProperty("methods").EnumerateArray().ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var variants = 0;
        var mappedMethods = 0;
        foreach (var row in rows)
        {
            var coverage = row.GetProperty("coverage");
            var caseId = Text(coverage, "rawHttpCaseId");
            Assert.True(ids.Add(caseId), "Duplicate HTTP case id: " + caseId);
            var expected = observed.Where(x => Text(x, "CaseId") == caseId).Select(x => Text(x, "Variant")).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Assert.NotEmpty(expected);
            Assert.Equal(expected.Length, expected.Distinct().Count());
            var required = row.GetProperty("requiredPositiveNegativeCases").EnumerateArray().Select(x => x.GetString()!).ToArray();
            Assert.Equal(expected, required);
            variants += required.Length;
            var expectedMethods = methods.Where(m => Key(m) == Key(row))
                .Select(m => Text(m, "interface") + "." + Text(m, "method")).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var listedMethods = coverage.GetProperty("desktopMethods").EnumerateArray().Select(x => x.GetString()!).ToArray();
            Assert.Equal(expectedMethods, listedMethods);
            mappedMethods += listedMethods.Length;
        }
        Assert.Equal(57, ids.Count);
        Assert.Equal(293, observed.Length);
        Assert.Equal(293, variants);
        Assert.Equal(46, mappedMethods);
        Assert.Equal(ids.OrderBy(x => x, StringComparer.Ordinal),
            observed.Select(o => Text(o, "CaseId")).Distinct().OrderBy(x => x, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("", "limit")]
    [InlineData("?limit=0", "limit")]
    [InlineData("?limit=201", "limit")]
    [InlineData("?limit=01", "limit")]
    [InlineData("?limit=+1", "limit")]
    [InlineData("?limit=25&%6cimit=25", null)]
    [InlineData("?Limit=25", null)]
    [InlineData("?limit=25&unknown=1", null)]
    [InlineData("?limit=25&search", null)]
    [InlineData("?limit=25&search=%C3%28", null)]
    public async Task Lookup_rejects_ambiguous_or_invalid_query_before_handler(string query, string? field)
    {
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("GET", Lookup + query, null);
        await AssertValidation(response, field);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Fact]
    public async Task Lookup_preserves_plus_and_percent_encoded_plus_and_limit_boundary()
    {
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("GET", Lookup + "?search=a+b%2Bc&limit=200", null);
        Vps05cDesktopRouteLiveParityTests.AssertMatchesNet48("VPS01-LOOKUP-PRODUCTS", "success",
            response, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, fixture.HandlerCalls);
        var query = Assert.IsType<LookupProductsQuery>(fixture.LastRequest);
        Assert.Equal("a b+c", query.Request.Search);
        Assert.Equal(200, query.Request.Limit);
    }

    [Fact]
    public async Task Lookup_GET_body_is_rejected_before_handler()
    {
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("GET", Lookup + "?limit=25", "{}");
        await AssertValidation(response, null);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData(",\"isGift\":false", "", "lines[0].isGift")]
    [InlineData("\"isGift\":false", "\"isGift\":null", "lines[0].isGift")]
    [InlineData("\"productId\":104", "\"productId\":\"104\"", "lines[0].productId")]
    [InlineData("\"note\":null", "\"note\":null,\"unknown\":0", null)]
    [InlineData("\"note\":null", "\"note\":null,\"note\":null", null)]
    [InlineData("\"note\":null", "\"note\":null,\"\\u006eote\":null", null)]
    [InlineData("\"qty\":\"1.2500\"", "\"qty\":\"1.25001\"", "lines[0].qty")]
    [InlineData("\"qty\":\"1.2500\"", "\"qty\":\"+1.25\"", "lines[0].qty")]
    [InlineData("\"qty\":\"1.2500\"", "\"qty\":\"-1.25\"", "lines[0].qty")]
    [InlineData("\"qty\":\"1.2500\"", "\"qty\":\"1,25\"", "lines[0].qty")]
    [InlineData("\"qty\":\"1.2500\"", "\"qty\":1.25", "lines[0].qty")]
    [InlineData("\"unitPrice\":\"2.0000\"", "\"unitPrice\":\"-2\"", "lines[0].unitPrice")]
    [InlineData("\"saleDate\":\"2024-02-29\"", "\"saleDate\":\"2024-2-29\"", "saleDate")]
    [InlineData("\"saleLineId\":\"0\"", "\"saleLineId\":0", "lines[0].saleLineId")]
    public async Task Sales_does_not_coerce_missing_null_duplicate_unknown_or_noncanonical_fields(
        string before, string after, string? field)
    {
        Assert.Contains(before, ValidSales);
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("POST", Sales, ValidSales.Replace(before, after, StringComparison.Ordinal));
        await AssertValidation(response, field);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Fact]
    public async Task Invalid_UTF8_and_unsupported_content_type_fail_before_handler()
    {
        await using var fixture = await LiveFixture.StartAsync();
        var raw = new byte[] { (byte)'{', (byte)'"', (byte)'x', (byte)'"', (byte)':', (byte)'"', 0xC3, 0x28, (byte)'"', (byte)'}' };
        using var badUtf8 = await fixture.SendRawAsync("POST", Sales, raw, "application/json; charset=utf-8");
        await AssertValidation(badUtf8, null);
        using var badMedia = await fixture.SendRawAsync("POST", Sales, Encoding.UTF8.GetBytes(ValidSales), "text/plain");
        await AssertValidation(badMedia, null);
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Theory]
    [InlineData(SalesPost, "VPS01-SALES-POST", "AAAAAAAA AAI=", true)]
    [InlineData(ReceiptPost, "VPS01-RECEIPT-POST", "AAAAAAAA AAI=", false)]
    [InlineData(ReceiptPost, "VPS01-RECEIPT-POST", "AAAAAAAAAAI=", true)]
    public async Task Sales_and_Receipt_keep_their_distinct_version_grammars(
        string path, string caseId, string version, bool accepted)
    {
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync("POST", path, "{\"expectedVersion\":\"" + version + "\"}");
        if (accepted)
        {
            Vps05cDesktopRouteLiveParityTests.AssertMatchesNet48(caseId, "success",
                response, await response.Content.ReadAsByteArrayAsync());
            Assert.Equal(1, fixture.HandlerCalls);
        }
        else
        {
            await AssertValidation(response, "expectedVersion");
            Assert.Equal(0, fixture.HandlerCalls);
        }
    }

    [Fact]
    public async Task Authentication_precedes_malformed_request_and_disabled_additive_route_precedes_binding()
    {
        await using var fixture = await LiveFixture.StartAsync(desktopApiEnabled: false);
        const string path = "/api/v1/catalog/products/1/active";
        using var unauthenticated = await fixture.SendAsync("PUT", path, "not-json", authenticated: false);
        Vps05cDesktopRouteLiveParityTests.AssertMatchesNet48("VPS02-PRODUCT-ACTIVE", "unauthenticated",
            unauthenticated, await unauthenticated.Content.ReadAsByteArrayAsync());
        using var disabled = await fixture.SendAsync("PUT", path, "not-json");
        Assert.Equal(404, (int)disabled.StatusCode);
        using var error = JsonDocument.Parse(await disabled.Content.ReadAsByteArrayAsync());
        Assert.Equal("NotFound", Text(error.RootElement.GetProperty("error"), "code"));
        Assert.Equal(0, fixture.HandlerCalls);
        using var lookup = await fixture.SendAsync("GET", Lookup + "?limit=25", null);
        Vps05cDesktopRouteLiveParityTests.AssertMatchesNet48("VPS01-LOOKUP-PRODUCTS", "success",
            lookup, await lookup.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, fixture.HandlerCalls);
        await using var enabledFixture = await LiveFixture.StartAsync();
        using var invalid = await enabledFixture.SendAsync("PUT", path, "not-json");
        await AssertValidation(invalid, null);
        Assert.Equal(0, enabledFixture.HandlerCalls);
    }

    [Fact]
    public async Task Invalid_body_precedes_application_permission_check()
    {
        await using var fixture = await LiveFixture.StartAsync(denyPermissions: true);
        using var response = await fixture.SendAsync("POST", Sales, "{}");
        await AssertValidation(response, "saleDate");
        Assert.Equal(0, fixture.HandlerCalls);
        Assert.Empty(fixture.PermissionChecks);
    }

    [Theory]
    [InlineData("vi-VN")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void Canonical_operation_bytes_and_hashes_match_frozen_WEB4_vectors_under_culture(string name)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            using var corpus = Read("web4-v1/fingerprint-vectors.json");
            var vectors = corpus.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
            Assert.True(vectors.Length >= 6);
            foreach (var vector in vectors)
            {
                var intent = vector.GetProperty("intent");
                var target = intent.GetProperty("requestTargetId");
                long? id = target.ValueKind == JsonValueKind.Null ? null : long.Parse(target.GetString()!, CultureInfo.InvariantCulture);
                var source = StrictWireJson.Read(Encoding.UTF8.GetBytes(intent.GetProperty("body").GetRawText()));
                var before = source.Node!.ToJsonString();
                var actual = WebOperationWireInput.Canonicalize(WebOperationWireInput.ParseKind(Text(intent, "operationKind")), id, source);
                Assert.Equal(before, source.Node!.ToJsonString());
                Assert.Equal(Text(vector, "canonical"), actual);
                Assert.Equal(Text(vector, "sha256"), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actual))).ToLowerInvariant());
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    private static async Task AssertValidation(HttpResponseMessage response, string? field)
    {
        Assert.Equal(400, (int)response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        using var body = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var error = body.RootElement.GetProperty("error");
        Assert.Equal("ValidationError", Text(error, "code"));
        Assert.Equal("The request is invalid.", Text(error, "message"));
        Assert.Equal("vps05c_live_28", Text(error, "correlationId"));
        Assert.Equal("vps05c_live_28", Assert.Single(response.Headers.GetValues("X-Correlation-ID")));
        var detail = Assert.Single(error.GetProperty("details").EnumerateArray());
        Assert.Equal("Validation.InvalidValue", Text(detail, "code"));
        Assert.Equal("The field or request format is invalid.", Text(detail, "message"));
        if (field == null) Assert.False(detail.TryGetProperty("field", out _));
        else Assert.Equal(field, Text(detail, "field"));
    }

    private static string Key(JsonElement row) => Text(row, "httpMethod") + " " + Text(row, "path");
    private static string Text(JsonElement row, string name) => row.GetProperty(name).GetString()!;

    private static JsonDocument Read(string relative)
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current != null; current = current.Parent)
        {
            var path = Path.Combine(current.FullName, "docs", "contracts", relative);
            if (File.Exists(path)) return JsonDocument.Parse(File.ReadAllText(path));
        }
        throw new FileNotFoundException("Frozen contract input not found: " + relative);
    }
}
