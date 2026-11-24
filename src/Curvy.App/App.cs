using Curvy.App.Components;
using Curvy.App.Services;
using Curvy.App.Tray;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

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
});

namespace Curvy.App
{
    class App : EngineComponent
    {
        public override Element Render()
        {
            var state = UseEngine();
            var host = EngineHost.Instance;
            var window = UseWindow();

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

            void OpenWindow()
            {
                window?.Show();
                window?.Activate();
            }

            void Exit()
            {
                host.Flush(); // write any pending (debounced) edit before leaving
                ReactorApp.Exit();
            }

            // Task 13 refines the tray; for now it reflects the engine directly.
            var tray = Component<TrayMenu, TrayMenuProps>(new TrayMenuProps(
                state.Profiles.Select(p => (p.Id, p.Name)).ToList(),
                state.Settings.ActiveProfileId,
                state.Settings.MasterEnabled,
                id => { if (state.Ready) host.Now(e => e.Activate(id)); },
                on => { if (state.Ready) host.Now(e => e.SetMasterEnabled(on)); },
                OpenWindow,
                Exit));

            return FlexColumn(
                    tray,
                    TitleBar("Curvy").Flex(shrink: 0),
                    Component<Shell>().Flex(grow: 1, basis: 0))
                .Backdrop(BackdropKind.Mica);
        }
    }
}
