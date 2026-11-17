using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace MyDmsVn.Desktop.Infrastructure.Http
{
    /// <summary>One bounded intent per Windows owner and HTTPS origin, independent of the login session.</summary>
    public sealed class HttpPendingWriteRecord
    {
        public int Version { get; set; } = 1;
        public string OperationId { get; set; } = "";
        public int ActorUserId { get; set; }
        public string Protocol { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Method { get; set; } = "";
        public string Route { get; set; } = "";
        public long? TargetId { get; set; }
        public string? DeploymentEpoch { get; set; }
        public string Fingerprint { get; set; } = "";
        public DateTime CreatedUtc { get; set; }

        public void Validate()
        {
            if (Version != 1 || ActorUserId <= 0 ||
                !Guid.TryParseExact(OperationId, "D", out var key) ||
                key.ToString("D") != OperationId ||
                OperationId[14] != '4' || "89ab".IndexOf(OperationId[19]) < 0 ||
                !(Protocol == "web4" || Protocol == "web5" || Protocol == "legacy" || Protocol == "readback") ||
                string.IsNullOrEmpty(Kind) || Kind.Length > 64 ||
                Kind.Any(c => !(char.IsLetterOrDigit(c) || c == '.')) ||
                !(Method == "POST" || Method == "PUT" || Method == "DELETE") ||
                string.IsNullOrEmpty(Route) || Route.Length > 256 ||
                !Route.StartsWith("/api/v1/", StringComparison.Ordinal) ||
                Route.IndexOfAny(new[] { '?', '#', '\\', '\r', '\n' }) >= 0 ||
                (TargetId.HasValue && TargetId.Value <= 0) ||
                Fingerprint.Length != 64 ||
                Fingerprint.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) ||
                CreatedUtc.Kind != DateTimeKind.Utc ||
                CreatedUtc > DateTime.UtcNow.AddMinutes(5) ||
                CreatedUtc < new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                throw new InvalidOperationException("Desktop.PendingIntentInvalid");

            if (Protocol == "web4" || Protocol == "web5")
            {
                if (!Guid.TryParseExact(DeploymentEpoch, "D", out var epoch) ||
                    epoch == Guid.Empty || DeploymentEpoch != epoch.ToString("D") ||
                    !(Kind == "Sales.Create" || Kind == "Sales.Update" || Kind == "Sales.Post" ||
                      Kind == "Receipt.Create" || Kind == "Receipt.Update" || Kind == "Receipt.Post"))
                    throw new InvalidOperationException("Desktop.PendingIntentInvalid");
                var create = Kind == "Sales.Create" || Kind == "Receipt.Create";
                if (create == TargetId.HasValue)
                    throw new InvalidOperationException("Desktop.PendingIntentInvalid");
            }
            else if (DeploymentEpoch != null)
                throw new InvalidOperationException("Desktop.PendingIntentInvalid");
        }
    }

    public interface IHttpPendingWriteStore
    {
        HttpPendingWriteRecord? Load();
        void Save(HttpPendingWriteRecord record);
        void Clear(HttpPendingWriteRecord expected);
    }

    /// <summary>
    /// Current-user DPAPI plus owner-only Windows ACL. CreateNew + write-through + same-volume
    /// atomic rename is the dispatch barrier. An unreadable/tampered record blocks all writes.
    /// No secret, body, cookie or bearer token is ever serialized.
    /// </summary>
    public sealed class WindowsHttpPendingWriteStore : IHttpPendingWriteStore
    {
        private const int MaximumRecordBytes = 4096;
        private const int MaximumCipherBytes = 8192;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly string _file;
        private readonly SecurityIdentifier _owner;
        private readonly SecurityIdentifier _system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        private readonly byte[] _entropy;
        private readonly string _mutexName;
        private readonly Action? _beforeClearDelete;

        public WindowsHttpPendingWriteStore(HttpDesktopOptions options) : this(options, null) { }

        internal WindowsHttpPendingWriteStore(HttpDesktopOptions options, Action? beforeClearDelete)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new InvalidOperationException("Desktop.PendingWindowsRequired");
            using (var identity = WindowsIdentity.GetCurrent())
                _owner = identity.User ?? throw new InvalidOperationException("Desktop.PendingOwnerUnavailable");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(home) || !Path.IsPathRooted(home))
                throw new InvalidOperationException("Desktop.PendingOwnerUnavailable");
            var directory = Path.Combine(home, "MyDmsVn-Vps", "PendingWrites");
            Directory.CreateDirectory(directory);
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Desktop.PendingDirectoryUnsafe");

            var directoryAcl = new DirectorySecurity();
            directoryAcl.SetAccessRuleProtection(true, false);
            directoryAcl.SetOwner(_owner);
            var inherited = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            directoryAcl.AddAccessRule(new FileSystemAccessRule(_owner, FileSystemRights.FullControl,
                inherited, PropagationFlags.None, AccessControlType.Allow));
            directoryAcl.AddAccessRule(new FileSystemAccessRule(_system, FileSystemRights.FullControl,
                inherited, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(directory, directoryAcl);
            ValidateOwner(Directory.GetAccessControl(directory));
            var binding = "MyDmsVn-Vps|pending-v1|" + _owner.Value + "|" +
                options.Origin.GetLeftPart(UriPartial.Authority);
            using (var sha = SHA256.Create())
                _entropy = sha.ComputeHash(Utf8.GetBytes(binding));
            _mutexName = @"Local\MyDmsVn-Vps.PendingWrite." + Hex(_entropy);
            _beforeClearDelete = beforeClearDelete;
            _file = Path.Combine(directory, Hex(_entropy) + ".intent");
            // Eager load: startup must fail closed if the protected record cannot be read.
            Load();
        }

        public HttpPendingWriteRecord? Load()
        {
            try
            {
                FileAttributes flags;
                try
                {
                    flags = File.GetAttributes(_file);
                }
                catch (FileNotFoundException)
                {
                    // An actually absent file is the ONLY clean-slate condition.
                    // File.Exists is deliberately not used: it also returns false
                    // for certain inaccessible files and would silently drop the fence.
                    return null;
                }
                if ((flags & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Desktop.PendingIntentCorrupt");
                ValidateOwner(File.GetAccessControl(_file));
                byte[] ciphertext;
                using (var input = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    if (input.Length <= 0 || input.Length > MaximumCipherBytes)
                        throw new InvalidOperationException("Desktop.PendingIntentCorrupt");
                    ciphertext = new byte[checked((int)input.Length)];
                    var offset = 0;
                    while (offset < ciphertext.Length)
                    {
                        var count = input.Read(ciphertext, offset, ciphertext.Length - offset);
                        if (count <= 0) throw new InvalidOperationException("Desktop.PendingIntentCorrupt");
                        offset += count;
                    }
                }
                var bytes = ProtectedData.Unprotect(ciphertext, _entropy, DataProtectionScope.CurrentUser);
                if (bytes.Length == 0 || bytes.Length > MaximumRecordBytes)
                    throw new InvalidOperationException("Desktop.PendingIntentCorrupt");
                var record = JsonConvert.DeserializeObject<HttpPendingWriteRecord>(Utf8.GetString(bytes),
                    new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error,
                        DateParseHandling = DateParseHandling.DateTime, TypeNameHandling = TypeNameHandling.None })
                    ?? throw new InvalidOperationException("Desktop.PendingIntentCorrupt");
                record.Validate();
                return record;
            }
            catch (Exception error) when (error is CryptographicException ||
                error is IOException || error is UnauthorizedAccessException ||
                error is JsonException || error is DecoderFallbackException ||
                error is InvalidOperationException || error is ArgumentException)
            {
                throw new InvalidOperationException("Desktop.PendingIntentCorrupt", error);
            }
        }

        public void Save(HttpPendingWriteRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            record.Validate();
            var bytes = Utf8.GetBytes(JsonConvert.SerializeObject(record, Formatting.None));
            if (bytes.Length > MaximumRecordBytes)
                throw new InvalidOperationException("Desktop.PendingIntentTooLarge");
            var encrypted = ProtectedData.Protect(bytes, _entropy, DataProtectionScope.CurrentUser);
            if (encrypted.Length > MaximumCipherBytes)
                throw new InvalidOperationException("Desktop.PendingIntentTooLarge");
            using (AcquireMutationLock())
            {
                if (File.Exists(_file))
                    throw new InvalidOperationException("Desktop.PendingWriteAlreadyExists");
                var temp = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, 4096, FileOptions.WriteThrough))
                    {
                        output.Write(encrypted, 0, encrypted.Length);
                        output.Flush(true);
                    }
                    var fileAcl = new FileSecurity();
                    fileAcl.SetAccessRuleProtection(true, false);
                    fileAcl.SetOwner(_owner);
                    fileAcl.AddAccessRule(new FileSystemAccessRule(_owner, FileSystemRights.FullControl, AccessControlType.Allow));
                    fileAcl.AddAccessRule(new FileSystemAccessRule(_system, FileSystemRights.FullControl, AccessControlType.Allow));
                    File.SetAccessControl(temp, fileAcl);
                    // File.Move refuses an existing destination. The owner/origin mutation lock
                    // also keeps compare-delete and replacement save in one serial order.
                    File.Move(temp, _file);
                }
                finally
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
            }
        }

        public void Clear(HttpPendingWriteRecord expected)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            using (AcquireMutationLock())
            {
                var stored = Load();
                if (stored == null || stored.OperationId != expected.OperationId ||
                    stored.Fingerprint != expected.Fingerprint)
                    throw new InvalidOperationException("Desktop.PendingWriteChanged");
                _beforeClearDelete?.Invoke();
                File.Delete(_file);
            }
        }

        private IDisposable AcquireMutationLock()
        {
            var mutex = new Mutex(false, _mutexName);
            var acquired = false;
            try
            {
                try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(15)); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new InvalidOperationException("Desktop.PendingWriteBusy");
                return new MutationLock(mutex);
            }
            catch
            {
                if (acquired) mutex.ReleaseMutex();
                mutex.Dispose();
                throw;
            }
        }

        private sealed class MutationLock : IDisposable
        {
            private Mutex? _mutex;
            internal MutationLock(Mutex mutex) => _mutex = mutex;
            public void Dispose()
            {
                var mutex = Interlocked.Exchange(ref _mutex, null);
                if (mutex == null) return;
                try { mutex.ReleaseMutex(); }
                finally { mutex.Dispose(); }
            }
        }

        private void ValidateOwner(FileSystemSecurity acl)
        {
            if (!Equals(acl.GetOwner(typeof(SecurityIdentifier)), _owner))
                throw new InvalidOperationException("Desktop.PendingOwnerMismatch");
            if (!acl.AreAccessRulesProtected)
                throw new InvalidOperationException("Desktop.PendingAclUnsafe");
            var rules = acl.GetAccessRules(true, false, typeof(SecurityIdentifier));
            foreach (FileSystemAccessRule rule in rules)
            {
                if (rule.AccessControlType == AccessControlType.Allow &&
                    !Equals(rule.IdentityReference, _owner) &&
                    !Equals(rule.IdentityReference, _system))
                    throw new InvalidOperationException("Desktop.PendingAclUnsafe");
            }
        }

        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}
