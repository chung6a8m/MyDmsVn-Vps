using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using MyDmsVn.Server.Application.Identity;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace MyDmsVn.Server.HttpHost;

// VPS-04B: a pre-provisioned, append-only Linux destination. No Windows ACL,
// shell command, automatic path creation or "best effort" audit fallback.
// All path components are opened relative to pinned directory descriptors with
// O_NOFOLLOW. statx checks the opened inode (not an attacker-supplied path).
internal sealed class PortableProtectedSecurityAuditSink : ISecurityAuditSink
{
    private const int AtCurrentDirectory = -100;
    private const int AtEmptyPath = 0x1000;
    private const int AtSymlinkNoFollow = 0x100;
    private const int ReadOnly = 0;
    private const int WriteOnly = 1;
    private const int AppendFlag = 0x400;
    private const int NonBlock = 0x800;
    private const int DirectoryOnly = 0x10000;
    private const int NoFollow = 0x20000;
    private const int CloseOnExec = 0x80000;
    private const int LockExclusive = 2;
    private const uint StatxBasicStats = 0x7ff;
    private const uint StatxRequired = 0x10f; // TYPE, MODE, NLINK, UID, INO
    private const int TypeMask = 0xf000;
    private const int RegularFile = 0x8000;
    private const int DirectoryFile = 0x4000;
    private const int PermissionMask = 0xfff;
    private const int OwnerDirectoryPermissions = 0x1c0; // 0700
    private const int OwnerFilePermissions = 0x180; // 0600

    private readonly string[] _components;
    private readonly int _rootDepth;
    private readonly object _writeLock = new();

    internal PortableProtectedSecurityAuditSink(WebHostOptions options)
    {
        if (!OperatingSystem.IsLinux())
            throw new InvalidOperationException("Linux protected audit requires Linux.");
        var root = options.Web6CandidateRoot ??
            throw new ArgumentException("A configured audit root is required.", nameof(options));
        var path = options.SecurityAuditPath ??
            throw new ArgumentException("A configured audit file is required.", nameof(options));
        if (root == "/" || !path.StartsWith(root + "/", StringComparison.Ordinal))
            throw new InvalidOperationException("The audit path is outside the candidate root.");
        _components = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        _rootDepth = root.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
        if (_components.Length <= _rootDepth)
            throw new InvalidOperationException("An audit file is required.");

        // Startup must prove the existing file is a durable private destination.
        // A missing, symlinked, broadly accessible or unwritable file rejects startup.
        Append(null, CancellationToken.None);
    }

