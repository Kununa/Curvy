using Curvy.Core.Engine;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
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
/// Non-visual component owning the system-tray icon. Left click opens/focuses the main window;
/// right click shows a native popup menu (<see cref="NativeMenu"/>), which closes on any outside
/// click like every other tray menu.
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
            EventHandler rightClick = (_, _) => NativeMenu.Show(BuildMenu(latest.Current));
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

    static List<NativeMenuItem> BuildMenu(TrayMenuProps p)
    {
        // '&' marks an access key in native menus; a profile named "Bass & Treble" must show it literally.
        static string Text(string s) => s.Replace("&", "&&");

        var items = new List<NativeMenuItem>
        {
            new("No profile", () => p.OnActivate(null), Checked: p.ActiveId is null),
        };
        if (p.Profiles.Count > 0) items.Add(NativeMenuItem.Separator);
        foreach (var (id, name) in p.Profiles)
            items.Add(new(Text(name), () => p.OnActivate(id), Checked: p.ActiveId == id));
        items.Add(NativeMenuItem.Separator);
        // While disconnected (after a restore) the EQ toggle can't help: open the window (Reconnect) instead.
        items.Add(p.Disconnected
            ? new("EQ disconnected…", p.OnOpen)
            : new("EQ on", () => p.OnMasterToggle(!p.MasterEnabled), Checked: p.MasterEnabled));
        items.Add(NativeMenuItem.Separator);
        items.Add(new("Open Curvy", p.OnOpen));
        items.Add(new("Exit", p.OnExit));
        return items;
    }
}
