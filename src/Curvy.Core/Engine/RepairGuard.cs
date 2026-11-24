namespace Curvy.Core.Engine;

/// <summary>
/// Limits automatic repairs of Curvy's include line so Curvy never fights another tool in a loop:
/// at most <see cref="MaxRepairs"/> repairs per sliding <see cref="Window"/>. Once exceeded it stays
/// blocked until <see cref="Reset"/> (the user's "Add it back") or the next app start. Not thread-safe:
/// use it from the UI thread.
/// </summary>
public sealed class RepairGuard(TimeProvider time, int maxRepairs = 3, TimeSpan? window = null)
{
    private readonly Queue<DateTimeOffset> _recent = new();

    public int MaxRepairs { get; } = maxRepairs;
    public TimeSpan Window { get; } = window ?? TimeSpan.FromSeconds(60);

    /// <summary>True after the limit was hit; automatic repairs are refused until <see cref="Reset"/>.</summary>
    public bool IsBlocked { get; private set; }

    /// <summary>Asks to repair now; records the repair and returns true when allowed.</summary>
    public bool TryAcquire()
    {
        if (IsBlocked) return false;
        var now = time.GetUtcNow();
        while (_recent.Count > 0 && now - _recent.Peek() >= Window) _recent.Dequeue();
        if (_recent.Count >= MaxRepairs)
        {
            IsBlocked = true;
            return false;
        }
        _recent.Enqueue(now);
        return true;
    }

    public void Reset()
    {
        _recent.Clear();
        IsBlocked = false;
    }
}
