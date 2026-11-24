using System.Collections.Immutable;
using Curvy.App.Components;
using Curvy.App.Services;
using Curvy.App.Tray;
using Curvy.Core.Chain;
using Curvy.Core.Filters;
using Curvy.Core.Model;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// Tray app: closing the window only hides it; the process lives until "Exit".
ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
ReactorApp.Run<Curvy.App.App>(new WindowSpec
{
    Title = "Curvy",
    Width = 960,
    Height = 600,
    MinWidth = 480,
    MinHeight = 320,
    Icon = WindowIcon.FromPath(Path.Combine(AppContext.BaseDirectory, "Assets", "curvy.ico")),
});

namespace Curvy.App
{
    class App : Component
    {
        // Milestone slice: one hard-coded profile with two preference bands.
        static readonly Profile DemoProfile = Profile.New("Demo") with
        {
            Preference = Preference.Empty with
            {
                Bands = ImmutableList.Create(
                    new Filter(FilterType.Peaking, 100, 4, 0.9),
                    new Filter(FilterType.Peaking, 3000, -3, 1.4)),
            },
        };

        public override Element Render()
        {
            var (profile, updateProfile) = UseReducer(DemoProfile);
            var (activeId, setActiveId) = UseState<Guid?>(DemoProfile.Id);
            var (masterEnabled, setMasterEnabled) = UseState(true);
            var writer = UseMemo(() => new DebouncedOutputWriter(TimeSpan.FromMilliseconds(150)), []);
            var window = UseWindow();

            var settings = CurvySettings.Default with { MasterEnabled = masterEnabled, ActiveProfileId = activeId };
            var active = activeId == profile.Id ? profile : null;
            var chain = EqChain.Build(active, settings);

            // Live write (debounced). Also runs once on mount, writing the initial chain.
            UseEffect(() => writer.Schedule(chain), profile, activeId, masterEnabled);

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
                writer.Flush();
                writer.Dispose();
                ReactorApp.Exit();
            }

            void OnBandChanged(int index, Filter band) =>
                updateProfile(p => p with
                {
                    Preference = p.Preference with
                    {
                        Bands = p.Preference.Bands.SetItem(index, band with { Q = Math.Max(0.1, band.Q) }),
                    },
                    Modified = DateTimeOffset.Now,
                });

            var series = new List<GraphSeries>
            {
                new("Preference", EqChain.Layer(profile, settings, ChainLayer.ProfilePreference).ToList(),
                    GraphSeriesStyle.ProfilePreference),
                new("Total", chain.Filters, GraphSeriesStyle.Total),
            };

            var tray = Component<TrayMenu, TrayMenuProps>(new TrayMenuProps(
                new[] { (profile.Id, profile.Name) },
                activeId,
                masterEnabled,
                setActiveId,
                setMasterEnabled,
                OpenWindow,
                Exit));

            var graph = Component<EqGraph, EqGraphProps>(new EqGraphProps(
                series, profile.Preference.Bands, OnBandChanged));

            return FlexColumn(
                    tray,
                    TitleBar("Curvy").Flex(shrink: 0),
                    Caption(masterEnabled
                            ? active is null ? "No profile active" : $"Profile: {active.Name}  ·  preamp {chain.PreampDb:0.0} dB"
                            : "EQ off (bypassed)")
                        .Margin(16, 4).Flex(shrink: 0),
                    graph.Flex(grow: 1, basis: 0).Margin(8))
                .Backdrop(BackdropKind.Mica);
        }
    }
}
