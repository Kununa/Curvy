using System.Diagnostics;
using Curvy.Core.Apo;
using Curvy.Core.AutoEq;
using Curvy.Core.Chain;
using Curvy.Core.Engine;
using Curvy.Core.Model;
using Curvy.Core.Storage;
using Curvy.Core.SystemIntegration;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using Microsoft.UI.Reactor.Core;

namespace Curvy.App.Services;

/// <summary>
/// Immutable view of the engine for rendering. <c>Ready</c> is false when APO is unavailable
/// or the engine failed to start; pages must then stay inert. A new instance is created on every engine
/// change, so Reactor's external-store comparison re-renders exactly when something changed.
/// </summary>
/// <param name="ReEnabledLines">
/// config.txt lines Curvy had disabled that are active again (another app re-enabled them); shown
/// as a warning until disabled again or dismissed. Never disabled automatically.
/// </param>
/// <param name="FirstRunLines">
/// Non-null while the first-run dialog should be shown: the active EQ-like foreign lines found at startup.
/// </param>
/// <param name="IncludeRemovedRepeatedly">
/// Another app kept removing Curvy's include and automatic repair stopped (see <see cref="RepairGuard"/>);
/// the user can add it back once.
/// </param>
/// <param name="Version">Increases with every published snapshot (e.g. to re-read config.txt).</param>
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
    string? UiError,
    IReadOnlyList<ConfigLine> ReEnabledLines,
    IReadOnlyList<ConfigLine>? FirstRunLines,
    bool IncludeRemovedRepeatedly,
    long Version,
    string? UpdateReady = null);

