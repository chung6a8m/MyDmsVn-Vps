using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MyDmsVn.Server.Application.Identity;
using MyDmsVn.Server.HttpHost;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

[SupportedOSPlatform("linux")]
public sealed class PortableAuditLinuxTests
{
    private static readonly UnixFileMode PrivateDirectory =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private static readonly UnixFileMode PrivateFile =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private sealed class Destination : IDisposable
    {
        public Destination()
        {
            Assert.True(OperatingSystem.IsLinux(), "Protected audit acceptance must run on real Linux.");
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "vps04b-private-audit-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Root);
            File.SetUnixFileMode(Root, PrivateDirectory);
            Path = System.IO.Path.Combine(Root, "audit.jsonl");
            File.WriteAllBytes(Path, Array.Empty<byte>());
            File.SetUnixFileMode(Path, PrivateFile);
        }

        public string Root { get; }
        public string Path { get; }
        public WebHostOptions Options(string? auditPath = null) => new(
            publicOrigin: "https://localhost",
            issuer: "https://localhost",
            audience: "vps04b-test",
            activeKeyId: "test-key",
            validationKeys: new Dictionary<string, byte[]>
            {
                ["test-key"] = Enumerable.Repeat((byte)0x5A, 32).ToArray()
            },
            web5Enabled: true,
            web6ResendEligibilityEnabled: true,
            web6CandidateId: "vps04b-ci",
            web6CandidateRoot: Root,
            securityAuditPath: auditPath ?? Path);

        public void Dispose()
        {
            if (File.Exists(Path)) File.Delete(Path);
            if (System.IO.Directory.Exists(Root)) System.IO.Directory.Delete(Root, recursive: true);
        }
    }

