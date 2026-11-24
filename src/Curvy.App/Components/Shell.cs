using Curvy.App.Services;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Curvy.App.Components;

public abstract record Route
{
    public sealed record Profiles : Route;
    public sealed record Universal : Route;
    /// <summary>AutoEQ browser, optionally pre-filtered (e.g. "Change source" passes the headphone).</summary>
    public sealed record AutoEq(string? Query = null) : Route;
    public sealed record Settings : Route;
}

/// <summary>
/// Main window content: NavigationView (Profiles, Universal preference, AutoEQ, Settings) with
/// status InfoBars above the page. When Equalizer APO is unavailable a blocking InfoBar is shown
/// and no page touches the engine.
/// </summary>
public sealed class Shell : EngineComponent
{
    const string ProfilesTag = "profiles", UniversalTag = "universal", AutoEqTag = "autoeq";

    static string RouteToTag(Route r) => r switch
    {
        Route.Universal => UniversalTag,
        Route.AutoEq => AutoEqTag,
        Route.Settings => NavigationViewElement.SettingsTag,
        _ => ProfilesTag,
    };

    static Route TagToRoute(string? tag) => tag switch
    {
        UniversalTag => new Route.Universal(),
        AutoEqTag => new Route.AutoEq(),
        NavigationViewElement.SettingsTag or null => new Route.Settings(),
        _ => new Route.Profiles(),
    };

    public override Element Render()
    {
        var state = UseEngine();
        var nav = UseNavigation<Route>(new Route.Profiles());
        var host = EngineHost.Instance;

        void OpenAutoEq(string? query)
        {
            host.Flush(); // persist pending edits before leaving the page
            nav.Navigate(new Route.AutoEq(query));
        }

        Element Page(Route route)
        {
            if (!state.ApoAvailable)
                return Placeholder("Curvy is inactive",
                    "Install Equalizer APO (and enable it for your output device), then restart Curvy.");
            if (!state.Ready)
                return Placeholder("Curvy is inactive",
                    state.StartupError ?? "The engine couldn't start. Restart Curvy to try again.");
            return route switch
            {
                Route.Universal => Placeholder("Universal preference",
                    "A preference applied on top of every profile. Coming soon."),
                Route.AutoEq a => Placeholder("AutoEQ",
                    a.Query is { Length: > 0 } q
                        ? $"Search AutoEQ headphone corrections. Pre-filtered to \"{q}\". Coming soon."
                        : "Search AutoEQ headphone corrections. Coming soon."),
                Route.Settings => Placeholder("Settings", "Coming soon."),
                _ => Component<ProfilesPage, ProfilesPageProps>(new ProfilesPageProps(state, OpenAutoEq)),
            };
        }

        var bars = new List<Element>();
        if (!state.ApoAvailable)
            bars.Add(InfoBar("Equalizer APO not found or its config folder isn't writable",
                    state.TriedPath is { } path
                        ? $"Tried: {path}"
                        : @"Tried: registry key HKLM\SOFTWARE\EqualizerAPO (not found)")
                with { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Error });
        if (state.WriteError is { } writeError)
            bars.Add(InfoBar("Couldn't write the Equalizer APO configuration", writeError)
                with { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Warning });
        if (state.StartupError is { } startupError)
            bars.Add(InfoBar("Curvy couldn't start", startupError)
                with { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Error });
        if (state.UiError is { } uiError)
            bars.Add(InfoBar("Something went wrong", uiError)
                with { IsOpen = true, IsClosable = true, Severity = InfoBarSeverity.Error, OnClosed = host.DismissUiError });

        var content = FlexColumn(
            VStack(8, bars.ToArray()).Margin(16, bars.Count > 0 ? 8 : 0, 16, 0).Flex(shrink: 0),
            NavigationHost(nav, Page).Flex(grow: 1, basis: 0));

        return NavigationView(
            [
                NavItem("Profiles", icon: "Audio", tag: ProfilesTag),
                NavItem("Universal preference", icon: "Globe", tag: UniversalTag),
                NavItem("AutoEQ", icon: "Find", tag: AutoEqTag),
            ],
            content) with
        {
            SelectedTag = RouteToTag(nav.CurrentRoute),
            OnSelectedTagChanged = tag =>
            {
                var target = TagToRoute(tag);
                if (RouteToTag(target) == RouteToTag(nav.CurrentRoute)) return;
                host.Flush(); // persist pending edits before leaving the page
                nav.Navigate(target);
            },
            IsSettingsVisible = true,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
        };
    }

    static Element Placeholder(string title, string body) =>
        VStack(16, Title(title), Body(body)).Padding(24, 16, 24, 16);
}
