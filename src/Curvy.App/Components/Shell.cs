using Curvy.App.Services;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Curvy.App.Components;

public abstract record Route
{
    /// <summary>Profiles page, optionally with a profile selected (e.g. after AutoEQ created it).</summary>
    public sealed record Profiles(Guid? Select = null) : Route;
    public sealed record Universal : Route;
    /// <summary>
    /// AutoEQ browser, optionally pre-filtered (e.g. "Change source" passes the headphone). With
    /// <paramref name="ReplaceForProfileId"/> it swaps that profile's correction instead of creating one.
    /// </summary>
    public sealed record AutoEq(string? Query = null, Guid? ReplaceForProfileId = null) : Route;
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

        void OpenAutoEq(string? query, Guid? replaceForProfileId)
        {
            host.Flush(); // persist pending edits before leaving the page
            nav.Navigate(new Route.AutoEq(query, replaceForProfileId));
        }

        void OpenProfile(Guid id)
        {
            host.Flush();
            nav.Navigate(new Route.Profiles(id));
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
                Route.Universal => Component<UniversalPage, UniversalPageProps>(new UniversalPageProps(state)),
                // Keyed by the route so a different query / target remounts with fresh state.
                Route.AutoEq a => Component<AutoEqPage, AutoEqPageProps>(
                        new AutoEqPageProps(a.Query, a.ReplaceForProfileId, state, OpenProfile))
                    .WithKey($"autoeq:{a.Query}:{a.ReplaceForProfileId}"),
                Route.Settings => Component<SettingsPage, SettingsPageProps>(new SettingsPageProps(state)),
                Route.Profiles p => Component<ProfilesPage, ProfilesPageProps>(
                        new ProfilesPageProps(state, OpenAutoEq, p.Select))
                    .WithKey($"profiles:{p.Select}"),
                _ => Component<ProfilesPage, ProfilesPageProps>(new ProfilesPageProps(state, OpenAutoEq, null)),
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
        if (state.ReEnabledLines.Count > 0)
            bars.Add(InfoBar("Another app re-enabled: " + string.Join(", ", state.ReEnabledLines.Select(l => l.Text.Trim()))
                        + ". Effects may stack.", null)
                with
                {
                    IsOpen = true,
                    IsClosable = true,
                    Severity = InfoBarSeverity.Warning,
                    ActionButtonContent = "Disable again",
                    OnActionButtonClick = host.DisableReEnabledLines,
                    OnClosed = host.DismissReEnabledLines,
                });
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
