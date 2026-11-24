using System.Diagnostics;
using Curvy.Core.Apo;
using Curvy.Core.Chain;
using Curvy.Core.Engine;
using Curvy.Core.Model;
using Curvy.Core.Storage;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;

namespace Curvy.App.Services;

/// <summary>
/// Immutable view of the engine for rendering. <c>Ready</c> is false when APO is unavailable
/// or the engine failed to start; pages must then stay inert. A new instance is created on every engine
/// change, so Reactor's external-store comparison re-renders exactly when something changed.
/// </summary>
public sealed record EngineState(
    bool ApoAvailable,
    bool Ready,
    string? TriedPath,
    IReadOnlyList<Profile> Profiles,
    CurvySettings Settings,
    Profile? ActiveProfile,
    ChainResult Current,
    string? WriteError,
    string? StartupError,
    string? UiError);

/// <summary>
/// Process-wide owner of the <see cref="CurvyEngine"/>. Everything runs on the UI thread
/// (Ruling E): engine calls are synchronous and fast (small JSON/text writes), and
/// <see cref="Debounced"/> coalesces bursts (slider drags, graph drags) with a 150 ms
/// <see cref="DispatcherQueueTimer"/>. Engine exceptions are caught and surfaced as
/// <see cref="EngineState.UiError"/> so a failing handler never crashes the app.
/// </summary>
public sealed class EngineHost
{
    public static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(150);

    static EngineHost? _instance;

    /// <summary>The singleton. First access must happen on the UI thread.</summary>
    public static EngineHost Instance => _instance ??= new EngineHost();

    readonly CurvyEngine? _engine;
    readonly DispatcherQueue _dispatcher;
    readonly DispatcherQueueTimer _timer;
    readonly KeyedPending _pending = new();
    string? _startupError;
    string? _uiError;

    EngineHost()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread()
                      ?? throw new InvalidOperationException("EngineHost must be created on the UI thread.");
        _timer = _dispatcher.CreateTimer();
        _timer.Interval = DebounceDelay;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Flush();

        TriedPath = ApoLocator.FindConfigDirectory();
        ApoAvailable = TriedPath is not null && new ApoConfig(TriedPath).IsWritable();
        if (ApoAvailable)
        {
            try
            {
                _engine = new CurvyEngine(
                    new ProfileStore(CurvyPaths.ProfilesDir),
                    new SettingsStore(CurvyPaths.Root),
                    new ApoConfig(TriedPath!));
                _engine.Changed += OnEngineChanged;
                // Startup: make sure config.txt includes us and curvy.txt reflects the saved state.
                _engine.Apply();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Curvy: engine startup failed: {ex}");
                _startupError = $"Curvy could not start its engine: {ex.Message}";
            }
        }
        State = Snapshot();
    }

    /// <summary>False when no APO config folder was found or it isn't writable.</summary>
    public bool ApoAvailable { get; }

    /// <summary>The APO config folder that was found (or null if none).</summary>
    public string? TriedPath { get; }

    public CurvyEngine Engine =>
        _engine ?? throw new InvalidOperationException("Equalizer APO is not available.");

    /// <summary>Latest snapshot; replaced on every change.</summary>
    public EngineState State { get; private set; }

    /// <summary>Raised on the UI thread after the engine (or the UI error) changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Debounce key for edits to one profile.</summary>
    public static string ProfileKey(Guid id) => $"profile:{id}";

    /// <summary>Debounce key for the universal preference (Task 13).</summary>
    public const string UniversalKey = "universal";

    /// <summary>Debounce key for settings edits (Task 13).</summary>
    public const string SettingsKey = "settings";

    /// <summary>
    /// Schedules <paramref name="mutate"/> under <paramref name="key"/>: one pending action per
    /// key (a later call with the same key replaces it), one shared 150 ms timer restarted on
    /// every call. When the timer fires, all pending actions run in first-insertion order, so
    /// edits to different things (two profiles, a profile and the universal preference) are
    /// never dropped.
    /// </summary>
    public void Debounced(string key, Action mutate)
    {
        _pending.Set(key, mutate);
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>True while a debounced action for <paramref name="key"/> is waiting to run.</summary>
    public bool HasPending(string key) => _pending.Contains(key);

    /// <summary>
    /// Runs every pending debounced action now (before delete/activate/navigation/exit). Each runs
    /// in its own try/catch, so one failure doesn't drop the others.
    /// </summary>
    public void Flush()
    {
        _timer.Stop();
        foreach (var action in _pending.TakeAll()) Run(action);
    }

    /// <summary>Runs an engine call immediately, flushing pending edits first; never throws.</summary>
    public void Now(Action<CurvyEngine> action)
    {
        Flush();
        Run(() => action(Engine));
    }

    public void DismissUiError()
    {
        if (_uiError is null) return;
        _uiError = null;
        Publish();
    }

    void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Curvy: engine call failed: {ex}");
            _uiError = ex.Message;
            Publish();
        }
    }

    void OnEngineChanged(object? sender, EventArgs e)
    {
        if (_dispatcher.HasThreadAccess) Publish();
        else _dispatcher.TryEnqueue(Publish);
    }

    void Publish()
    {
        State = Snapshot();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    EngineState Snapshot() => _engine is { } e
        ? new EngineState(true, true, TriedPath, e.Profiles, e.Settings, e.ActiveProfile, e.Current, e.LastError, _startupError, _uiError)
        : new EngineState(ApoAvailable, false, TriedPath, [], CurvySettings.Default, null,
            new ChainResult(true, 0, []), null, _startupError, _uiError);
}

/// <summary>The <c>UseEngine()</c> hook: current <see cref="EngineState"/>, re-rendering on every change.</summary>
public static class EngineHooks
{
    // Stable subscribe delegate (Reactor re-subscribes when the delegate identity changes).
    internal static readonly Func<Action, Action> Subscribe = onChange =>
    {
        EventHandler h = (_, _) => onChange();
        EngineHost.Instance.Changed += h;
        return () => EngineHost.Instance.Changed -= h;
    };

    internal static readonly Func<EngineState> GetSnapshot = () => EngineHost.Instance.State;

    // Each change publishes a new snapshot instance, so identity is the right comparison.
    internal static readonly IEqualityComparer<EngineState> Comparer = ReferenceEqualityComparer.Instance;

    /// <summary>For function components (<c>Memo(ctx => ...)</c>).</summary>
    public static EngineState UseEngine(this RenderContext ctx) =>
        ctx.UseExternalStore(Subscribe, GetSnapshot, Comparer);
}

/// <summary>Base for class components that read the engine via <see cref="UseEngine"/>.</summary>
public abstract class EngineComponent : Component
{
    protected EngineState UseEngine() =>
        UseExternalStore(EngineHooks.Subscribe, EngineHooks.GetSnapshot, EngineHooks.Comparer);
}