/// <summary>
/// Process-wide owner of the <see cref="CurvyEngine"/>. Everything runs on the UI thread
/// (Ruling E): engine calls are synchronous and fast (small JSON/text writes), and
/// <see cref="Debounced"/> coalesces bursts (slider drags, graph drags) with a 150 ms
/// <see cref="DispatcherQueueTimer"/>. Engine exceptions are caught and surfaced as
/// <see cref="EngineState.UiError"/> so a failing handler never crashes the app.
/// <para>
/// A <see cref="FileSystemWatcher"/> on <c>config.txt</c> (500 ms debounce, see
/// <see cref="ConfigChangeDebouncer"/>) runs <see cref="CurvyEngine.CheckAndRepair"/> on the UI
/// thread after other apps change the file; changes within 1 s of Curvy's own config.txt writes are ignored.
/// </para>
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
    ConfigChangeDebouncer? _configDebouncer;
    FileSystemWatcher? _configWatcher;
    IReadOnlyList<ConfigLine> _reEnabled = [];
    IReadOnlyList<ConfigLine>? _firstRunLines;
    readonly RepairGuard _repairGuard = new(TimeProvider.System);
    bool _includeLoop;
    long _version;
    AutoEqClient? _autoEq;
    AutoEqIndexLoader? _autoEqIndex;
    AutoEqCorrectionCache? _autoEqCorrections;
    string? _startupError;
    string? _uiError;
    string? _updateReady;

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
                var apo = new ApoConfig(TriedPath!);
                // Created first so Curvy's own startup writes are already marked as its own.
                var debouncer = _configDebouncer = new ConfigChangeDebouncer(TimeProvider.System,
                    () => _dispatcher.TryEnqueue(CheckConfig));
                apo.ConfigTxtWriting += (_, _) => debouncer.NotifyOwnWrite();
                _engine = new CurvyEngine(
                    new ProfileStore(CurvyPaths.ProfilesDir),
                    new SettingsStore(CurvyPaths.Root),
                    apo);
                _engine.Changed += OnEngineChanged;
                // Startup: make sure config.txt includes us and curvy.txt reflects the saved state
                // (while paused after a restore, only curvy.txt is written).
                _engine.Apply();
                RunStartupChecks();
                RefreshStartupRegistration();
                StartWatcher(apo.ConfigTxtPath);
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

    /// <summary>
    /// AutoEQ access (index + ParametricEQ files), cached on disk under <c>CurvyPaths.Root\autoeq</c>.
    /// One shared <see cref="HttpClient"/> for the process; created on first use.
    /// </summary>
    public AutoEqClient AutoEq => _autoEq ??= new AutoEqClient(
        new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
        Path.Combine(CurvyPaths.Root, "autoeq"),
        TimeProvider.System);

    /// <summary>In-memory AutoEQ index; at most one index load runs at a time (Task 12 ruling).</summary>
    public AutoEqIndexLoader AutoEqIndex => _autoEqIndex ??= new AutoEqIndexLoader(AutoEq);

    /// <summary>Fetched corrections per index entry, so repeated previews don't refetch.</summary>
    public AutoEqCorrectionCache AutoEqCorrections => _autoEqCorrections ??= new AutoEqCorrectionCache(AutoEq);

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread (inline when already there). For async
    /// continuations that may resume on the thread pool. Exceptions go to the UI error bar.
    /// </summary>
    public void Post(Action action)
    {
        if (_dispatcher.HasThreadAccess) Run(action);
        else _dispatcher.TryEnqueue(() => Run(action));
    }

    /// <summary>Shows <paramref name="message"/> in the dismissable "Something went wrong" bar.</summary>
    public void ReportError(string message)
    {
        if (!_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueue(() => ReportError(message));
            return;
        }
        _uiError = message;
        Publish();
    }

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

    // ── config.txt watcher ───────────────────────────────────────────────

    void StartWatcher(string configTxtPath)
    {
        try
        {
            var w = new FileSystemWatcher(Path.GetDirectoryName(configTxtPath)!, Path.GetFileName(configTxtPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = false,
            };
            // Raised on thread-pool threads; the debouncer is thread-safe and marshals to the UI thread.
            var debouncer = _configDebouncer!;
            FileSystemEventHandler changed = (_, _) => debouncer.NotifyChanged();
            w.Changed += changed;
            w.Created += changed;
            w.Deleted += changed;
            w.Renamed += (_, _) => debouncer.NotifyChanged();
            w.Error += (_, e) =>
            {
                Debug.WriteLine($"Curvy: config.txt watcher error: {e.GetException()}");
                debouncer.NotifyChanged(); // events may have been lost (buffer overflow): re-check
            };
            w.EnableRaisingEvents = true;
            _configWatcher = w;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Debug.WriteLine($"Curvy: couldn't watch config.txt: {ex}");
        }
    }

    /// <summary>
    /// Startup (and Reconnect): re-enabled lines (warning) and, until shown once, the first-run
    /// dialog. Reports nothing while the integration is paused.
    /// </summary>
    void RunStartupChecks()
    {
        if (_engine is null) return;
        var health = _engine.CheckAndRepair();
        _reEnabled = health.ReEnabledLines;
        _includeLoop = false;
        _firstRunLines = !_engine.Settings.FirstRunShown && health.ActiveEqLikeForeignLines.Count > 0
            ? health.ActiveEqLikeForeignLines
            : null;
    }

    /// <summary>
    /// Debounced watcher callback (UI thread): re-adds Curvy's include if it was removed — at most
    /// <see cref="RepairGuard.MaxRepairs"/> times a minute, then it stops and asks the user — and
    /// reports lines Curvy had disabled that another app re-enabled. Never disables anything itself.
    /// </summary>
    void CheckConfig()
    {
        if (_engine is null || _configWatcher is null) return; // not started, or shut down
        Run(() =>
        {
            var health = _engine.CheckAndRepair(_repairGuard.TryAcquire);
            _reEnabled = health.ReEnabledLines;
            _includeLoop = health.IncludeMissing;
            Publish(); // also refreshes pages that show config.txt
        });
    }

    /// <summary>"Add it back" on the include-loop warning: one manual repair; automatic repair resumes.</summary>
    public void AddIncludeBack()
    {
        _repairGuard.Reset();
        _includeLoop = false;
        Now(e =>
        {
            var health = e.CheckAndRepair();
            _reEnabled = health.ReEnabledLines;
        });
        Publish();
    }

    /// <summary>"Disable again" on the re-enabled warning: disables those lines as they are on disk now.</summary>
    public void DisableReEnabledLines()
    {
        _reEnabled = [];
        Now(e =>
        {
            var lines = e.FindReEnabledLines();
            if (lines.Count > 0) e.DisableForeignLines(lines);
        });
        Publish();
    }

    /// <summary>"Keep it on" on the re-enabled warning: stop tracking those lines (they stay active).</summary>
    public void KeepReEnabledLines()
    {
        _reEnabled = [];
        Now(e =>
        {
            foreach (var line in e.FindReEnabledLines()) e.EnableForeignLine(line.Index, line.Text);
        });
        Publish();
    }

    /// <summary>
    /// "Restore original config.txt": restores the backup and pauses the integration. Returns
    /// true/false (restored / no backup), or null when it failed (the error bar shows why).
    /// </summary>
    public bool? RestoreOriginalConfig()
    {
        bool? restored = null;
        _reEnabled = [];
        _includeLoop = false;
        _firstRunLines = null;
        Now(e => restored = e.RestoreOriginalConfig());
        Publish();
        return restored;
    }

    /// <summary>"Reconnect": re-adds Curvy's include and runs the startup checks (incl. first run) again.</summary>
    public void Reconnect()
    {
        _repairGuard.Reset();
        Now(e =>
        {
            e.ResumeIntegration();
            RunStartupChecks();
        });
        Publish();
    }

    public void DismissReEnabledLines()
    {
        if (_reEnabled.Count == 0) return;
        _reEnabled = [];
        Publish();
    }

    /// <summary>
    /// Closes the first-run dialog: disables <paramref name="lines"/> (may be empty; checked against
    /// config.txt as it is now) and records that the dialog was shown, even if disabling failed.
    /// </summary>
    public void CompleteFirstRun(IReadOnlyCollection<ConfigLine> lines)
    {
        _firstRunLines = null;
        Now(e =>
        {
            try
            {
                if (lines.Count > 0) e.DisableForeignLines(lines);
            }
            catch (ConfigChangedException)
            {
                _uiError = "config.txt changed while the dialog was open — review the lines in Settings → Equalizer APO.";
            }
            finally
            {
                e.SetFirstRunShown(true);
            }
        });
        Publish();
    }

    // ── Start with Windows ───────────────────────────────────────────────

    static StartupRegistration Startup => new(Registry.CurrentUser);

    static string? ExePath => Environment.ProcessPath;

    /// <summary>True when the HKCU Run entry exists (the source of truth for the toggle).</summary>
    public bool IsStartWithWindowsEnabled()
    {
        try
        {
            return Startup.IsEnabled();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Debug.WriteLine($"Curvy: couldn't read the Run key: {ex}");
            return false;
        }
    }

    /// <summary>Writes or removes the HKCU Run entry and records the choice in the settings.</summary>
    public void SetStartWithWindows(bool on) => Now(e =>
    {
        if (on) Startup.Enable(ExePath ?? throw new InvalidOperationException("Can't determine Curvy's executable path."));
        else Startup.Disable();
        e.SetStartWithWindows(on);
    });

    /// <summary>When enabled, rewrites the Run entry so it follows the exe if Curvy was moved.</summary>
    void RefreshStartupRegistration()
    {
#if DEBUG
        // Never let a dev/debug build rewrite the user's autostart entry.
        return;
#else
        if (_engine is not { Settings.StartWithWindows: true } || ExePath is not { } exe) return;
        try
        {
            // Only repair the entry when the registered exe is gone (moved/reinstalled); a registered
            // exe that still exists is left alone.
            if (StartupRegistration.ShouldRefresh(Startup.GetRegisteredExe(), exe, File.Exists)) Startup.Enable(exe);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Debug.WriteLine($"Curvy: couldn't refresh the Run key: {ex}");
        }
#endif
    }

    /// <summary>Stops the config.txt watcher (on Exit).</summary>
    public void Shutdown()
    {
        _configWatcher?.Dispose();
        _configWatcher = null;
        _configDebouncer?.Dispose();
    }

    /// <summary>A downloaded update (its version) is ready: offered as "Restart to update".</summary>
    public void SetUpdateReady(string version)
    {
        _updateReady = version;
        Publish();
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
        catch (ConfigChangedException)
        {
            // Another app changed config.txt after we read it; nothing was written. Publishing a new
            // snapshot makes the Settings page re-read the file.
            _uiError = ConfigChangedMessage;
            Publish();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Curvy: engine call failed: {ex}");
            _uiError = ex.Message;
            Publish();
        }
    }

    public const string ConfigChangedMessage = "config.txt changed — please review again.";

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
        ? new EngineState(true, true, TriedPath, e.Profiles, e.Settings, e.ActiveProfile, e.Current, e.LastError,
            _startupError, _uiError, _reEnabled, _firstRunLines, _includeLoop, ++_version, _updateReady)
        : new EngineState(ApoAvailable, false, TriedPath, [], CurvySettings.Default, null,
            new ChainResult(true, 0, []), null, _startupError, _uiError, [], null, false, ++_version, _updateReady);
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
