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

namespace Curvy.App.Components;

public record SettingsPageProps(EngineState State);

/// <summary>
/// Settings: Equalizer APO's config.txt (every line with an enable toggle, restore the original),
/// extra headroom, Start with Windows, the AutoEQ index, and About.
/// </summary>
public sealed class SettingsPage : Component<SettingsPageProps>
{
    const string AutoEqUrl = "https://github.com/jaakkopasanen/AutoEq";

    sealed record ConfigRead(IReadOnlyList<ConfigLine> Lines, string? Error);

    static readonly string Version =
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
                host.Now(e => e.DisableForeignLines(new[] { line.Index }));
        }

        Element LineRow(ConfigLine line)
        {
            var (text, isOn, canToggle, note) = line.Kind switch
            {
                ConfigLineKind.CurvyInclude => (line.Text.Trim(), true, false, "Curvy's own include — always on"),
                ConfigLineKind.CurvyDisabled => (line.Text[ConfigTxt.DisabledPrefix.Length..].Trim(), false, true, "Turned off by Curvy"),
                ConfigLineKind.Comment => (line.Text.Trim(), false, false, "Comment"),
                _ => (line.Text.Trim(), true, true, (string?)null),
            };
            return HStack(12,
                    ToggleSwitch(isOn, on => Toggle(line, on), "On", "Off")
                        .IsEnabled(canToggle)
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
            var restored = false;
            host.Now(e => restored = e.RestoreOriginalConfig());
            setRestoreNote(restored
                ? "config.txt was restored. Curvy added its own include line back, because it is still running."
                : "There is no backup yet: Curvy hasn't changed config.txt.");
        }

        var restoreDialog = ContentDialog("Restore the original config.txt?",
                Body("config.txt will be replaced by the copy saved before Curvy first changed it. Lines Curvy " +
                     "turned off become active again, so their EQ can stack with Curvy's.\n\n" +
                     "Curvy adds its own line (Include: Curvy\\curvy.txt) back right away, because it needs it " +
                     "while it runs, and again whenever it starts. Unless you also close Curvy (tray menu → Exit) " +
                     "and remove that line, Curvy stays in config.txt.").TextWrapping(TextWrapping.Wrap),
                "Restore") with
            {
                IsOpen = confirmRestore,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                OnClosed = result =>
                {
                    setConfirmRestore(false);
                    if (result == ContentDialogResult.Primary) Restore();
                },
            };

        var apoSection = Section("Equalizer APO",
            VStack(2, Caption("Configuration file"),
                TextBlock(host.Engine.ConfigTxtPath).IsTextSelectionEnabled().TextWrapping(TextWrapping.Wrap)),
            InfoBar(null, "Curvy's clipping protection (preamp) only covers its own EQ. Other enabled lines are outside it.")
                with { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Informational },
            linesArea,
            HStack(12,
                Button("Restore original config.txt", () => setConfirmRestore(true)),
                restoreNote is null ? null : Caption(restoreNote).VAlign(VerticalAlignment.Center).TextWrapping(TextWrapping.Wrap)),
            restoreDialog);

        // ── Extra headroom ───────────────────────────────────────────────
        var headroom = host.HasPending(EngineHost.SettingsKey) && headroomDraft is { } d
            ? d
            : state.Settings.ExtraHeadroomDb;
        var headroomSection = Section("Extra headroom",
            Caption("Lowers the overall level by this much on top of Curvy's automatic clipping protection."),
            Component<NumField, NumFieldProps>(new NumFieldProps("Extra headroom (dB)", headroom, 0, 12, 0.5, v =>
            {
                setHeadroomDraft(v);
                host.Debounced(EngineHost.SettingsKey, () => host.Engine.SetExtraHeadroom(v));
            }, 200)));

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

    static Element Section(string title, params Element?[] children) =>
        Card(VStack(12, new Element?[] { BodyStrong(title) }.Concat(children).ToArray()));

    static ConfigRead ReadConfig(EngineHost host)
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
    sealed record Note(InfoBarSeverity Severity, string Message);

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
                await host.AutoEqIndex.LoadAsync(forceRefresh: true).WaitAsync(ct);
                host.Post(() =>
                {
                    host.AutoEqCorrections.Clear();
                    if (ct.IsCancellationRequested) return;
                    var after = host.AutoEq.IndexFetchedAt;
                    setFetchedAt(after);
                    setBusy(false);
                    setNote(after == before
                        ? new Note(InfoBarSeverity.Warning, AutoEqPage.StaleRefreshMessage(after))
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
                host.Post(() => { if (!ct.IsCancellationRequested) setBusy(false); });
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
                    IsOpen = true, IsClosable = true, Severity = n.Severity, OnClosed = () => setNote(null),
                }
                : null);
    }
}
