using System;
using System.IO;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03AcceptanceOptions
{
    private Vps03AcceptanceOptions(string sqlConnectionString, string origin, string tlsThumbprint,
        string desktopPackage, string hostPackage, string headSha, string resultsDirectory)
    {
        SqlConnectionString = sqlConnectionString;
        Origin = origin;
        TlsThumbprint = tlsThumbprint;
        DesktopPackage = desktopPackage;
        HostPackage = hostPackage;
        HeadSha = headSha;
        ResultsDirectory = resultsDirectory;
    }

    internal string SqlConnectionString { get; }
    internal string Origin { get; }
    internal string TlsThumbprint { get; }
    internal string DesktopPackage { get; }
    internal string HostPackage { get; }
    internal string HeadSha { get; }
    internal string ResultsDirectory { get; }

    internal static Vps03AcceptanceOptions FromEnvironment(Func<string, string?> read)
    {
        if (!string.Equals(read("MYDMSVN_VPS03_ACCEPTANCE"), "approved", StringComparison.Ordinal))
            throw new InvalidDataException("Explicit VPS-03 operator approval is required.");
        return new Vps03AcceptanceOptions(
            Required(read, "MYDMSVN_TEST_SQLSERVER_CONNECTION_STRING"),
            Required(read, "MYDMSVN_VPS03_API_ORIGIN"),
            Required(read, "MYDMSVN_WEB_TEST_TLS_THUMBPRINT"),
            Required(read, "MYDMSVN_VPS03_DESKTOP_PACKAGE"),
            Required(read, "MYDMSVN_VPS03_HOST_PACKAGE"),
            Required(read, "MYDMSVN_VPS03_HEAD_SHA"),
            Required(read, "MYDMSVN_VPS03_RESULTS_DIRECTORY"));
    }

    private static string Required(Func<string, string?> read, string name) =>
        string.IsNullOrWhiteSpace(read(name))
            ? throw new InvalidDataException("Required VPS-03 environment value is absent: " + name)
            : read(name)!;
}
