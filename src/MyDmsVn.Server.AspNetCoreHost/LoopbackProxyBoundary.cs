using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace MyDmsVn.Server.HttpHost;

// Dedicated, opt-in VPS-08 boundary; do not enable ASPNETCORE_FORWARDEDHEADERS_ENABLED.
// A loopback address alone does not authenticate Nginx (other local users may connect).
// The independent proxy credential is mandatory and overwritten by Nginx.
internal sealed record LoopbackProxySettings(string OriginAuthority, string ProxyToken);

internal static class LoopbackProxyBoundary
{
    internal const string LoopbackUrl = "http://127.0.0.1:5080";
    private const string Mode = "loopback-nginx-v1";
    private const string TokenHeader = "X-MyDmsVn-Proxy-Token";
    private static readonly Regex TokenPattern = new(
        @"\A[A-Za-z0-9_-]{32,128}\z", RegexOptions.CultureInvariant);

    internal static LoopbackProxySettings? ReadSettings(Func<string, string?> read, string publicOrigin)
    {
        // The framework shortcut clears the trust allowlist. Reject it even in direct-HTTPS mode.
        var shortcut = read("ASPNETCORE_FORWARDEDHEADERS_ENABLED");
        if (shortcut != null && shortcut != "false")
            throw new InvalidOperationException("Unsafe forwarded-header shortcut is forbidden.");

        var mode = read("MYDMSVN_VPS08_PROXY_MODE");
        if (mode == null) return null;
        if (mode != Mode || !OperatingSystem.IsLinux() ||
            read("ASPNETCORE_URLS") != LoopbackUrl)
            throw new InvalidOperationException("Invalid Linux proxy mode or Kestrel binding.");
        var token = read("MYDMSVN_VPS08_PROXY_TOKEN");
        if (token == null || !TokenPattern.IsMatch(token))
            throw new InvalidOperationException("Missing bounded proxy credential.");
        return new LoopbackProxySettings(new Uri(publicOrigin).Authority, token);
    }

    internal static void ConfigureForwardedHeaders(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.RequireHeaderSymmetry = true;
        // No wildcard or CIDR-based trust; Nginx and Kestrel share the same host.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownProxies.Add(IPAddress.Loopback);
    }

    internal static Task AdmitAsync(HttpContext context, Func<Task> next, LoopbackProxySettings settings)
    {
        var headers = context.Request.Headers;
        var valid = context.Request.Scheme == "http" &&
            context.Connection.RemoteIpAddress != null &&
            IPAddress.Loopback.Equals(context.Connection.RemoteIpAddress) &&
            IPAddress.Loopback.Equals(context.Connection.LocalIpAddress) &&
            context.Request.Host.Value == settings.OriginAuthority &&
            One(headers, "X-Forwarded-Proto") == "https" &&
            ValidClientIp(One(headers, "X-Forwarded-For")) &&
            ValidToken(One(headers, TokenHeader), settings.ProxyToken) &&
            !headers.Keys.Any(key =>
                key.Equals("Forwarded", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("X-Real-IP", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("X-Original-", StringComparison.OrdinalIgnoreCase) ||
                (key.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase) &&
                    !key.Equals("X-Forwarded-For", StringComparison.OrdinalIgnoreCase) &&
                    !key.Equals("X-Forwarded-Proto", StringComparison.OrdinalIgnoreCase)));

        if (!valid)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return Task.CompletedTask;
        }

        // Prevent the credential from reaching legacy controllers, audit records or logs.
        headers.Remove(TokenHeader);
        return next();
    }

    private static string? One(IHeaderDictionary headers, string name) =>
        headers.TryGetValue(name, out var values) && values.Count == 1 ? values[0] : null;

    private static bool ValidClientIp(string? value) =>
        value != null && value.Length <= 45 && !value.Contains('%') &&
        IPAddress.TryParse(value, out var ip) && ip != IPAddress.Any && ip != IPAddress.IPv6Any;

    private static bool ValidToken(string? supplied, string expected)
    {
        // Do not accept invalid Unicode or a second appended/duplicated credential.
        if (supplied == null || !TokenPattern.IsMatch(supplied)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(supplied), Encoding.ASCII.GetBytes(expected));
    }
}
