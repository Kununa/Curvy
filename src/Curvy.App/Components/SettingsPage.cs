using System.Diagnostics;
using System.Reflection;
using Curvy.App.Services;
using Curvy.Core.Apo;
using Curvy.Core.AutoEq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Curvy.App.Components.ProfileButtons;

namespace Curvy.App.Components;

public record SettingsPageProps(EngineState State);

/// <summary>
/// Settings: Equalizer APO's config.txt (every line with an enable toggle, restore the original),
/// extra headroom, Start with Windows, the AutoEQ index, and About.
/// </summary>
public sealed class SettingsPage : Component<SettingsPageProps>
{
    private const string AutoEqUrl = "https://github.com/jaakkopasanen/AutoEq";

    private sealed record ConfigRead(IReadOnlyList<ConfigLine> Lines, string? Error);

    private static readonly string Version =
        (typeof(SettingsPage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? typeof(SettingsPage).Assembly.GetName().Version?.ToString()
         ?? "unknown").Split('+')[0];

    public override Element Render()
    {
        var host = EngineHost.Instance;
        var state = Props.State;

        // Toggles are controlled: a failed toggle leaves the model unchanged, so remount them (new
        // key) after every attempt to show the real state again.
        var (nonce, bump) = UseReducer(0);
        var (confirmRestore, setConfirmRestore) = UseState(false);
        var (restoreNote, setRestoreNote) = UseState<string?>(null);
        var (headroomDraft, setHeadroomDraft) = UseState<double?>(null);

        // Re-read config.txt whenever the engine (or the watcher) published a new snapshot.
        var config = UseMemo(() => ReadConfig(host), state.Version);
        var paused = state.Settings.IntegrationPaused;
        var startWithWindows = UseMemo(host.IsStartWithWindowsEnabled, state.Version);

        // Leaving the page: persist a pending headroom edit now.
        UseEffect(() => () => EngineHost.Instance.Flush(), true);

        // ── Equalizer APO ────────────────────────────────────────────────
        void Toggle(ConfigLine line, bool on)
        {
            bump(n => n + 1);
            if (on && line.Kind == ConfigLineKind.CurvyDisabled)
                host.Now(e => e.EnableForeignLine(line.Index, line.Text));
            else if (!on && line.Kind == ConfigLineKind.Active)
                host.Now(e => e.DisableForeignLines(new[] { line }));
        }

        Element LineRow(ConfigLine line)
        {
            var (text, isOn, canToggle, note) = line.Kind switch
            {
                ConfigLineKind.CurvyInclude => (line.Text.Trim(), true, false, "Curvy's own include — always on"),
                ConfigLineKind.CurvyDisabled => (line.Text[ConfigTxt.DisabledPrefix.Length..].Trim(), false, true,
                    "Turned off by Curvy"),
                ConfigLineKind.Comment => (line.Text.Trim(), false, false, "Comment"),
                _ => (line.Text.Trim(), true, true, (string?)null)
            };
            return HStack(12,
                    ToggleSwitch(isOn, on => Toggle(line, on), "On", "Off")
                        .IsEnabled(canToggle && !paused)
                        .AutomationName($"Enable {text}")
                        .VAlign(VerticalAlignment.Center),
                    VStack(0,
                        TextBlock(text).FontFamily("Consolas").TextWrapping(TextWrapping.Wrap).IsTextSelectionEnabled(),
                        note is null ? null : Caption(note)))
                .WithKey($"{line.Index}:{line.Text}:{nonce}");
        }

        Element linesArea = config.Error is { } readError
            ? Caption($"Couldn't read config.txt: {readError}")
            : config.Lines.All(l => l.Kind == ConfigLineKind.Blank)
                ? Caption("config.txt is empty.")
                : VStack(8, config.Lines.Where(l => l.Kind != ConfigLineKind.Blank).Select(LineRow).ToArray());

        void Restore()
        {
            // null: it failed and the error bar says why — no note then.
            setRestoreNote(host.RestoreOriginalConfig() switch
            {
                true => "config.txt was restored. Curvy is disconnected until you click Reconnect.",
                false => "There is no backup yet: Curvy hasn't changed config.txt.",
                null => null
            });
        }

        var restoreDialog = ContentDialog("Restore the original config.txt?",
                Body("config.txt will be replaced by the copy saved before Curvy first changed it. Lines Curvy " +
                     "turned off become active again. Changes made to config.txt by other apps or by hand " +
                     "since Curvy's first change will also be undone.\n\n" +
                     "Curvy will stop adding its line to config.txt until you click Reconnect, so your EQ " +
                     "isn't applied until then.").TextWrapping(TextWrapping.Wrap),
                "Restore") with
            {
                IsOpen = confirmRestore,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                OnClosed = result =>
                {
                    setConfirmRestore(false);
                    if (result == ContentDialogResult.Primary) Restore();
                }
            };

        var apoSection = Section("Equalizer APO",
            VStack(2, Caption("Configuration file"),
                // Path (wraps in the star column), then icon buttons on its right.
                Grid([GridSize.Star(), GridSize.Auto, GridSize.Auto], [GridSize.Auto],
                    TextBlock(host.Engine.ConfigTxtPath).IsTextSelectionEnabled().TextWrapping(TextWrapping.Wrap)
                        .VAlign(VerticalAlignment.Center).Grid(row: 0, column: 0),
                    IconButton(Symbol.Edit, "Open in text editor", () => OpenInEditor(host))
                        .Margin(12, 0, 0, 0).Grid(row: 0, column: 1),
                    IconButton(Symbol.Folder, "Open folder", () => OpenFolder(host))
                        .Margin(8, 0, 0, 0).Grid(row: 0, column: 2))),
            InfoBar(null,
                    "Curvy's clipping protection (preamp) only covers its own EQ. Other enabled lines are outside it.")
                with
                {
                    IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Informational
                },
            linesArea,
            HStack(12,
                Button("Restore original config.txt", () => setConfirmRestore(true)).IsEnabled(!paused),
                restoreNote is null
                    ? null
                    : Caption(restoreNote).VAlign(VerticalAlignment.Center).TextWrapping(TextWrapping.Wrap)),
            restoreDialog);

        // ── Extra headroom ───────────────────────────────────────────────
        var headroom = host.HasPending(EngineHost.SettingsKey) && headroomDraft is { } d
            ? d
            : state.Settings.ExtraHeadroomDb;
        var headroomSection = Section("Extra headroom",
            Caption("Lowers the overall level by this much on top of Curvy's automatic clipping protection."),
            HStack(Component<NumField, NumFieldProps>(new NumFieldProps("", headroom, 0, 12, 0.5, v =>
            {
                setHeadroomDraft(v);
                host.Debounced(EngineHost.SettingsKey, () => host.Engine.SetExtraHeadroom(v));
            }, 100, "Extra headroom (dB)")), Body("(dB)").VAlign(VerticalAlignment.Center))
        );

        // ── Start with Windows ───────────────────────────────────────────
        var startupSection = Section("Start with Windows",
            ToggleSwitch(startWithWindows, on =>
                {
                    bump(n => n + 1);
                    host.SetStartWithWindows(on);
                }, "On", "Off", "Start Curvy in the tray when you sign in")
                .WithKey($"startup:{nonce}"));

        // ── AutoEQ ───────────────────────────────────────────────────────
        var autoEqSection = Section("AutoEQ", Component<AutoEqIndexSettings>());

        // ── About ────────────────────────────────────────────────────────
        var aboutSection = Section("About",
            Body($"Curvy {Version}"),
            UpdateRow(host, state.Update),
            Body("Headphone corrections from AutoEq by Jaakko Pasanen (MIT license)").TextWrapping(TextWrapping.Wrap),
            HyperlinkButton(AutoEqUrl, new Uri(AutoEqUrl)));

        return ScrollView(
            VStack(16,
                    Subtitle("Settings"),
                    apoSection,
                    headroomSection,
                    startupSection,
                    autoEqSection,
                    aboutSection)
                .Padding(24, 16, 24, 24));
    }

    /// <summary>The update check's state, with "Restart to update" or "Check for updates" next to it.</summary>
    private static Element UpdateRow(EngineHost host, UpdateStatus? update)
    {
        var (text, button) = update switch
        {
            null or { Kind: UpdateKind.NotInstalled } => ("Updates are available in the installed app.", null),
            { Kind: UpdateKind.Checking } => ("Checking for updates…", null),
            { Kind: UpdateKind.Downloading, Version: var v } => ($"Downloading Curvy {v}…", null),
            { Kind: UpdateKind.Ready, Version: var v } => ($"Curvy {v} is ready. Restart Curvy to finish updating.",
                Button("Restart to update", () => Updater.ApplyAndRestart(host))),
            { Kind: UpdateKind.Failed, Error: var error } => ($"Couldn't check for updates: {error}",
                Button("Try again", () => Updater.Check(host))),
            _ => ("Curvy is up to date.", (Element?)Button("Check for updates", () => Updater.Check(host))),
        };
        // Text wraps in the star column; the button stays on its right.
        var caption = Caption(text).VAlign(VerticalAlignment.Center).TextWrapping(TextWrapping.Wrap)
            .Grid(row: 0, column: 0);
        return button is null
            ? caption
            : Grid([GridSize.Star(), GridSize.Auto], [GridSize.Auto],
                caption,
                button.Margin(12, 0, 0, 0).VAlign(VerticalAlignment.Center).Grid(row: 0, column: 1));
    }

    private static Element Section(string title, params Element?[] children)
    {
        return Card(VStack(12, new Element?[] { BodyStrong(title) }.Concat(children).ToArray()));
    }

    /// <summary>Opens config.txt with whatever app Windows uses for .txt files.</summary>
    private static void OpenInEditor(EngineHost host)
    {
        try
        {
            Process.Start(new ProcessStartInfo(host.Engine.ConfigTxtPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            host.ReportError($"Couldn't open config.txt: {ex.Message}");
        }
    }

    /// <summary>Opens the config folder in Explorer with config.txt selected.</summary>
    private static void OpenFolder(EngineHost host)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{host.Engine.ConfigTxtPath}\"")
                { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            host.ReportError($"Couldn't open the config folder: {ex.Message}");
        }
    }

    private static ConfigRead ReadConfig(EngineHost host)
    {
        try
        {
            return new ConfigRead(host.Engine.ReadConfigLines(), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ConfigRead([], ex.Message);
        }
    }
}

/// <summary>Settings → AutoEQ: when the index was downloaded, and a Refresh button.</summary>
public sealed class AutoEqIndexSettings : Component
{
    private sealed record Note(InfoBarSeverity Severity, string Message);

    /// <summary>
    /// Shown when a forced refresh fell back to the cached index (the client serves the stale
    /// cache instead of failing; detected by <see cref="AutoEqClient.IndexFetchedAt"/> not changing).
    /// </summary>
    private static string StaleRefreshMessage(DateTimeOffset? fetchedAt) => fetchedAt is { } at
        ? $"Couldn't refresh; showing the index from {at.LocalDateTime:d}."
        : "Couldn't refresh; showing the cached index.";


    public override Element Render()
    {
        var host = EngineHost.Instance;
        var initial = UseRef<DateTimeOffset?>(null);
        var read = UseRef(false);
        if (!read.Current)
        {
            initial.Current = host.AutoEq.IndexFetchedAt; // reads a small file: once per mount
            read.Current = true;
        }

        var (fetchedAt, setFetchedAt) = UseState(initial.Current);
        var (busy, setBusy) = UseState(false);
        var (note, setNote) = UseState<Note?>(null);
        var life = UseRef<CancellationTokenSource?>(null);

        UseEffect(() =>
        {
            var cts = life.Current = new CancellationTokenSource();
            return () => cts.Cancel();
        }, true);

        async Task RefreshAsync(CancellationToken ct)
        {
            var before = host.AutoEq.IndexFetchedAt;
            setBusy(true);
            setNote(null);
            try
            {
                await host.AutoEqIndex.LoadAsync(true).WaitAsync(ct);
                host.Post(() =>
                {
                    host.AutoEqCorrections.Clear();
                    if (ct.IsCancellationRequested) return;

                    var after = host.AutoEq.IndexFetchedAt;
                    setFetchedAt(after);
                    setBusy(false);
                    setNote(after == before
                        ? new Note(InfoBarSeverity.Warning, StaleRefreshMessage(after))
                        : new Note(InfoBarSeverity.Success, "The AutoEQ index is up to date."));
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Page went away; the shared load keeps running and fills the cache.
            }
            catch (AutoEqUnavailableException ex)
            {
                host.Post(() =>
                {
                    if (ct.IsCancellationRequested) return;

                    setBusy(false);
                    setNote(new Note(InfoBarSeverity.Warning, $"Couldn't refresh: {ex.Message}"));
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Curvy: AutoEQ index refresh failed: {ex}");
                host.ReportError($"Couldn't refresh the AutoEQ index: {ex.Message}");
                host.Post(() =>
                {
                    if (!ct.IsCancellationRequested) setBusy(false);
                });
            }
        }

        return VStack(8,
            HStack(12,
                Body(fetchedAt is { } at ? $"Index downloaded {at.LocalDateTime:g}" : "Index not downloaded yet")
                    .VAlign(VerticalAlignment.Center),
                busy ? ProgressRing().Width(16).Height(16).VAlign(VerticalAlignment.Center) : null,
                Button("Refresh", () => _ = RefreshAsync(life.Current?.Token ?? CancellationToken.None))
                    .IsEnabled(!busy)
                    .AutomationName("Refresh AutoEQ index")),
            note is { } n
                ? InfoBar(null, n.Message) with
                {
                    IsOpen = true, IsClosable = true, Severity = n.Severity, OnClosed = () => setNote(null)
                }
                : null);
    }
}