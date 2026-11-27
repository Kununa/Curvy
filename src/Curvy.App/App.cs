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
ReactorApp.Run<Curvy.App.App>(new WindowSpec
{
    Title = "Curvy",
    Width = 1120,
    Height = 820,
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
                Updater.CheckOnStartup(host);
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
