using Curvy.App.Services;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Curvy.App.Components;

public abstract record Route
{
    /// <summary>Profiles page, optionally with a profile selected.</summary>
    public sealed record Profiles(Guid? Select = null) : Route;
    public sealed record Universal : Route;
    /// <summary>What Curvy currently writes to Equalizer APO (curvy.txt).</summary>
    public sealed record Output : Route;
    public sealed record Settings : Route;
}

/// <summary>
/// Main window content: NavigationView (Profiles, Universal preference, Output, Settings) with
/// status InfoBars above the page. When Equalizer APO is unavailable a blocking InfoBar is shown
/// and no page touches the engine.
/// </summary>
public sealed class Shell : EngineComponent
{
    const string ProfilesTag = "profiles", UniversalTag = "universal", OutputTag = "output";

    static string RouteToTag(Route r) => r switch
    {
        Route.Universal => UniversalTag,
        Route.Output => OutputTag,
        Route.Settings => NavigationViewElement.SettingsTag,
        _ => ProfilesTag,
    };

    static Route TagToRoute(string? tag) => tag switch
    {
        UniversalTag => new Route.Universal(),
        OutputTag => new Route.Output(),
        NavigationViewElement.SettingsTag or null => new Route.Settings(),
        _ => new Route.Profiles(),
    };

    public override Element Render()
    {
        var state = UseEngine();
        var nav = UseNavigation<Route>(new Route.Profiles());
        var host = EngineHost.Instance;

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
                Route.Output => Component<OutputPage, OutputPageProps>(new OutputPageProps(state)),
                Route.Settings => Component<SettingsPage, SettingsPageProps>(new SettingsPageProps(state)),
                Route.Profiles p => Component<ProfilesPage, ProfilesPageProps>(
                        new ProfilesPageProps(state, p.Select))
                    .WithKey($"profiles:{p.Select}"),
                _ => Component<ProfilesPage, ProfilesPageProps>(new ProfilesPageProps(state, null)),
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
        if (state.Ready && state.Settings.IntegrationPaused)
            bars.Add(InfoBar("Curvy is disconnected from Equalizer APO",
                    "config.txt is back to its original, so your EQ isn't applied.")
                with
                {
                    IsOpen = true,
                    IsClosable = false,
                    Severity = InfoBarSeverity.Warning,
                    ActionButtonContent = "Reconnect",
                    OnActionButtonClick = host.Reconnect,
                });
        if (state.IncludeRemovedRepeatedly)
            bars.Add(InfoBar("Another app keeps removing Curvy's line from config.txt.",
                    "Curvy stopped adding it back automatically, so your EQ isn't applied.")
                with
                {
                    IsOpen = true,
                    IsClosable = false,
                    Severity = InfoBarSeverity.Warning,
                    ActionButtonContent = "Add it back",
                    OnActionButtonClick = host.AddIncludeBack,
                });
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
                    // Second action: keep the line on and stop warning about it.
                    Content = Button("Keep it on", host.KeepReEnabledLines).Margin(0, 0, 0, 8),
                    OnClosed = host.DismissReEnabledLines,
                });
        if (state.UpdateReady is { } updateVersion)
            bars.Add(InfoBar($"Curvy {updateVersion} is ready", "Restart Curvy to finish updating.")
                with
                {
                    IsOpen = true,
                    IsClosable = false,
                    Severity = InfoBarSeverity.Success,
                    ActionButtonContent = "Restart to update",
                    OnActionButtonClick = () => Updater.ApplyAndRestart(host),
                });
        if (state.UiError is { } uiError)
            bars.Add(InfoBar("Something went wrong", uiError)
                with { IsOpen = true, IsClosable = true, Severity = InfoBarSeverity.Error, OnClosed = host.DismissUiError });

        // Global EQ on/off (same as the tray's "EQ on/off"): a footer row in the navigation pane,
        // laid out like a nav item (icon column, label, switch at the right edge).
        var canToggle = state.Ready && !state.Settings.IntegrationPaused;
        var masterSwitch = (FlexRow(
                TextBlock("").FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets").FontSize(16)
                    .Width(16).VAlign(VerticalAlignment.Center).Flex(shrink: 0),
                TextBlock("Equalizer").VAlign(VerticalAlignment.Center)
                    .TextTrimming(TextTrimming.CharacterEllipsis).Flex(grow: 1, basis: 0),
                ToggleSwitch(state.Ready && state.Settings.MasterEnabled,
                        on => { if (canToggle) host.Now(e => e.SetMasterEnabled(on)); }, "", "")
                    .MinWidth(0)
                    .IsEnabled(canToggle)
                    .AutomationName("Equalizer on or off")
                    .VAlign(VerticalAlignment.Center)
                    .Flex(shrink: 0)) with { ColumnGap = 16 })
            .Margin(16, 4, 8, 4);

        var content = FlexColumn(
            VStack(8, bars.ToArray()).Margin(16, bars.Count > 0 ? 8 : 0, 16, 0).Flex(shrink: 0),
            NavigationHost(nav, Page).Flex(grow: 1, basis: 0));

        return NavigationView(
            [
                NavItem("Profiles", icon: "Audio", tag: ProfilesTag),
                NavItem("Universal preference", icon: "Globe", tag: UniversalTag),
                NavItem("Output", icon: "Document", tag: OutputTag),
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
            PaneFooter = masterSwitch,
            IsSettingsVisible = true,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
        };
    }

    static Element Placeholder(string title, string body) =>
        VStack(16, Title(title), Body(body)).Padding(24, 16, 24, 16);
}
