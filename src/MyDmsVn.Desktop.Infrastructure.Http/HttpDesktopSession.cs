using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MyDmsVn.Contracts;
using MyDmsVn.Desktop.Application;

namespace MyDmsVn.Desktop.Infrastructure.Http
{
    /// <summary>In-memory credentials and generation fencing; never persists Bearer or refresh secrets.</summary>
    public sealed class HttpDesktopSession : IDesktopSession, IDesktopSessionActivity, IAsyncDesktopSignOut
    {
        private readonly object _sync = new object();
        private readonly DesktopSessionOptions _options;
        private readonly IMonotonicClock _clock;
        private DesktopSessionState _state;
        private CurrentUserDto? _user;
        private string? _accessToken;
        private DateTime _expiresUtc;
        private long _version;
        private TimeSpan _lastActivity;
        private Func<Task>? _revoke;

        public HttpDesktopSession(DesktopSessionOptions options, IMonotonicClock clock)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public event EventHandler? SessionChanged;

        public bool IsAuthenticated { get { lock (_sync) return _state == DesktopSessionState.Authenticated; } }
        public DesktopSessionState State { get { lock (_sync) return _state; } }
        public CurrentUserDto? CurrentUser { get { lock (_sync) return _user; } }
        public long Version { get { lock (_sync) return _version; } }

        internal void RegisterRevoke(Func<Task> revoke)
        {
            _revoke = revoke ?? throw new ArgumentNullException(nameof(revoke));
        }

        internal bool TryBeginLogin(out long generation)
        {
            lock (_sync)
            {
                if (_state == DesktopSessionState.Authenticated ||
                    _state == DesktopSessionState.Authenticating ||
                    _state == DesktopSessionState.LoggingOut)
                {
                    generation = _version;
                    return false;
                }

                _version++;
                generation = _version;
                _state = DesktopSessionState.Authenticating;
                ClearUnsafe();
            }

            SessionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        internal void CancelLogin(long generation)
        {
            var changed = false;
            lock (_sync)
            {
                if (_state == DesktopSessionState.Authenticating && _version == generation)
                {
                    _version++;
                    _state = DesktopSessionState.Unauthenticated;
                    ClearUnsafe();
                    changed = true;
                }
            }

            if (changed) SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        internal bool TrySignIn(long generation, CurrentUserDto user, string token,
            DateTime expiresUtc, CancellationToken cancellationToken)
        {
            if (user == null || string.IsNullOrWhiteSpace(token) ||
                expiresUtc <= DateTime.UtcNow)
                return false;
            lock (_sync)
            {
                if (cancellationToken.IsCancellationRequested ||
                    _state != DesktopSessionState.Authenticating || _version != generation)
                    return false;
                _version++;
                _user = user;
                _accessToken = token;
                _expiresUtc = expiresUtc;
                _lastActivity = _clock.Elapsed;
                _state = DesktopSessionState.Authenticated;
            }

            SessionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        internal bool TryCapture(long? expectedVersion, out long generation,
            out string token, out DateTime expiresUtc)
        {
            bool expired;
            bool accepted;
            lock (_sync)
            {
                expired = ExpireIdleUnsafe();
                generation = _version;
                accepted = _state == DesktopSessionState.Authenticated &&
                    (!expectedVersion.HasValue || expectedVersion.Value == generation) &&
                    !string.IsNullOrEmpty(_accessToken);
                token = accepted ? _accessToken! : string.Empty;
                expiresUtc = accepted ? _expiresUtc : DateTime.MinValue;
            }

            if (expired) SessionChanged?.Invoke(this, EventArgs.Empty);
            return accepted;
        }

        internal bool IsCurrent(long generation)
        {
            lock (_sync)
                return _state == DesktopSessionState.Authenticated && _version == generation;
        }

        internal bool TryRefresh(long generation, string token, DateTime expiresUtc)
        {
            if (string.IsNullOrWhiteSpace(token) || expiresUtc <= DateTime.UtcNow)
                return false;
            lock (_sync)
            {
                if (_state != DesktopSessionState.Authenticated || _version != generation)
                    return false;
                _accessToken = token;
                _expiresUtc = expiresUtc;
                return true;
            }
        }

        internal void RequireReauthentication(long generation)
        {
            var changed = false;
            lock (_sync)
            {
                if (_state == DesktopSessionState.Authenticated && _version == generation)
                {
                    _version++;
                    _state = DesktopSessionState.Expired;
                    ClearUnsafe();
                    changed = true;
                }
            }

            if (changed) SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RecordUserActivity()
        {
            var expired = false;
            lock (_sync)
            {
                expired = ExpireIdleUnsafe();
                if (!expired && _state == DesktopSessionState.Authenticated)
                    _lastActivity = _clock.Elapsed;
            }

            if (expired) SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool ExpireIfIdle()
        {
            bool expired;
            lock (_sync) expired = ExpireIdleUnsafe();
            if (expired) SessionChanged?.Invoke(this, EventArgs.Empty);
            return expired;
        }

        public void SignOut() => SignOutAsync().GetAwaiter().GetResult();

        public async Task SignOutAsync()
        {
            Func<Task>? revoke;
            lock (_sync)
            {
                if (_state == DesktopSessionState.LoggingOut)
                    return;
                revoke = _state == DesktopSessionState.Authenticated ? _revoke : null;
                _version++;
                _state = DesktopSessionState.LoggingOut;
                ClearUnsafe();
            }
            SessionChanged?.Invoke(this, EventArgs.Empty);
            if (revoke != null)
            {
                try
                {
                    // Compatibility callers can still wait synchronously.
                    await Task.Run(revoke).ConfigureAwait(false);
                }
                catch (Exception) { /* Local credentials are already invalidated. */ }
            }
            lock (_sync)
            {
                if (_state == DesktopSessionState.LoggingOut)
                    _state = DesktopSessionState.Unauthenticated;
            }
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        private bool ExpireIdleUnsafe()
        {
            if (_state != DesktopSessionState.Authenticated ||
                _clock.Elapsed - _lastActivity < _options.IdleTimeout)
                return false;
            _version++;
            _state = DesktopSessionState.Expired;
            ClearUnsafe();
            return true;
        }

        private void ClearUnsafe()
        {
            _user = null;
            _accessToken = null;
            _expiresUtc = DateTime.MinValue;
        }
    }

    public sealed class HttpMonotonicClock : IMonotonicClock
    {
        private readonly long _origin = Stopwatch.GetTimestamp();
        public TimeSpan Elapsed => TimeSpan.FromSeconds(
            (Stopwatch.GetTimestamp() - _origin) / (double)Stopwatch.Frequency);
    }
}