    public Task WriteAsync(SecurityAuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        var category = entry.DocumentType switch
        {
            "WebAuthSessionReplay" or "WebAuthRefresh" or "WebAuthLogout" or "WebAuthThrottled" => entry.DocumentType,
            _ => "Application"
        };
        var correlation = entry.CorrelationId != null &&
            Regex.IsMatch(entry.CorrelationId, @"\A[A-Za-z0-9_-]{1,128}\z")
                ? entry.CorrelationId : null;
        // Stable, explicitly ordered, allowlisted audit shape. Include both
        // nullable fields rather than inheriting ambient JSON null-ignore policy.
        var record = new
        {
            category,
            action = Enum.IsDefined(typeof(SecurityAuditAction), entry.Action) ? entry.Action.ToString() : "Unknown",
            outcome = Enum.IsDefined(typeof(SecurityAuditOutcome), entry.Outcome) ? entry.Outcome.ToString() : "Unknown",
            userId = entry.UserId > 0 ? (int?)entry.UserId : null,
            correlationId = correlation,
            utc = entry.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture)
        };
        var line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        }) + "\n");
        lock (_writeLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(line, cancellationToken);
        }
        return Task.CompletedTask;
    }

    private void Append(byte[]? line, CancellationToken token)
    {
        try
        {
            var owner = GetEffectiveUserId();
            using var baseDirectory = Open(AtCurrentDirectory, "/", ReadOnly | DirectoryOnly | NoFollow | CloseOnExec);
            CheckDirectory(ReadMetadata(baseDirectory), owner, privateDirectory: false);

            // Each parent is opened without following links. Its permissions are
            // checked before any subsequent lookup (including the candidate root).
            SafeFileHandle current = baseDirectory;
            try
            {
                // Always release the last descriptor if a later openat/statx
                // fails while traversing a nested audit destination.
                for (var index = 0; index < _components.Length - 1; index++)
                {
                    var next = Open(current.DangerousGetHandle().ToInt32(), _components[index],
                        ReadOnly | DirectoryOnly | NoFollow | CloseOnExec);
                    try
                    {
                        CheckDirectory(ReadMetadata(next), owner, privateDirectory: index + 1 >= _rootDepth);
                    }
                    catch
                    {
                        next.Dispose();
                        throw;
                    }
                    if (!ReferenceEquals(current, baseDirectory))
                        current.Dispose();
                    current = next;
                }

                using var file = Open(current.DangerousGetHandle().ToInt32(), _components[^1],
                    WriteOnly | AppendFlag | NonBlock | NoFollow | CloseOnExec);
                var opened = ReadMetadata(file);
                CheckFile(opened, owner);
                CheckSameFile(opened, ReadMetadata(current, _components[^1]), owner);

                if (Flock(file.DangerousGetHandle().ToInt32(), LockExclusive) != 0)
                    throw new IOException();
                token.ThrowIfCancellationRequested();
                // The opened O_APPEND descriptor is locked across processes and
                // fsync'd before an admitted audit write can be acknowledged.
                using (var stream = new FileStream(file, FileAccess.Write, bufferSize: 4096,
                    isAsync: false))
                {
                    if (line != null) stream.Write(line);
                    stream.Flush(flushToDisk: true);
                }
                CheckSameFile(opened, ReadMetadata(current, _components[^1]), owner);
            }
            finally
            {
                if (!ReferenceEquals(current, baseDirectory))
                    current.Dispose();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            // No file path, inode, exception source, key, token or SQL settings escape.
            throw new IOException("Protected security audit storage is unavailable.");
        }
    }

    private static void CheckDirectory(StatxRecord value, uint owner, bool privateDirectory)
    {
        if ((value.Mode & TypeMask) != DirectoryFile)
            throw new UnauthorizedAccessException();
        var bits = value.Mode & PermissionMask;
        if (privateDirectory)
        {
            if (value.UserId != owner || bits != OwnerDirectoryPermissions)
                throw new UnauthorizedAccessException();
        }
        else
        {
            // Pre-root ancestors must be trusted; a root-owned sticky /tmp is
            // permitted for disposable CI without admitting arbitrary writable
            // parent directories. The configured root itself remains 0700.
            if (value.UserId != 0 && value.UserId != owner)
                throw new UnauthorizedAccessException();
            var rootSticky = value.UserId == 0 && (bits & 0x200) != 0;
            if ((bits & 0x12) != 0 && !rootSticky)
                throw new UnauthorizedAccessException();
        }
    }

    private static void CheckFile(StatxRecord value, uint owner)
    {
        if ((value.Mode & TypeMask) != RegularFile ||
            (value.Mode & PermissionMask) != OwnerFilePermissions ||
            value.UserId != owner || value.LinkCount != 1)
            throw new UnauthorizedAccessException();
    }

    private static void CheckSameFile(StatxRecord opened, StatxRecord linked, uint owner)
    {
        CheckFile(linked, owner);
        if (opened.Inode != linked.Inode ||
            opened.DeviceMajor != linked.DeviceMajor ||
            opened.DeviceMinor != linked.DeviceMinor)
            throw new UnauthorizedAccessException();
    }

    private static StatxRecord ReadMetadata(SafeFileHandle file) =>
        ReadMetadata(file.DangerousGetHandle().ToInt32(), "", AtEmptyPath);

    private static StatxRecord ReadMetadata(SafeFileHandle directory, string name) =>
        ReadMetadata(directory.DangerousGetHandle().ToInt32(), name, AtSymlinkNoFollow);

    private static StatxRecord ReadMetadata(int descriptor, string name, int flags)
    {
        if (Statx(descriptor, name, flags, StatxBasicStats, out var value) != 0 ||
            (value.Mask & StatxRequired) != StatxRequired)
            throw new IOException();
        return value;
    }

    private static SafeFileHandle Open(int directory, string name, int flags)
    {
        var descriptor = OpenAt(directory, name, flags, 0);
        if (descriptor < 0) throw new IOException();
        return new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
    }

    // Linux statx has a fixed 256-byte ABI on supported 64-bit Linux targets.
    // Inode, mode, owner, link count and device are read by explicit offsets.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct StatxRecord
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(16)] public uint LinkCount;
        [FieldOffset(20)] public uint UserId;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();

    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenAt(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags, uint mode);

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags, uint mask, out StatxRecord metadata);

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int Flock(int descriptor, int operation);
}
