namespace Curvy.Core.Engine;

/// <summary>
/// Bookkeeping for keyed debounced work (UI-free so it can be unit-tested). Holds at most one
/// pending action per key; setting a key again replaces its action but keeps the key's original
/// position. <see cref="TakeAll"/> returns every pending action in first-insertion order and
/// clears the store, so actions that schedule new work while running don't loop.
/// </summary>
public sealed class KeyedPending
{
    private readonly List<(string Key, Action Action)> _items = [];

    public bool IsEmpty => _items.Count == 0;

    public bool Contains(string key) => _items.Exists(x => x.Key == key);

    public void Set(string key, Action action)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(action);
        var i = _items.FindIndex(x => x.Key == key);
        if (i >= 0) _items[i] = (key, action);
        else _items.Add((key, action));
    }

    public IReadOnlyList<Action> TakeAll()
    {
        var actions = _items.Select(x => x.Action).ToList();
        _items.Clear();
        return actions;
    }
}
