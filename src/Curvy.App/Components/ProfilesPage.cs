using Curvy.Core.AutoEq;
using Curvy.App.Services;
using Curvy.Core.Chain;
using Curvy.Core.Filters;
using Curvy.Core.Model;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;
using static Curvy.App.Components.ProfileButtons;

namespace Curvy.App.Components;

/// <param name="InitialSelection">Profile to select on mount (falls back to the active one).</param>
public record ProfilesPageProps(EngineState State, Guid? InitialSelection = null);

/// <summary>Profile picker (combo box above the graph) and the editor for the selected profile.</summary>
public sealed class ProfilesPage : Component<ProfilesPageProps>
{
    public override Element Render()
    {
        var host = EngineHost.Instance;
        var state = Props.State;
        var profiles = state.Profiles;
        var (selectedId, setSelectedId) = UseState<Guid?>(
            Props.InitialSelection ?? state.ActiveProfile?.Id ?? profiles.FirstOrDefault()?.Id);

        // Fall back to the active (or first) profile when the selection disappeared.
        var selected = profiles.FirstOrDefault(p => p.Id == selectedId)
                       ?? state.ActiveProfile
                       ?? profiles.FirstOrDefault();
        var selectedIndex = selected is null ? -1 : profiles.ToList().FindIndex(p => p.Id == selected.Id);

        void NewBlank()
        {
            var p = Profile.New(ProfileNames.Unique(profiles, "New profile"));
            host.Now(e => e.SaveProfile(p));
            setSelectedId(p.Id);
        }

        // Profile picker: a combo box shown above the graph (no sidebar).
        var items = profiles
            .Select(p => p.Id == state.Settings.ActiveProfileId
                // Active profile: accent check mark, so it stands out in the closed box and in the
                // drop-down list.
                ? (Element)HStack(8,
                        TextBlock("").FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets")
                            .Foreground(Theme.AccentText).Width(16).VAlign(VerticalAlignment.Center),
                        TextBlock(p.Name).TextTrimming(TextTrimming.CharacterEllipsis))
                    .AutomationName($"{p.Name}, active")
                : HStack(8,
                        Border(null).Width(16),
                        TextBlock(p.Name).TextTrimming(TextTrimming.CharacterEllipsis))
                    .AutomationName(p.Name))
            .ToArray();
        var picker = HStack(8,
            ComboBox(items, selectedIndex, i =>
                {
                    if (i < 0 || i >= profiles.Count || profiles[i].Id == selected?.Id) return;
                    // Save the outgoing profile's pending edits before switching (belt and
                    // braces: debounce slots are keyed per profile, and the editor also
                    // flushes on unmount).
                    host.Flush();
                    setSelectedId(profiles[i].Id);
                })
                .Width(300)
                .AutomationName("Profile"),
            IconButton(Symbol.Add, "New profile", NewBlank));

        if (selected is null)
            return VStack(12,
                    picker,
                    Subtitle("No profiles yet"),
                    Body("Create a blank profile, or pick your headphones on the AutoEQ page."))
                .Padding(16);

        return Component<ProfileEditor, ProfileEditorProps>(new ProfileEditorProps(
                selected,
                selected.Id == state.Settings.ActiveProfileId,
                state.Settings,
                state.Current,
                id => setSelectedId(id),
                ProfileNames.Unique(profiles, selected.Name + " (copy)"),
                picker))
            .WithKey(selected.Id.ToString());
    }

}

public record ProfileEditorProps(
    Profile Saved,
    bool IsActive,
    CurvySettings Settings,
    ChainResult Current,
    Action<Guid> OnSelect,
    string DuplicateName,
    Element Picker);

/// <summary>
/// Edits one profile. Holds a local draft so the UI reacts instantly; every edit is saved to
/// the engine through <see cref="EngineHost.Debounced"/> (150 ms of quiet). Keyed by profile id.
/// </summary>
public sealed class ProfileEditor : Component<ProfileEditorProps>
{
    static readonly (ChainLayer Layer, string Label, GraphSeriesStyle Style)[] Layers =
    [
        (ChainLayer.AutoEq, "AutoEQ", GraphSeriesStyle.AutoEq),
        (ChainLayer.ProfilePreference, "Profile preference", GraphSeriesStyle.ProfilePreference),
        (ChainLayer.Universal, "Universal", GraphSeriesStyle.Universal),
    ];

