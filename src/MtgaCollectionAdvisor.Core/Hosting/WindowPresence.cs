namespace MtgaCollectionAdvisor.Core.Hosting;

/// <summary>
/// Whether the app still has a window, and so whether it should keep running. The window
/// is only a browser pointed at the local server; without this, closing it leaves the
/// server - and its MTG Arena watcher - running invisibly until killed.
///
/// The grace period lets a reload or a dropped connection come back before anything stops.
/// Nothing stops until a first window has connected, so a run nobody opens (smoke tests
/// with <c>--no-browser</c>) stays up.
/// </summary>
public sealed class WindowPresence(TimeSpan gracePeriod)
{
    public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromSeconds(45);

    private readonly Lock _lock = new();
    private int _open;
    private bool _everConnected;
    private DateTimeOffset? _emptySince;

    public WindowPresence() : this(DefaultGracePeriod) { }

    public void Connected()
    {
        lock (_lock)
        {
            _open++;
            _everConnected = true;
            _emptySince = null;
        }
    }

    public void Disconnected(DateTimeOffset now)
    {
        lock (_lock)
        {
            // Never below zero: a stray extra disconnect must not make the next window
            // count as none.
            _open = Math.Max(0, _open - 1);
            if (_open == 0) _emptySince = now;
        }
    }

    public bool ShouldStop(DateTimeOffset now)
    {
        lock (_lock)
        {
            return _everConnected && _open == 0 && _emptySince is { } since && now - since >= gracePeriod;
        }
    }
}
