using System;
using MyDmsVn.Contracts;

namespace MyDmsVn.Desktop.Application;

/// <summary>One unsent draft in memory, available to its owner after reauthentication.</summary>
public sealed class SalesDraftRecovery : IDisposable
{
    private readonly object _sync = new object();
    private readonly IDesktopSession _session;
    private (CurrentUserDto Owner, SalesReceiptDto Draft)? _saved;
    private bool _disposed;

    public SalesDraftRecovery(IDesktopSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _session.SessionChanged += SessionChanged;
    }

    public bool HasDraft { get { lock (_sync) return _saved.HasValue; } }

    internal void Preserve(CurrentUserDto owner, SalesReceiptDto draft)
    {
        lock (_sync)
            if (!_disposed && _session.State == DesktopSessionState.Expired)
                _saved = (owner, draft);
    }

    internal SalesReceiptDto? Take(long version)
    {
        lock (_sync)
        {
            if (!_session.IsAuthenticated || _session.Version != version ||
                _saved is not { } saved || !SameActor(saved.Owner, _session.CurrentUser))
                return null;
            _saved = null;
            return saved.Draft;
        }
    }

    private void SessionChanged(object? sender, EventArgs args)
    {
        lock (_sync)
            if (_session.State == DesktopSessionState.LoggingOut ||
                (_session.IsAuthenticated && _saved is { } saved && !SameActor(saved.Owner, _session.CurrentUser)))
                _saved = null;
    }

    private static bool SameActor(CurrentUserDto owner, CurrentUserDto? current) =>
        current != null && current.UserId == owner.UserId &&
        string.Equals(current.Username, owner.Username, StringComparison.Ordinal);

    public void Dispose()
    {
        _session.SessionChanged -= SessionChanged;
        lock (_sync)
        {
            _disposed = true;
            _saved = null;
        }
    }
}
