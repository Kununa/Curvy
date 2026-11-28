using Curvy.App.Components;
using Curvy.App.Services;
using Curvy.App.Tray;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Velopack;
using static Microsoft.UI.Reactor.Factories;

// Velopack first: an install/uninstall hook run is handled here and exits (no-op otherwise).
VelopackApp.Build()
    .OnBeforeUninstallFastCallback(_ => Installer.CleanUpBeforeUninstall())
    .Run();

// One Curvy per session: a second launch hands its activation to the running one and exits.
if (!SingleInstance.TryClaim()) return;

// "--tray" (Start with Windows) starts with no window; the tray icon opens it.
Curvy.App.App.StartInTray = SingleInstance.IsTrayLaunch(Environment.GetCommandLineArgs().Skip(1));

// Tray app: closing the window only hides it; the process lives until "Exit".
ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
// Sizes are DIPs (they scale with display scaling). 1120 x 820, but never larger than 95% of the
// primary work area: at 150% a 1080p screen only has about 1280 x 690 DIPs.
var (startWidth, startHeight) = Curvy.App.App.StartSize(1120, 820);
ReactorApp.Run<Curvy.App.App>(new WindowSpec
{
    Title = "Curvy",
    Width = startWidth,
    Height = startHeight,
    MinWidth = 640,
    MinHeight = 480,
    Icon = WindowIcon.FromPath(Path.Combine(AppContext.BaseDirectory, "Assets", "curvy.ico")),
    ActivateOnOpen = !Curvy.App.App.StartInTray,
});

namespace Curvy.App
{
    class App : EngineComponent
    {
        /// <summary>Set by the entry point before <c>Run</c>: start hidden in the tray.</summary>
        public static bool StartInTray { get; set; }

        /// <summary>The wanted start size in DIPs, shrunk to fit the primary display's work area.</summary>
        public static (double Width, double Height) StartSize(double width, double height)
        {
            try
            {
                var area = ReactorDisplay.Primary.WorkAreaDip;
                return (Math.Min(width, Math.Floor(area.Width * 0.95)), Math.Min(height, Math.Floor(area.Height * 0.95)));
            }
            catch
            {
                return (width, height); // no display info: the plain size
            }
        }

        public override Element Render()
        {
            var state = UseEngine();
            var host = EngineHost.Instance;
            var window = UseWindow();

            void OpenWindow()
            {
                window?.Show();
                window?.Activate();
            }

            // Closing the window hides it instead (tray app).
            UseEffect(() =>
            {
                if (window is null) return () => { };
                EventHandler<WindowClosingEventArgs> onClosing = (_, e) =>
                {
                    if (e.Reason != WindowCloseReason.UserClosed) return;
                    e.Cancel = true;
                    window.Hide();
                };
                window.Closing += onClosing;
                return () => window.Closing -= onClosing;
            }, window);

            // --tray: make sure the window stays hidden (it was opened without activation).
            UseEffect(() =>
            {
                if (StartInTray && window is { IsVisible: true }) window.Hide();
                return () => { };
            }, window);

            // A second launch was redirected here: show the window unless it was another --tray launch.
            UseEffect(() =>
            {
                if (window is null) return () => { };
                SingleInstance.SetHandler(trayOnly =>
                {
                    if (!trayOnly) host.Post(OpenWindow);
                });
                return () => SingleInstance.SetHandler(null);
            }, window);

            // Updates: one background check per start (installed copies only; see Updater).
            UseEffect(() =>
            {
                Updater.Check(host);
                return () => { };
            }, true);

            // The first-run dialog lives in the window, so show it even after a --tray start.
            var firstRunPending = state.FirstRunLines is not null;
            UseEffect(() =>
            {
                if (firstRunPending) OpenWindow();
                return () => { };
            }, firstRunPending, window);

            void Exit()
            {
                host.Flush(); // write any pending (debounced) edit before leaving
                host.Shutdown(); // stop watching config.txt
                ReactorApp.Exit();
            }

            var tray = Component<TrayMenu, TrayMenuProps>(new TrayMenuProps(
                state.Profiles.Select(p => (p.Id, p.Name)).ToList(),
                state.ActiveProfile?.Id,
                state.Settings.MasterEnabled,
                state.Settings.IntegrationPaused,
                id => { if (state.Ready) host.Now(e => e.Activate(id)); },
                on =>
                {
                    if (!state.Ready) return;
                    if (state.Settings.IntegrationPaused) OpenWindow(); // the Reconnect bar is there
                    else host.Now(e => e.SetMasterEnabled(on));
                },
                OpenWindow,
                Exit,
                state.UpdateReady,
                () => Updater.ApplyAndRestart(host)));

            var firstRun = state.FirstRunLines is { } lines
                ? Component<FirstRunDialog, FirstRunDialogProps>(new FirstRunDialogProps(lines, host.CompleteFirstRun))
                : null;

            return FlexColumn(
                    tray,
                    firstRun,
                    TitleBar("Curvy").Flex(shrink: 0),
                    Component<Shell>().Flex(grow: 1, basis: 0))
                .Backdrop(BackdropKind.Mica);
        }
    }
}
