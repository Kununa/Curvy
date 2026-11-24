namespace Curvy.Core.Engine;

/// <summary>
/// Timing for the <c>config.txt</c> watcher (UI-free so it can be unit-tested with a fake
/// <see cref="TimeProvider"/>). <see cref="NotifyChanged"/> (file-system event, any thread) starts or
/// restarts a <see cref="Debounce"/> timer; when it elapses <c>onFire</c> runs on a timer thread
/// (callers marshal to the UI thread). Changes are never dropped, only deferred past Curvy's own
/// writes: a change reported within <see cref="Suppress"/> of <see cref="NotifyOwnWrite"/> is checked
/// when that window ends (but no sooner than the debounce), and a pending check whose timer elapses
/// inside the window (the watcher may report a write before Curvy marks it) is rescheduled to the
/// window's end. A real external edit that coincides with Curvy's write is therefore still seen.
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
            if (_disposed) return;
            var wait = Debounce;
            if (RemainingSuppression() is { } left && left > wait) wait = left;
            _timer.Change(wait, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (RemainingSuppression() is { } left)
            {
                _timer.Change(left, Timeout.InfiniteTimeSpan); // defer to the end of the own-write window
                return;
            }
        }
        _onFire();
    }

    /// <summary>Time left in the own-write window, or null when outside it.</summary>
    private TimeSpan? RemainingSuppression()
    {
        if (_lastOwnWrite is not { } w) return null;
        var left = w + Suppress - _time.GetUtcNow();
        return left > TimeSpan.Zero ? left : null;
    }

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
