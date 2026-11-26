using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using MyDmsVn.Contracts;
using MyDmsVn.Server.Application.Operations;
using MyDmsVn.Server.HttpHost;
using MyDmsVn.Server.HttpHost.Features.Sales;
using Xunit;
using LiveFixture = MyDmsVn.Server.AspNetCoreHost.Tests.Vps05cDesktopRouteLiveParityTests.DesktopLiveFixture;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps10CanonicalOperationTests
{
    [Theory]
    [InlineData("vi-VN")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("")]
    public void Every_frozen_Sales_vector_preserves_exact_canonical_UTF8_and_hash(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            using var corpus = Read("web4-v1/fingerprint-vectors.json");
            var vectors = corpus.RootElement.GetProperty("vectors").EnumerateArray()
                .Where(v => v.GetProperty("intent").GetProperty("operationKind").GetString()!.StartsWith("Sales.", StringComparison.Ordinal)).ToArray();
            Assert.Equal(20, vectors.Length);
            foreach (var vector in vectors)
            {
                var intent = vector.GetProperty("intent");
                var kind = Kind(intent.GetProperty("operationKind").GetString()!);
                var target = intent.GetProperty("requestTargetId");
                long? id = target.ValueKind == JsonValueKind.Null ? null : long.Parse(target.GetString()!, CultureInfo.InvariantCulture);
                var source = intent.GetProperty("body");
                var before = source.GetRawText();
                _ = SalesRequestReader.Read(kind switch
                {
                    WebOperationKind.SalesCreate => typeof(SalesCreateRequest),
                    WebOperationKind.SalesUpdate => typeof(SalesUpdateRequest),
                    _ => typeof(SalesPostRequest)
                }, source);
                var canonical = V1OperationCanonicalizer.Canonicalize(kind, id, source);
                Assert.Equal(before, source.GetRawText());
                Assert.Equal(vector.GetProperty("canonical").GetString(), canonical);
                Assert.Equal(vector.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant());
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Theory]
    [InlineData("create", "POST", "/api/v1/sales-receipts", WebOperationKind.SalesCreate)]
    [InlineData("update", "PUT", "/api/v1/sales-receipts/9223372036854775807/draft", WebOperationKind.SalesUpdate)]
    [InlineData("post", "POST", "/api/v1/sales-receipts/9223372036854775807/post", WebOperationKind.SalesPost)]
    public async Task Frozen_pre_upgrade_outcome_decodes_and_native_live_response_equals_retained_codec(
        string action, string method, string path, WebOperationKind kind)
    {
        using var legacy = Read("web2-v1/sales-" + action + "-response.json");
        var bytes = Encoding.UTF8.GetBytes(legacy.RootElement.GetRawText());
        var codec = new WebOperationResultCodec();
        var options = new JsonSerializerOptions();
        V1HttpJson.Configure(options);
        byte[] retained;
        byte[] projected;
        if (kind == WebOperationKind.SalesPost)
        {
            var value = codec.Decode<PostSalesReceiptResponse>(kind, bytes);
            retained = codec.Encode(kind, value);
            projected = JsonSerializer.SerializeToUtf8Bytes(new V1SuccessEnvelope<SalesPostResponse>(SalesHttpMapper.Post(value)), options);
        }
        else
        {
            var value = codec.Decode<SalesReceiptDto>(kind, bytes);
            retained = codec.Encode(kind, value);
            projected = JsonSerializer.SerializeToUtf8Bytes(new V1SuccessEnvelope<SalesDetailResponse>(SalesHttpMapper.Detail(value)), options);
        }
        Assert.Equal(retained, projected);

        using var vectors = Read("web4-v1/fingerprint-vectors.json");
        var vector = vectors.RootElement.GetProperty("vectors").EnumerateArray()
            .Single(x => x.GetProperty("name").GetString() == "sales-" + action);
        await using var fixture = await LiveFixture.StartAsync();
        using var response = await fixture.SendAsync(method, path, vector.GetProperty("intent").GetProperty("body").GetRawText());
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal(retained, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Cancelled_request_never_constructs_operation_context()
    {
        var factory = new V1OperationContextFactory();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await factory.CreateAsync(new DefaultHttpContext(), WebOperationKind.SalesCreate, null, new CancellationToken(true));
        });
    }

    private static WebOperationKind Kind(string text) => text switch
    {
        "Sales.Create" => WebOperationKind.SalesCreate,
        "Sales.Update" => WebOperationKind.SalesUpdate,
        "Sales.Post" => WebOperationKind.SalesPost,
        _ => throw new InvalidOperationException("Unexpected frozen Sales vector.")
    };

    internal static JsonDocument Read(string relative)
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current != null; current = current.Parent)
        {
            var path = Path.Combine(current.FullName, "docs", "contracts", relative);
            if (File.Exists(path)) return JsonDocument.Parse(File.ReadAllText(path));
        }
        throw new FileNotFoundException("Frozen contract input not found: " + relative);
    }
}
