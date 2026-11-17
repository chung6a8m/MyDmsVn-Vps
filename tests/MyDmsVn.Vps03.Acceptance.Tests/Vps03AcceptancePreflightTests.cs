using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MyDmsVn.Vps03.Acceptance.Tests;

public sealed class Vps03AcceptancePreflightTests
{
    private const string Head = "0123456789abcdef0123456789abcdef01234567";
    private const string Thumbprint = "00112233445566778899AABBCCDDEEFF00112233";

    [Fact]
    public void Accepts_explicit_exact_head_x86_packages_and_canonical_https_origin()
    {
        using var fixture = PackageFixture.Create(Head, 0x014c);
        var values = fixture.Environment(Head, "https://localhost:44321", "00 11 22 33 44 55 66 77 88 99 aa bb cc dd ee ff 00 11 22 33");

        var options = Vps03AcceptanceOptions.FromEnvironment(name => values.TryGetValue(name, out var value) ? value : null);
        var result = Vps03AcceptancePreflight.Validate(options);

        Assert.Equal(new Uri("https://localhost:44321"), result.Origin);
        Assert.Equal(Head, result.HeadSha);
        Assert.Equal(Thumbprint, result.TlsThumbprint);
        Assert.Equal(32, result.DesktopBitness);
    }

    [Theory]
    [InlineData("http://localhost:44321")]
    [InlineData("https://localhost:44321/")]
    [InlineData("https://localhost:44321/path")]
    public void Rejects_noncanonical_or_non_https_origin(string origin)
    {
        using var fixture = PackageFixture.Create(Head, 0x014c);
        var values = fixture.Environment(Head, origin, Thumbprint);

        Assert.Throws<InvalidDataException>(() =>
            Vps03AcceptancePreflight.Validate(Vps03AcceptanceOptions.FromEnvironment(name =>
                values.TryGetValue(name, out var value) ? value : null)));
    }

    [Fact]
    public void Rejects_package_sha_or_architecture_mismatch()
    {
        using var wrongSha = PackageFixture.Create("1123456789abcdef0123456789abcdef01234567", 0x014c);
        var wrongShaValues = wrongSha.Environment(Head, "https://localhost:44321", Thumbprint);
        Assert.Throws<InvalidDataException>(() =>
            Vps03AcceptancePreflight.Validate(Vps03AcceptanceOptions.FromEnvironment(name =>
                wrongShaValues.TryGetValue(name, out var value) ? value : null)));

        using var wrongMachine = PackageFixture.Create(Head, 0x8664);
        var wrongMachineValues = wrongMachine.Environment(Head, "https://localhost:44321", Thumbprint);
        Assert.Throws<InvalidDataException>(() =>
            Vps03AcceptancePreflight.Validate(Vps03AcceptanceOptions.FromEnvironment(name =>
                wrongMachineValues.TryGetValue(name, out var value) ? value : null)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yes")]
    public void Rejects_missing_operator_approval(string? approval)
    {
        using var fixture = PackageFixture.Create(Head, 0x014c);
        var values = fixture.Environment(Head, "https://localhost:44321", Thumbprint);
        if (approval == null) values.Remove("MYDMSVN_VPS03_ACCEPTANCE");
        else values["MYDMSVN_VPS03_ACCEPTANCE"] = approval;

        Assert.Throws<InvalidDataException>(() =>
            Vps03AcceptanceOptions.FromEnvironment(name => values.TryGetValue(name, out var value) ? value : null));
    }

    private sealed class PackageFixture : IDisposable
    {
        private PackageFixture(string root)
        {
            Root = root;
            Desktop = Path.Combine(root, "desktop");
            Host = Path.Combine(root, "host");
            Results = Path.Combine(root, "results");
        }

        internal string Root { get; }
        internal string Desktop { get; }
        internal string Host { get; }
        internal string Results { get; }

        internal static PackageFixture Create(string packageHead, ushort machine)
        {
            var fixture = new PackageFixture(Path.Combine(Path.GetTempPath(), "vps03-preflight-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(fixture.Desktop);
            Directory.CreateDirectory(fixture.Host);
            Directory.CreateDirectory(fixture.Results);
            WritePe(Path.Combine(fixture.Desktop, "MyDmsVn.Desktop.App.exe"), machine);
            var provenance = new JObject { ["headSha"] = packageHead };
            File.WriteAllText(Path.Combine(fixture.Desktop, "vps03-package-provenance.json"), provenance.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(fixture.Host, "vps03-package-provenance.json"), provenance.ToString(), new UTF8Encoding(false));
            return fixture;
        }

        internal Dictionary<string, string?> Environment(string head, string origin, string thumbprint) =>
            new(StringComparer.Ordinal)
            {
                ["MYDMSVN_VPS03_ACCEPTANCE"] = "approved",
                ["MYDMSVN_TEST_SQLSERVER_CONNECTION_STRING"] = "Server=(test);Initial Catalog=master;Integrated Security=true",
                ["MYDMSVN_VPS03_API_ORIGIN"] = origin,
                ["MYDMSVN_WEB_TEST_TLS_THUMBPRINT"] = thumbprint,
                ["MYDMSVN_VPS03_DESKTOP_PACKAGE"] = Desktop,
                ["MYDMSVN_VPS03_HOST_PACKAGE"] = Host,
                ["MYDMSVN_VPS03_HEAD_SHA"] = head,
                ["MYDMSVN_VPS03_RESULTS_DIRECTORY"] = Results,
            };

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }

        private static void WritePe(string path, ushort machine)
        {
            var bytes = new byte[512];
            bytes[0] = (byte)'M';
            bytes[1] = (byte)'Z';
            BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3c);
            bytes[0x80] = (byte)'P';
            bytes[0x81] = (byte)'E';
            BitConverter.GetBytes(machine).CopyTo(bytes, 0x84);
            File.WriteAllBytes(path, bytes);
        }
    }
}