    private static SecurityAuditEntry Entry(string? correlation = "audit_04b") => new(
        SecurityAuditAction.Authentication, SecurityAuditOutcome.Denied, 19,
        new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc),
        documentType: "WebAuthRefresh",
        documentId: "secret-document-or-refresh-token",
        correlationId: correlation);

    [Fact]
    public async Task PreprovisionedPrivateLinuxFileAppendsOnlyAllowlistedDataAcrossRestart()
    {
        using var destination = new Destination();
        var sink = new PortableProtectedSecurityAuditSink(destination.Options());
        await sink.WriteAsync(Entry(), CancellationToken.None);
        // A fresh host process reopens the same durable inode; it cannot
        // truncate or substitute a newly created file for an existing log.
        var reopened = new PortableProtectedSecurityAuditSink(destination.Options());
        await reopened.WriteAsync(Entry("audit_second"), CancellationToken.None);

        var lines = File.ReadAllLines(destination.Path);
        Assert.Equal(2, lines.Length);
        foreach (var line in lines)
        {
            using var json = JsonDocument.Parse(line);
            Assert.Equal(new[] { "action", "category", "correlationId", "outcome", "userId", "utc" },
                json.RootElement.EnumerateObject().Select(p => p.Name)
                    .OrderBy(x => x, StringComparer.Ordinal).ToArray());
            Assert.Equal("WebAuthRefresh", json.RootElement.GetProperty("category").GetString());
            Assert.Equal("Denied", json.RootElement.GetProperty("outcome").GetString());
            Assert.Equal(19, json.RootElement.GetProperty("userId").GetInt32());
        }
        Assert.DoesNotContain("secret", string.Concat(lines));
        Assert.Equal(PrivateDirectory, File.GetUnixFileMode(destination.Root));
        Assert.Equal(PrivateFile, File.GetUnixFileMode(destination.Path));
    }

    [Fact]
    public async Task JsonlShapeOrderAndExplicitNullableFieldsRemainByteExactAcrossRestart()
    {
        using var destination = new Destination();
        var first = new PortableProtectedSecurityAuditSink(destination.Options());
        await first.WriteAsync(new SecurityAuditEntry(
            SecurityAuditAction.Authentication, SecurityAuditOutcome.Denied, null,
            new DateTime(2026, 11, 18, 1, 2, 3, DateTimeKind.Utc),
            documentType: "WebAuthRefresh", correlationId: null), CancellationToken.None);

        var reopened = new PortableProtectedSecurityAuditSink(destination.Options());
        await reopened.WriteAsync(new SecurityAuditEntry(
            SecurityAuditAction.Authentication, SecurityAuditOutcome.Succeeded, 19,
            new DateTime(2026, 11, 18, 1, 2, 4, DateTimeKind.Utc),
            documentType: "WebAuthLogout", correlationId: "audit_second"), CancellationToken.None);

        var expected = "{\"category\":\"WebAuthRefresh\",\"action\":\"Authentication\",\"outcome\":\"Denied\",\"userId\":null,\"correlationId\":null,\"utc\":\"2026-11-18T01:02:03.0000000Z\"}\n" +
            "{\"category\":\"WebAuthLogout\",\"action\":\"Authentication\",\"outcome\":\"Succeeded\",\"userId\":19,\"correlationId\":\"audit_second\",\"utc\":\"2026-11-18T01:02:04.0000000Z\"}\n";
        Assert.Equal(Encoding.UTF8.GetBytes(expected), File.ReadAllBytes(destination.Path));
    }

    [Fact]
    public void StartupRejectsBroadDirectoryAndFileModesAndNeverRepairsPermissions()
    {
        using var destination = new Destination();
        File.SetUnixFileMode(destination.Root, PrivateDirectory | UnixFileMode.GroupRead);
        Assert.Throws<IOException>(() => new PortableProtectedSecurityAuditSink(destination.Options()));
        Assert.True(File.GetUnixFileMode(destination.Root).HasFlag(UnixFileMode.GroupRead));

        File.SetUnixFileMode(destination.Root, PrivateDirectory);
        File.SetUnixFileMode(destination.Path, PrivateFile | UnixFileMode.OtherRead);
        var error = Assert.Throws<IOException>(() =>
            new PortableProtectedSecurityAuditSink(destination.Options()));
        Assert.Equal("Protected security audit storage is unavailable.", error.Message);
        Assert.Null(error.InnerException);
        Assert.Equal(PrivateFile | UnixFileMode.OtherRead, File.GetUnixFileMode(destination.Path));
    }

    [Fact]
    public async Task RuntimePermissionDriftRefusesWriteAndNeverLeaksPath()
    {
        using var destination = new Destination();
        var sink = new PortableProtectedSecurityAuditSink(destination.Options());
        File.SetUnixFileMode(destination.Path, PrivateFile | UnixFileMode.GroupRead);
        var error = await Assert.ThrowsAsync<IOException>(() =>
            sink.WriteAsync(Entry(), CancellationToken.None));
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(destination.Path, error.Message);
        Assert.Empty(File.ReadAllLines(destination.Path));
    }

    [Fact]
    public async Task FailedNestedDirectoryTraversalDoesNotLeakFileDescriptors()
    {
        using var destination = new Destination();
        var parent = System.IO.Path.Combine(destination.Root, "nested");
        var leaf = System.IO.Path.Combine(parent, "leaf");
        System.IO.Directory.CreateDirectory(leaf);
        File.SetUnixFileMode(parent, PrivateDirectory);
        File.SetUnixFileMode(leaf, PrivateDirectory);
        var nestedAudit = System.IO.Path.Combine(leaf, "audit.jsonl");
        File.WriteAllBytes(nestedAudit, Array.Empty<byte>());
        File.SetUnixFileMode(nestedAudit, PrivateFile);
        var sink = new PortableProtectedSecurityAuditSink(destination.Options(nestedAudit));

        // Root and first nested parent remain openable; the next openat fails.
        // A leaking traversal retains one directory descriptor on each failure.
        System.IO.Directory.Move(leaf, leaf + "-moved");
        var before = System.IO.Directory.EnumerateFileSystemEntries("/proc/self/fd").Count();
        for (var i = 0; i < 64; i++)
            await Assert.ThrowsAsync<IOException>(() =>
                sink.WriteAsync(Entry(), CancellationToken.None));
        var after = System.IO.Directory.EnumerateFileSystemEntries("/proc/self/fd").Count();
        Assert.InRange(after - before, -4, 8);
    }

    [Fact]
    public void StartupRejectsSymlinkedFileAndMissingFile()
    {
        using var destination = new Destination();
        var alternate = System.IO.Path.Combine(destination.Root, "other.jsonl");
        File.Move(destination.Path, alternate);
        Assert.Throws<IOException>(() => new PortableProtectedSecurityAuditSink(destination.Options()));
        File.CreateSymbolicLink(destination.Path, alternate);
        Assert.Throws<IOException>(() => new PortableProtectedSecurityAuditSink(destination.Options()));
    }

    [Fact]
    public void LinuxPathsMustBeAbsoluteCanonicalAndInsidePrivateCandidateRoot()
    {
        using var destination = new Destination();
        Assert.Throws<ArgumentException>(() => new WebHostOptions(
            "https://localhost", "https://localhost", "vps04b-test", "test-key",
            new Dictionary<string, byte[]> { ["test-key"] = new byte[32] },
            web5Enabled: true, web6ResendEligibilityEnabled: true,
            web6CandidateId: "vps04b-ci", web6CandidateRoot: destination.Root,
            securityAuditPath: destination.Root + "/../escape.jsonl"));
        Assert.Throws<ArgumentException>(() => new WebHostOptions(
            "https://localhost", "https://localhost", "vps04b-test", "test-key",
            new Dictionary<string, byte[]> { ["test-key"] = new byte[32] },
            web5Enabled: true, web6ResendEligibilityEnabled: true,
            web6CandidateId: "vps04b-ci", web6CandidateRoot: "C:\\temp\\vps04b",
            securityAuditPath: "C:\\temp\\vps04b\\audit.jsonl"));
    }
}
