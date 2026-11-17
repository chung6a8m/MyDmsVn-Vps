using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03PreflightResult
{
    internal Vps03PreflightResult(Uri origin, string headSha, string tlsThumbprint, int desktopBitness)
    {
        Origin = origin;
        HeadSha = headSha;
        TlsThumbprint = tlsThumbprint;
        DesktopBitness = desktopBitness;
    }

    internal Uri Origin { get; }
    internal string HeadSha { get; }
    internal string TlsThumbprint { get; }
    internal int DesktopBitness { get; }
}

internal static class Vps03AcceptancePreflight
{
    private static readonly Regex ShaPattern = new("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex ThumbprintPattern = new("^[0-9A-F]{40}$", RegexOptions.CultureInvariant);

    internal static Vps03PreflightResult Validate(Vps03AcceptanceOptions options)
    {
        if (!ShaPattern.IsMatch(options.HeadSha))
            throw new InvalidDataException("VPS-03 HEAD must be a full hexadecimal Git SHA.");
        var head = options.HeadSha.ToLowerInvariant();
        var origin = CanonicalOrigin(options.Origin);
        var thumbprint = new string(options.TlsThumbprint.Where(character => !char.IsWhiteSpace(character)).ToArray()).ToUpperInvariant();
        if (!ThumbprintPattern.IsMatch(thumbprint))
            throw new InvalidDataException("VPS-03 TLS thumbprint must contain exactly 40 hexadecimal characters.");

        RequireDirectory(options.DesktopPackage, "Desktop package");
        RequireDirectory(options.HostPackage, "Host package");
        RequireDirectory(options.ResultsDirectory, "Results directory");
        ValidateProvenance(options.DesktopPackage, head);
        ValidateProvenance(options.HostPackage, head);

        var executable = Path.Combine(options.DesktopPackage, "MyDmsVn.Desktop.App.exe");
        if (!File.Exists(executable)) throw new InvalidDataException("Published Desktop executable is absent.");
        var bitness = ReadPeBitness(executable);
        if (bitness != 32) throw new InvalidDataException("Published Desktop executable must be x86.");
        return new Vps03PreflightResult(origin, head, thumbprint, bitness);
    }

    private static Uri CanonicalOrigin(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var origin) || origin.Scheme != Uri.UriSchemeHttps ||
            origin.UserInfo.Length != 0 || value != origin.GetLeftPart(UriPartial.Authority))
            throw new InvalidDataException("VPS-03 API origin must be one canonical HTTPS authority without a trailing slash.");
        return origin;
    }

    private static void RequireDirectory(string path, string label)
    {
        if (!Path.IsPathRooted(path) || !Directory.Exists(path))
            throw new InvalidDataException(label + " must be an existing absolute directory.");
    }

    private static void ValidateProvenance(string package, string expectedHead)
    {
        var path = Path.Combine(package, "vps03-package-provenance.json");
        if (!File.Exists(path)) throw new InvalidDataException("Package provenance is absent.");
        var document = JObject.Parse(File.ReadAllText(path));
        var actual = (string?)document["headSha"];
        if (!string.Equals(actual, expectedHead, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package provenance does not match the exact VPS-03 HEAD.");
    }

    private static int ReadPeBitness(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 0x88 || reader.ReadUInt16() != 0x5a4d)
            throw new InvalidDataException("Desktop executable has an invalid PE header.");
        stream.Position = 0x3c;
        var peOffset = reader.ReadInt32();
        if (peOffset < 0x40 || peOffset > stream.Length - 6)
            throw new InvalidDataException("Desktop executable has an invalid PE offset.");
        stream.Position = peOffset;
        if (reader.ReadUInt32() != 0x00004550)
            throw new InvalidDataException("Desktop executable has an invalid PE signature.");
        return reader.ReadUInt16() switch
        {
            0x014c => 32,
            0x8664 => 64,
            _ => throw new InvalidDataException("Desktop executable has an unsupported PE machine."),
        };
    }
}
