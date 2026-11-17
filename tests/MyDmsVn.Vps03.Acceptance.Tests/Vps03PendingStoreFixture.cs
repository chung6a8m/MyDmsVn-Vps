using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using MyDmsVn.Desktop.Infrastructure.Http;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03PendingStoreFixture : IDisposable
{
    private readonly string _exchange;
    private readonly HashSet<string> _intentPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly SecurityIdentifier _owner;
    private readonly SecurityIdentifier _system =
        new(WellKnownSidType.LocalSystemSid, null);
    private bool _disposed;

    private Vps03PendingStoreFixture(string origin, string exchange,
        HttpPendingWriteRecord record, SecurityIdentifier owner)
    {
        Origin = origin;
        _exchange = exchange;
        Record = record;
        _owner = owner;
        _intentPaths.Add(IntentPath);
    }

    internal string Origin { get; }
    internal HttpPendingWriteRecord Record { get; }
    internal string IntentPath => Vps03PendingStoreProcess.IntentPathFor(Origin);
    internal bool IntentExists => File.Exists(IntentPath);

    internal static Vps03PendingStoreFixture Create()
    {
        SecurityIdentifier owner;
        using (var identity = WindowsIdentity.GetCurrent())
            owner = identity.User ?? throw new InvalidOperationException("VPS03.PendingOwnerUnavailable");
        return new Vps03PendingStoreFixture(UniqueOrigin(),
            Vps03PendingStoreProcess.CreateExchangeDirectory(), NewRecord(), owner);
    }

    internal static string UniqueOrigin() =>
        "https://issue7-file-" + Guid.NewGuid().ToString("N") + ".invalid";

    internal void SaveInChild()
    {
        using var child = Vps03PendingStoreProcess.Start("after-save", Origin, _exchange, Record);
        child.WaitForStage("after-save-complete", TimeSpan.FromSeconds(15));
        child.KillAndWait(TimeSpan.FromSeconds(15));
        if (child.IntentCount != 1 || !IntentExists)
            throw new InvalidDataException("VPS03.PendingIntentCount");
        ValidateOwnerOnly(Directory.GetAccessControl(Path.GetDirectoryName(IntentPath)!));
        ValidateOwnerOnly(File.GetAccessControl(IntentPath));
    }

    internal Vps03PendingStoreLoadResult LoadInChild() => LoadInChild(Origin);

    internal Vps03PendingStoreLoadResult LoadInChild(string origin)
    {
        using var child = Vps03PendingStoreProcess.Start("load", origin, _exchange, Record);
        var result = child.WaitForLoad(TimeSpan.FromSeconds(15));
        if (!child.WaitForExit(TimeSpan.FromSeconds(15)))
            throw new TimeoutException("VPS03.PendingLoadDrainTimeout");
        return result;
    }

    internal void ReplaceCiphertext(byte[] bytes)
    {
        RequireExactOwnedIntent(IntentPath);
        if (bytes == null || bytes.Length <= 0 || bytes.Length > 8192)
            throw new ArgumentOutOfRangeException(nameof(bytes));
        File.WriteAllBytes(IntentPath, bytes);
    }

    internal void WeakenAcl()
    {
        RequireExactOwnedIntent(IntentPath);
        var acl = File.GetAccessControl(IntentPath);
        acl.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.Read, AccessControlType.Allow));
        File.SetAccessControl(IntentPath, acl);
    }

    internal void CopyToOrigin(string otherOrigin)
    {
        _ = new HttpDesktopOptions(otherOrigin);
        RequireExactOwnedIntent(IntentPath);
        var target = Vps03PendingStoreProcess.IntentPathFor(otherOrigin);
        RequirePendingPath(target);
        if (File.Exists(target)) throw new InvalidOperationException("VPS03.PendingTargetExists");
        File.Copy(IntentPath, target, false);
        SetOwnerOnly(target);
        _intentPaths.Add(target);
    }

    internal bool IntentExistsAt(string origin) =>
        File.Exists(Vps03PendingStoreProcess.IntentPathFor(origin));

    internal void Clear(HttpPendingWriteRecord expected) =>
        new WindowsHttpPendingWriteStore(new HttpDesktopOptions(Origin)).Clear(expected);

    internal HttpPendingWriteRecord CloneRecord() => new()
    {
        Version = Record.Version,
        OperationId = Record.OperationId,
        ActorUserId = Record.ActorUserId,
        Protocol = Record.Protocol,
        Kind = Record.Kind,
        Method = Record.Method,
        Route = Record.Route,
        TargetId = Record.TargetId,
        DeploymentEpoch = Record.DeploymentEpoch,
        Fingerprint = Record.Fingerprint,
        CreatedUtc = Record.CreatedUtc,
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var path in _intentPaths)
        {
            RequirePendingPath(path);
            if (!File.Exists(path)) continue;
            SetOwnerOnly(path);
            File.Delete(path);
        }
        Vps03PendingStoreProcess.ValidateExchange(_exchange);
        if (Directory.GetFiles(_exchange, "*", SearchOption.AllDirectories).Length > 5)
            throw new InvalidDataException("VPS03.PendingCleanupFileCount");
        Directory.Delete(_exchange, true);
    }

    private void RequireExactOwnedIntent(string path)
    {
        RequirePendingPath(path);
        if (!_intentPaths.Contains(path) || !File.Exists(path))
            throw new FileNotFoundException("VPS03.PendingIntentMissing", path);
    }

    private static void RequirePendingPath(string path)
    {
        var full = Path.GetFullPath(path);
        var parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyDmsVn-Vps", "PendingWrites");
        if (!string.Equals(Path.GetDirectoryName(full), parent, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(full).Length != 71 ||
            !Path.GetFileName(full).EndsWith(".intent", StringComparison.Ordinal) ||
            Path.GetFileNameWithoutExtension(full).Any(c =>
                !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))))
            throw new InvalidDataException("VPS03.PendingCleanupPathUnsafe");
    }

    private void SetOwnerOnly(string path)
    {
        var acl = new FileSecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.SetOwner(_owner);
        acl.AddAccessRule(new FileSystemAccessRule(_owner,
            FileSystemRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(_system,
            FileSystemRights.FullControl, AccessControlType.Allow));
        File.SetAccessControl(path, acl);
    }

    private void ValidateOwnerOnly(FileSystemSecurity acl)
    {
        if (!Equals(acl.GetOwner(typeof(SecurityIdentifier)), _owner) ||
            !acl.AreAccessRulesProtected)
            throw new InvalidDataException("VPS03.PendingAclUnsafe");
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, false,
                     typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType == AccessControlType.Allow &&
                !Equals(rule.IdentityReference, _owner) &&
                !Equals(rule.IdentityReference, _system))
                throw new InvalidDataException("VPS03.PendingAclUnsafe");
        }
    }

    private static HttpPendingWriteRecord NewRecord() => new()
    {
        OperationId = Guid.NewGuid().ToString("D"),
        ActorUserId = 7302,
        Protocol = "web5",
        Kind = "Receipt.Post",
        Method = "POST",
        Route = "/api/v1/goods-receipts/7302/post",
        TargetId = 7302,
        DeploymentEpoch = Guid.NewGuid().ToString("D"),
        Fingerprint = new string('a', 64),
        CreatedUtc = DateTime.UtcNow,
    };
}
