using System;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using Microsoft.IdentityModel.Tokens;
using MyDmsVn.Server.Application.Identity;
using System.Text.Json;

namespace MyDmsVn.Server.HttpHost;

public sealed class BearerTokenValidator
{
    private readonly WebHostOptions _options;
    private readonly IUtcClock _clock;
    public BearerTokenValidator(WebHostOptions options, IUtcClock clock) { _options = options; _clock = clock; }

    public ValidatedBearer? Validate(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 8192) return null;
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3 || parts.Any(p => p.Length == 0 || p.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))) return null;
            var header = SignedTokenJson.ReadObject(parts[0]);
            var payload = SignedTokenJson.ReadObject(parts[1]);
            if (SignedTokenJson.Text(SignedTokenJson.Member(header, "alg")) != SecurityAlgorithms.HmacSha256 || SignedTokenJson.Member(header, "kid").ValueKind != JsonValueKind.String ||
                header.EnumerateObject().Any(p => p.Name != "alg" && p.Name != "kid" && p.Name != "typ") ||
                (SignedTokenJson.Member(header, "typ").ValueKind != JsonValueKind.Undefined && SignedTokenJson.Text(SignedTokenJson.Member(header, "typ")) != "JWT")) return null;
            var keyId = SignedTokenJson.Text(SignedTokenJson.Member(header, "kid"))!;
            var key = _options.FindKey(keyId);
            if (key == null || SignedTokenJson.Member(payload, "iss").ValueKind != JsonValueKind.String || SignedTokenJson.Member(payload, "aud").ValueKind != JsonValueKind.String ||
                SignedTokenJson.Text(SignedTokenJson.Member(payload, "iss")) != _options.Issuer || SignedTokenJson.Text(SignedTokenJson.Member(payload, "aud")) != _options.Audience ||
                SignedTokenJson.Member(payload, "sub").ValueKind != JsonValueKind.String || SignedTokenJson.Member(payload, "sid").ValueKind != JsonValueKind.String || SignedTokenJson.Member(payload, "jti").ValueKind != JsonValueKind.String) return null;
            var sub = SignedTokenJson.Text(SignedTokenJson.Member(payload, "sub"))!;
            if (!int.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) || userId <= 0 ||
                sub != userId.ToString(CultureInfo.InvariantCulture) || !CanonicalGuid(SignedTokenJson.Text(SignedTokenJson.Member(payload, "sid"))!, out var sessionId) ||
                !CanonicalGuid(SignedTokenJson.Text(SignedTokenJson.Member(payload, "jti"))!, out _)) return null;
            if (!NumericDate(SignedTokenJson.Member(payload, "iat"), out var issued) || !NumericDate(SignedTokenJson.Member(payload, "nbf"), out var notBefore) ||
                !NumericDate(SignedTokenJson.Member(payload, "exp"), out var expiry)) return null;
            var now = new DateTimeOffset(DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc)).ToUnixTimeSeconds();
            var skew = _options.ClockSkewSeconds;
            if (issued > now + skew || notBefore > now + skew || expiry <= now - skew ||
                expiry <= issued || expiry <= notBefore || expiry - issued > _options.AccessLifetimeMinutes * 60 || notBefore < issued - skew) return null;
            var validator = new JwtSecurityTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 8192 };
            validator.ValidateToken(token, new TokenValidationParameters
            {
                RequireSignedTokens = true, ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(key) { KeyId = keyId },
                ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }, ValidateIssuer = true, ValidIssuer = _options.Issuer,
                ValidateAudience = true, ValidAudience = _options.Audience, RequireAudience = true, RequireExpirationTime = true,
                ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(skew),
                LifetimeValidator = (nbf, exp, _, _) => nbf.HasValue && exp.HasValue && notBefore <= now + skew && expiry > now - skew,
            }, out _);
            return new ValidatedBearer(userId, sessionId);
        }
        catch (Exception exception) when (exception is SecurityTokenException || exception is ArgumentException ||
            exception is JsonException || exception is FormatException || exception is OverflowException ||
            exception is InvalidOperationException)
        { return null; }
    }
    private static bool NumericDate(JsonElement value, out long result)
    {
        result = 0;
        return value.ValueKind == JsonValueKind.Number && long.TryParse(value.GetRawText(), NumberStyles.None, CultureInfo.InvariantCulture, out result) && result >= 0 && result <= 253402300799;
    }
    private static bool CanonicalGuid(string value, out Guid result) => Guid.TryParseExact(value, "D", out result) && result != Guid.Empty && value == result.ToString("D");
}
public sealed class ValidatedBearer
{
    internal ValidatedBearer(int userId, Guid sessionId) { UserId = userId; SessionId = sessionId; }
    public int UserId { get; }
    public Guid SessionId { get; }
}
