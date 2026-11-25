using Curvy.Core.Engine;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Curvy.App.Tray;

public record TrayMenuProps(
    IReadOnlyList<(Guid Id, string Name)> Profiles,
    Guid? ActiveId,
    bool MasterEnabled,
    bool Disconnected,
    Action<Guid?> OnActivate,
    Action<bool> OnMasterToggle,
    Action OnOpen,
    Action OnExit);

/// <summary>
/// Non-visual component owning the system-tray icon. Left click opens/focuses the main
/// window; right click shows a Reactor tray flyout acting as the context menu
/// (Reactor's tray has no native HMENU, but <see cref="ReactorTrayIcon.ShowFlyout"/>
/// light-dismisses like a menu, so no H.NotifyIcon fallback is needed).
/// </summary>
public sealed class TrayMenu : Component<TrayMenuProps>
{
    // Monochrome tray glyphs (see tools/icons): line colour follows the taskbar, dimmed while the EQ is off.
    static WindowIcon Load(string name) => WindowIcon.FromPath(Path.Combine(AppContext.BaseDirectory, "Assets", name));
    static readonly WindowIcon DarkOn = Load("tray-dark.ico"), DarkOff = Load("tray-dark-off.ico");
    static readonly WindowIcon LightOn = Load("tray-light.ico"), LightOff = Load("tray-light-off.ico");

    static WindowIcon TrayIconFor(bool eqOn) => TaskbarTheme.IsLight()
        ? (eqOn ? LightOn : LightOff)
        : (eqOn ? DarkOn : DarkOff);

    public override Element Render()
    {
        var activeName = Props.ActiveId is { } id ? Props.Profiles.FirstOrDefault(p => p.Id == id).Name : null;
        var tooltip = TrayText.Tooltip(activeName, Props.MasterEnabled, Props.Disconnected);
        var icon = TrayIconFor(Props.MasterEnabled && !Props.Disconnected);
        var tray = UseTrayIcon(new TrayIconSpec(icon, tooltip, WindowKey.Of("curvy-tray")));
        var latest = UseRef(Props);
        latest.Current = Props;

        UseEffect(() =>
        {
            if (tray is null) return () => { };
            EventHandler click = (_, _) => latest.Current.OnOpen();
            EventHandler rightClick = (_, _) => tray.ShowFlyout(BuildMenu(latest.Current, tray));
            tray.Click += click;
            tray.RightClick += rightClick;
            return () =>
            {
                tray.Click -= click;
                tray.RightClick -= rightClick;
            };
        }, tray);

        return Empty();
    }

    static Element BuildMenu(TrayMenuProps p, ReactorTrayIcon tray)
    {
        Action Close(Action a) => () =>
        {
            tray.HideFlyout();
            a();
        };
        static string Check(bool on) => on ? "✓  " : "     ";

        var items = new List<Element>
        {
            MenuItem(Check(p.ActiveId is null) + "No profile", Close(() => p.OnActivate(null))),
        };
        foreach (var (id, name) in p.Profiles)
            items.Add(MenuItem(Check(p.ActiveId == id) + name, Close(() => p.OnActivate(id))));
        items.Add(Separator());
        // While disconnected (after a restore) the EQ toggle can't help: open the window (Reconnect) instead.
        items.Add(p.Disconnected
            ? MenuItem("     EQ disconnected…", Close(p.OnOpen))
            : MenuItem(Check(p.MasterEnabled) + "EQ on/off", Close(() => p.OnMasterToggle(!p.MasterEnabled))));
        items.Add(Separator());
        items.Add(MenuItem("Open Curvy", Close(p.OnOpen)));
        items.Add(MenuItem("Exit", Close(p.OnExit)));

        return VStack(2, items.ToArray()).Padding(6).MinWidth(200);
    }

    static Element MenuItem(string label, Action onClick) =>
        Button(label, onClick)
            .HAlign(HorizontalAlignment.Stretch)
            .BorderThickness(0)
            .Set(b =>
            {
                b.HorizontalContentAlignment = HorizontalAlignment.Left;
                b.Background = null;
            });

    static Element Separator() => Rectangle().Height(1).Margin(4, 2).Opacity(0.3).Set(r =>
        r.Fill = Application.Current?.Resources is { } res
                 && res.TryGetValue("TextFillColorSecondaryBrush", out var b)
                 && b is Microsoft.UI.Xaml.Media.Brush brush
            ? brush
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray));
}