    public override Element Render()
    {
        var host = EngineHost.Instance;
        var props = Props;
        var draft = UseRef(props.Saved);
        var lastSaved = UseRef(props.Saved);
        var (_, bump) = UseReducer(0);
        var (nameEdit, setNameEdit) = UseState<string?>(null);
        var (confirmDelete, setConfirmDelete) = UseState(false);
        var (hidden, updateHidden) = UseReducer(0); // bitmask of hidden GraphSeriesStyle values

        // Leaving this profile (list switch, navigation, delete): persist pending edits now, so a
        // rename committed on LostFocus right after the selection changed can't be lost.
        UseEffect(() => () => EngineHost.Instance.Flush(), props.Saved.Id);

        // Adopt changes made elsewhere (e.g. another page) unless our own edits are in flight.
        if (!ReferenceEquals(lastSaved.Current, props.Saved))
        {
            lastSaved.Current = props.Saved;
            if (!host.HasPending(EngineHost.ProfileKey(props.Saved.Id)) && props.Saved with { Modified = default } != draft.Current with { Modified = default })
                draft.Current = props.Saved;
        }
        var p = draft.Current;

        void Update(Func<Profile, Profile> f)
        {
            var next = f(draft.Current);
            if (next == draft.Current) return;
            draft.Current = next;
            bump(n => n + 1);
            host.Debounced(EngineHost.ProfileKey(next.Id), () => host.Engine.SaveProfile(next));
        }

        // Names apply as you type (saved through the usual 150 ms debounce). An empty name is
        // never saved; leaving the box empty restores the last real name.
        void OnNameChanged(string text)
        {
            setNameEdit(text);
            var t = text.Trim();
            if (t.Length > 0 && t != draft.Current.Name) Update(x => x with { Name = t });
        }

        void EndNameEdit() => setNameEdit(null);

        void OnBandChanged(int i, Filter band) => Update(x => i < x.Preference.Bands.Count
            ? x with { Preference = x.Preference with { Bands = x.Preference.Bands.SetItem(i, EqLimits.Clamp(band)) } }
            : x);

        void Duplicate()
        {
            var now = DateTimeOffset.UtcNow;
            var copy = draft.Current with
            {
                Id = Guid.NewGuid(), Name = props.DuplicateName, Created = now, Modified = now,
            };
            host.Now(e => e.SaveProfile(copy));
            props.OnSelect(copy.Id);
        }

        // ── Graph ────────────────────────────────────────────────────────
        bool Visible(GraphSeriesStyle s) => (hidden & (1 << (int)s)) == 0;
        void Toggle(GraphSeriesStyle s, bool on) =>
            updateHidden(h => on ? h & ~(1 << (int)s) : h | (1 << (int)s));

        var settings = props.Settings;
        // A universal preference this profile ignores has no line and no legend entry.
        var shownLayers = Layers
            .Where(l => !(l.Layer == ChainLayer.Universal && p.IgnoreUniversal))
            .ToArray();
        var series = shownLayers
            .Where(l => Visible(l.Style))
            .Select(l => new GraphSeries(l.Label, EqChain.Layer(p, settings, l.Layer).ToList(), l.Style))
            .ToList();
        if (Visible(GraphSeriesStyle.Total))
            series.Add(new GraphSeries("Total",
                EqChain.Build(p, settings with { MasterEnabled = true }).Filters, GraphSeriesStyle.Total));

        var graph = Component<EqGraph, EqGraphProps>(new EqGraphProps(series, p.Preference.ActiveBands, OnBandChanged));

        // Legend + visibility toggles in one: a colour swatch matching the line, then a checkbox.
        var isDark = UseIsDarkTheme();
        var seriesToggles = FlexRow(
            shownLayers.Select(l => (Label: l.Label, Style: l.Style))
                .Append((Label: "Total", Style: GraphSeriesStyle.Total))
                .Select(x => (Element)HStack(6,
                        Border(null)
                            .Background(EqGraph.SeriesHex(x.Style, isDark))
                            .Width(14).Height(x.Style == GraphSeriesStyle.Total ? 4 : 3)
                            .VAlign(VerticalAlignment.Center),
                        // MinWidth 0: WinUI's 120 px CheckBox minimum pads short labels unevenly.
                        CheckBox(Visible(x.Style), on => Toggle(x.Style, on), x.Label)
                            .MinWidth(0)
                            .AutomationName($"Show {x.Label}"))
                    .WithKey(x.Style.ToString()))
                .ToArray()) with { Wrap = FlexWrap.Wrap, ColumnGap = 16, RowGap = 4 };

        // ── Header ───────────────────────────────────────────────────────
        // Activate, then the picker (combo box + New) and the other profile actions on the left, the
        // preamp quick switch on the right, above the graph.
        var header = (FlexRow(
                IconButton(Symbol.Accept, props.IsActive ? "Active profile" : "Activate",
                        () => host.Now(e => e.Activate(draft.Current.Id)))
                    .IsEnabled(!props.IsActive),
                props.Picker,
                IconButton(Symbol.Copy, "Duplicate", Duplicate),
                IconButton(Symbol.Delete, "Delete", () => setConfirmDelete(true)),
                Component<PreampControl, PreampControlProps>(new PreampControlProps(props.Settings, props.Current))
                    .Flex(grow: 1)) with
            {
                Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 8,
            });

        var nameBox = TextBox(nameEdit ?? p.Name, OnNameChanged)
            .Header("Name")
            .Width(300)
            .HAlign(HorizontalAlignment.Left)
            .OnLostFocus((_, _) => EndNameEdit())
            .OnKeyDown((_, e) =>
            {
                if (e.Key != VirtualKey.Enter) return;
                EndNameEdit();
                e.Handled = true;
            });

        var deleteDialog = ContentDialog("Delete profile?",
                Body($"\"{p.Name}\" will be deleted. This can't be undone."),
                "Delete") with
            {
                IsOpen = confirmDelete,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                OnClosed = result =>
                {
                    setConfirmDelete(false);
                    if (result == ContentDialogResult.Primary) host.Now(e => e.DeleteProfile(draft.Current.Id));
                },
            };

        // ── AutoEQ section ───────────────────────────────────────────────
        // Search box applies a correction straight to this profile; the AutoEQ tab is for browsing.
        var autoEqSearch = Component<AutoEqPicker, AutoEqPickerProps>(new AutoEqPickerProps(
            p.AutoEq is null ? "Search your headphones…" : "Search to replace…",
            c => Update(x => x with { AutoEq = c, AutoEqEnabled = true })));
        Element autoEq = p.AutoEq is { } ae
            ? VStack(8,
                // Header with its enable switch on the same line.
                HStack(12,
                    BodyStrong("AutoEQ correction").VAlign(VerticalAlignment.Center),
                    ToggleSwitch(p.AutoEqEnabled, on => Update(x => x with { AutoEqEnabled = on }), "", "")
                        .MinWidth(0)
                        .AutomationName("AutoEQ correction enabled")
                        .VAlign(VerticalAlignment.Center)),
                // Disabled: only the header row; the correction is kept for when it's switched back on.
                p.AutoEqEnabled
                    ? VStack(8,
                        VStack(2,
                            Body(ae.Source.Headphone),
                            Caption(AutoEqText.Join(ae.Source.Source, ae.Source.Rig, $"{ae.Filters.Count} filters", $"fetched {ae.Source.FetchedAt.LocalDateTime:d}"))),
                        autoEqSearch)
                    : null)
            : VStack(8,
                BodyStrong("AutoEQ correction"),
                Caption("No AutoEQ correction in this profile."),
                autoEqSearch);

        var body = VStack(16,
            Card(VStack(12,
                nameBox,
                // Label and switch on one line (no header above, no On/Off text).
                HStack(12,
                    TextBlock("Ignore universal preference").VAlign(VerticalAlignment.Center),
                    ToggleSwitch(p.IgnoreUniversal, on => Update(x => x with { IgnoreUniversal = on }), "", "")
                        .MinWidth(0)
                        .AutomationName("Ignore universal preference")
                        .VAlign(VerticalAlignment.Center)))),
            Card(autoEq),
            Card(Component<PreferenceEditor, PreferenceEditorProps>(new PreferenceEditorProps(
                p.Preference, pref => Update(x => x with { Preference = pref }), "Profile preference",
                p.AutoEqEnabled ? p.AutoEq?.Filters : null))));

        return (FlexColumn(
                header.Flex(shrink: 0),
                graph.Flex(basis: 280, shrink: 0),
                seriesToggles.Flex(shrink: 0),
                ScrollView(body.Padding(0, 0, 16, 16)).Flex(grow: 1, basis: 0),
                deleteDialog) with { RowGap = 12 })
            .FlexPadding(16);
    }
}

static class ProfileButtons
{
    /// <summary>Icon-only button; the label becomes the tooltip and the accessible name.</summary>
    public static ButtonElement IconButton(Symbol symbol, string label, Action onClick) =>
        Button(Icon(SymbolIcon(symbol.ToString())), onClick)
            .ToolTip(label)
            .AutomationName(label);
}
