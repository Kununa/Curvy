using System.Collections.Concurrent;
using Curvy.Core.Model;

namespace Curvy.Core.AutoEq;

/// <summary>
/// Keeps the AutoEQ index in memory and makes sure at most one index load runs at a time
/// (<see cref="AutoEqClient"/> has no internal lock and writes its cache files).
/// A plain load while another is running joins it; a forced refresh while a plain load is running
/// waits for that load and then refreshes. A failed load leaves <see cref="Current"/> unchanged.
/// The load itself is shared, so it is not cancellable per caller: callers that go away should stop
/// waiting (e.g. <c>task.WaitAsync(ct)</c>) and let the load finish and fill the cache.
/// </summary>
public sealed class AutoEqIndexLoader(Func<bool, CancellationToken, Task<IReadOnlyList<AutoEqIndexEntry>>> load)
{
    private readonly Lock _gate = new();
    private Task<IReadOnlyList<AutoEqIndexEntry>>? _inFlight;
    private bool _inFlightForced;
    private volatile IReadOnlyList<AutoEqIndexEntry>? _current;

    public AutoEqIndexLoader(AutoEqClient client)
        : this((force, ct) => client.GetIndexAsync(force, ct)) { }

    /// <summary>The last successfully loaded index, or null before the first success.</summary>
    public IReadOnlyList<AutoEqIndexEntry>? Current => _current;

    /// <summary>True while a load is running.</summary>
    public bool IsLoading
    {
        get { lock (_gate) return _inFlight is { IsCompleted: false }; }
    }

    public Task<IReadOnlyList<AutoEqIndexEntry>> LoadAsync(bool forceRefresh = false)
    {
        lock (_gate)
        {
            if (!forceRefresh && _current is { } cached && _inFlight is not { IsCompleted: false })
                return Task.FromResult(cached);
            if (_inFlight is { IsCompleted: false } running)
            {
                if (!forceRefresh || _inFlightForced) return running;
                _inFlightForced = true;
                return _inFlight = RunAfter(running);
            }
            _inFlightForced = forceRefresh;
            return _inFlight = Run(forceRefresh);
        }
    }

    private async Task<IReadOnlyList<AutoEqIndexEntry>> Run(bool force)
    {
        await Task.Yield(); // never run the loader synchronously inside the lock
        var entries = await load(force, CancellationToken.None).ConfigureAwait(false);
        _current = entries;
        return entries;
    }

    private async Task<IReadOnlyList<AutoEqIndexEntry>> RunAfter(Task previous)
    {
        try { await previous.ConfigureAwait(false); }
        catch (Exception) { /* the earlier caller observes its own failure */ }
        return await Run(true).ConfigureAwait(false);
    }
}

/// <summary>
/// Successfully fetched AutoEQ corrections, per index entry, so previewing the same entry twice
/// (or previewing and then creating a profile) doesn't refetch. Failed or cancelled fetches aren't cached.
/// </summary>
public sealed class AutoEqCorrectionCache(Func<AutoEqIndexEntry, CancellationToken, Task<AutoEqCorrection>> fetch)
{
    private readonly ConcurrentDictionary<AutoEqIndexEntry, AutoEqCorrection> _cache = new();

    public AutoEqCorrectionCache(AutoEqClient client) : this(client.FetchCorrectionAsync) { }

    public bool TryGet(AutoEqIndexEntry entry, out AutoEqCorrection correction) =>
        _cache.TryGetValue(entry, out correction!);

    public async Task<AutoEqCorrection> GetAsync(AutoEqIndexEntry entry, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(entry, out var hit)) return hit;
        var correction = await fetch(entry, ct).ConfigureAwait(false);
        return _cache.GetOrAdd(entry, correction);
    }

    /// <summary>Forget everything (after an index refresh, files may have changed upstream).</summary>
    public void Clear() => _cache.Clear();
}
