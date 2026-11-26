using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using MyDmsVn.Server.Application.Identity;
using MyDmsVn.Server.Application.Operations;
using MyDmsVn.Server.HttpHost;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps10BridgeRemovalTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly Guid Session = Guid.Parse("a1000000-0000-4000-8000-000000000001");
    private static readonly FrozenClock Clock = new();
    private static WebHostOptions Options() => new("https://example.test", "https://example.test",
        "mydmsvn", "key01", new Dictionary<string, byte[]> { ["key01"] = Key },
        web5Enabled: true, web6ResendEligibilityEnabled: true, web6CandidateId: "candidate-1");

    [Fact]
    public void Production_assembly_has_no_legacy_facade_or_wire_implementation()
    {
        var assembly = typeof(NativeEndpointRoutes).Assembly;
        var retired = new[] { "CompatWebApi", "WireDom", "WireToken", "WireTokenType",
            "WireObject", "WireArray", "WireValue", "WireProperty", "Net10RouteTable",
            "Net10WireJson", "HttpJson", "ApiHttpResponseMessage", "CoreRequestContextExtension",
            "StrictWireJson", "WebOperationWireInput", "WebOperationResultCodec", "WireApiController" };
        Assert.DoesNotContain(assembly.GetTypes(), type =>
            (type.Namespace?.StartsWith("System.Web.Http", StringComparison.Ordinal) ?? false) ||
            (type.Namespace?.StartsWith("Microsoft.Owin", StringComparison.Ordinal) ?? false) ||
            retired.Contains(type.Name, StringComparer.Ordinal));
        Assert.NotEqual(assembly, typeof(Net10WireJson).Assembly);
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
            reference.Name is "Newtonsoft.Json" or "System.Web" or "System.Web.Http" or "Microsoft.Owin");
    }

    [Fact]
    public async Task Native_boundary_error_preserves_frozen_wire_and_http_metadata()
    {
        var context = new DefaultHttpContext();
        using var output = new MemoryStream();
        context.Response.Body = output;
        await NativeHttpErrors.WriteAsync(context, 405, "Request.Invalid", "The request is invalid.", "bridge_01");
        using var historical = HttpJson.Error(405, "Request.Invalid", "The request is invalid.", "bridge_01");
        var expected = await historical.Content.ReadAsByteArrayAsync();
        Assert.Equal(expected, output.ToArray());
        Assert.Equal(405, context.Response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", context.Response.ContentType);
        Assert.Equal((long)expected.Length, context.Response.ContentLength);
    }

    private static string Header => """{"alg":"HS256","kid":"key01","typ":"JWT"}""";

    private static string Payload()
    {
        var now = new DateTimeOffset(Clock.UtcNow).ToUnixTimeSeconds();
        return JsonSerializer.Serialize(new
        {
            iss = "https://example.test", aud = "mydmsvn", sub = "7",
            sid = Session.ToString("D"), jti = "a1000000-0000-4000-8000-000000000002",
            iat = now, nbf = now, exp = now + 300
        });
    }

    private static string Segment(string value) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(value));
    private static string Sign(string header, string payload, byte[] key)
    {
        var content = Segment(header) + "." + Segment(payload);
        return content + "." + Base64UrlEncoder.Encode(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(content)));
    }

    [Fact]
    public void Valid_signed_bearer_retains_exact_identity()
    {
        var result = new BearerTokenValidator(Options(), Clock).Validate(Sign(Header, Payload(), Key));
        Assert.NotNull(result);
        Assert.Equal(7, result.UserId);
        Assert.Equal(Session, result.SessionId);
    }

    [Theory]
    [InlineData("sub-number")]
    [InlineData("duplicate")]
    [InlineData("escaped-duplicate")]
    [InlineData("fractional-exp")]
    [InlineData("missing-session")]
    [InlineData("aud-array")]
    [InlineData("header-type")]
    public void Correctly_signed_but_malformed_claims_fail_closed(string mutation)
    {
        var header = Header;
        var payload = Payload();
        var expiry = (new DateTimeOffset(Clock.UtcNow).ToUnixTimeSeconds() + 300).ToString(CultureInfo.InvariantCulture);
        switch (mutation)
        {
            case "sub-number": payload = payload.Replace("\"sub\":\"7\"", "\"sub\":7"); break;
            case "duplicate": payload = payload.Insert(payload.Length - 1, ",\"sub\":\"7\""); break;
            case "escaped-duplicate": payload = payload.Insert(payload.Length - 1, ",\"\\u0073ub\":\"7\""); break;
            case "fractional-exp": payload = payload.Replace("\"exp\":" + expiry, "\"exp\":" + expiry + ".0"); break;
            case "missing-session": payload = payload.Replace("\"sid\":\"" + Session.ToString("D") + "\",", ""); break;
            case "aud-array": payload = payload.Replace("\"aud\":\"mydmsvn\"", "\"aud\":[\"mydmsvn\"]"); break;
            case "header-type": header = header.Replace("\"typ\":\"JWT\"", "\"typ\":42"); break;
            default: throw new InvalidOperationException(mutation);
        }
        Assert.Null(new BearerTokenValidator(Options(), Clock).Validate(Sign(header, payload, Key)));
    }

    [Fact]
    public void Web6_signed_receipt_retains_canonical_identity_and_rejects_ambiguity()
    {
        var codec = new Web6ResendReceiptCodec(Options(), Clock);
        var identity = new Web6ResendIdentity(7, "candidate-1", Session,
            Guid.Parse("a1000000-0000-4000-8000-000000000003"),
            WebOperationKind.SalesCreate, null, new byte[32]);
        var issued = codec.Issue(identity);
        var result = codec.Validate(issued.Token, identity);
        Assert.False(result.IsExpired);
        Assert.Equal(issued.IssuedAtUtc, result.IssuedAtUtc);
        Assert.Equal(issued.EligibleUntilUtc, result.EligibleUntilUtc);
        Assert.Equal(TimeSpan.FromDays(7), result.EligibleUntilUtc - result.IssuedAtUtc);

        var parts = issued.Token.Split('.');
        var header = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[0]));
        var payload = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[1]));
        var derivedKey = HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes("MyDmsVn.Web6.ResendEligibility.SigningKey.v1"));
        var duplicate = payload.Insert(payload.Length - 1, ",\"\\u0073ub\":\"7\"");
        Assert.Throws<V1BindingException>(() => codec.Validate(Sign(header, duplicate, derivedKey), identity));
        Assert.Throws<V1BindingException>(() => codec.Validate(Sign(header, payload, Key), identity));
    }

    private sealed class FrozenClock : IUtcClock
    {
        public DateTime UtcNow => new(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc);
    }
}
