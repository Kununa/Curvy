namespace Curvy.Core.Engine;

/// <summary>
/// Timing for the <c>config.txt</c> watcher (UI-free so it can be unit-tested with a fake
/// <see cref="TimeProvider"/>). <see cref="NotifyChanged"/> (file-system event, any thread) starts or
/// restarts a <see cref="Debounce"/> timer; when it elapses <c>onFire</c> runs on a timer thread
/// (callers marshal to the UI thread). Changes are ignored while Curvy's own write is recent: a
/// change reported within <see cref="Suppress"/> of <see cref="NotifyOwnWrite"/>, or a pending check
/// whose timer elapses within that window (the watcher may report a write before Curvy marks it),
/// is dropped.
/// </summary>
public sealed class ConfigChangeDebouncer : IDisposable
{
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan DefaultSuppress = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _time;
    private readonly Action _onFire;
    private readonly ITimer _timer;
    private readonly Lock _gate = new();
    private DateTimeOffset? _lastOwnWrite;
    private bool _disposed;

    public ConfigChangeDebouncer(TimeProvider time, Action onFire, TimeSpan? debounce = null, TimeSpan? suppress = null)
    {
        _time = time;
        _onFire = onFire;
        Debounce = debounce ?? DefaultDebounce;
        Suppress = suppress ?? DefaultSuppress;
        _timer = time.CreateTimer(_ => Fire(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public TimeSpan Debounce { get; }
    public TimeSpan Suppress { get; }

    /// <summary>Curvy is about to write (or just wrote) <c>config.txt</c>.</summary>
    public void NotifyOwnWrite()
    {
        lock (_gate) _lastOwnWrite = _time.GetUtcNow();
    }

    /// <summary>The watcher reported a change.</summary>
    public void NotifyChanged()
    {
        lock (_gate)
        {
            if (_disposed || IsSuppressed()) return;
            _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        lock (_gate)
        {
            if (_disposed || IsSuppressed()) return;
        }
        _onFire();
    }

    private bool IsSuppressed() =>
        _lastOwnWrite is { } w && _time.GetUtcNow() - w < Suppress;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _timer.Dispose();
    }
}
