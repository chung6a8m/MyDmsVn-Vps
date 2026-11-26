using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using MyDmsVn.Server.Application.Identity;

namespace MyDmsVn.Server.HttpHost;

// An immutable typed snapshot. Do not use a record here: its synthesized
// ToString would expose the connection string and proxy credential.
internal sealed class HostDeploymentOptions
{
    internal HostDeploymentOptions(WebHostOptions web, WebAuthSessionOptions sessions,
        LoopbackProxySettings? proxy, string connectionString, string superuserUsername)
    {
        Web = web;
        Sessions = sessions;
        Proxy = proxy;
        ConnectionString = connectionString;
        SuperuserUsername = superuserUsername;
    }

    internal WebHostOptions Web { get; }
    internal WebAuthSessionOptions Sessions { get; }
    internal LoopbackProxySettings? Proxy { get; }
    internal string ConnectionString { get; }
    internal string SuperuserUsername { get; }
}

internal static class HostOptionsRegistration
{
    private static readonly string[] DeploymentKeys =
    [
        "MYDMSVN_WEB_ORIGIN",
        "MYDMSVN_WEB_JWT_ISSUER",
        "MYDMSVN_WEB_JWT_AUDIENCE",
        "MYDMSVN_WEB_JWT_ACTIVE_KID",
        "MYDMSVN_WEB_JWT_KEYS",
        "MYDMSVN_WEB_ACCESS_LIFETIME_MINUTES",
        "MYDMSVN_WEB_OPERATION_MODE",
        "MYDMSVN_WEB_DESKTOP_API",
        "MYDMSVN_WEB6_RESEND_ELIGIBILITY",
        "MYDMSVN_WEB6_CANDIDATE_ID",
        "MYDMSVN_WEB6_CANDIDATE_ROOT",
        "MYDMSVN_WEB_SECURITY_AUDIT_PATH",
        "MYDMSVN_WEB_REFRESH_IDLE_DAYS",
        "MYDMSVN_WEB_REFRESH_ABSOLUTE_DAYS",
        "MYDMSVN_WEB_SQL_CONNECTION_STRING",
        "MYDMSVN_WEB_SUPERUSER_USERNAME",
        "MYDMSVN_VPS08_PROXY_MODE",
        "MYDMSVN_VPS08_PROXY_TOKEN",
        "ASPNETCORE_FORWARDEDHEADERS_ENABLED",
        "ASPNETCORE_URLS"
    ];

    // Preserve the previous exact environment names, missing/empty semantics
    // and Linux case sensitivity. Do not let appsettings/CLI override secrets
    // or deployment flags through the general ASP.NET configuration providers.
    internal static IConfigurationRoot ReadEnvironment() =>
        Snapshot(Environment.GetEnvironmentVariable);

    internal static IConfigurationRoot Snapshot(Func<string, string?> readSetting)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var key in DeploymentKeys)
        {
            var value = readSetting(key);
            if (value != null)
                values.Add(key, value);
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    internal static HostDeploymentOptions Read(IConfiguration configuration)
    {
        string? ReadSetting(string key) => configuration[key];

        var web = WebHostOptions.Read(ReadSetting);
        var proxy = LoopbackProxyBoundary.ReadSettings(ReadSetting, web.PublicOrigin);
        var sessions = WebAuthSessionConfiguration.Read(ReadSetting);
        var connection = ReadSetting("MYDMSVN_WEB_SQL_CONNECTION_STRING");
        var superuser = ReadSetting("MYDMSVN_WEB_SUPERUSER_USERNAME");
        if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(superuser))
            throw new InvalidOperationException("Missing restricted web deployment settings.");
        if (proxy != null)
            LoopbackProxyBoundary.RequireSqlTls(connection);

        return new HostDeploymentOptions(web, sessions, proxy, connection, superuser);
    }

    // Called only after AddVps10HostServices successfully parses the same
    // immutable deployment snapshot. General Kestrel configuration is unchanged.
    internal static void ConfigureListener(WebApplicationBuilder builder, IConfiguration configuration)
    {
        if (configuration["MYDMSVN_VPS08_PROXY_MODE"] != null)
            builder.WebHost.UseUrls(LoopbackProxyBoundary.LoopbackUrl);
    }
}
