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

namespace Curvy.App.Components;

public record ProfilesPageProps(EngineState State, Action<string?> OnOpenAutoEq);

/// <summary>Profile list (left) and the editor for the selected profile (right).</summary>
public sealed class ProfilesPage : Component<ProfilesPageProps>
{
    public override Element Render()
    {
        var host = EngineHost.Instance;
        var state = Props.State;
        var profiles = state.Profiles;
        var (selectedId, setSelectedId) = UseState<Guid?>(state.ActiveProfile?.Id ?? profiles.FirstOrDefault()?.Id);

        // Fall back to the active (or first) profile when the selection disappeared.
        var selected = profiles.FirstOrDefault(p => p.Id == selectedId)
                       ?? state.ActiveProfile
                       ?? profiles.FirstOrDefault();
        var selectedIndex = selected is null ? -1 : profiles.ToList().FindIndex(p => p.Id == selected.Id);

        void NewBlank()
        {
            var p = Profile.New(UniqueName(profiles, "New profile"));
            host.Now(e => e.SaveProfile(p));
            setSelectedId(p.Id);
        }

        var rows = profiles.Select(p =>
                HStack(8,
                        TextBlock(p.Name).TextTrimming(TextTrimming.CharacterEllipsis),
                        p.Id == state.Settings.ActiveProfileId
                            ? Caption("● active").Foreground(Theme.AccentText).VAlign(VerticalAlignment.Center)
                            : null)
                    .AutomationName(p.Id == state.Settings.ActiveProfileId ? $"{p.Name} (active)" : p.Name)
                    .WithKey(p.Id.ToString()))
            .ToArray<Element>();

        var list = (FlexColumn(
                Subtitle("Profiles").Flex(shrink: 0),
                Button("New blank profile", NewBlank).HAlign(HorizontalAlignment.Stretch).Flex(shrink: 0),
                ListView(selectedIndex, i =>
                    {
                        if (i < 0 || i >= profiles.Count || profiles[i].Id == selected?.Id) return;
                        // Save the outgoing profile's pending edits before switching (belt and
                        // braces: debounce slots are keyed per profile, and the editor also
                        // flushes on unmount).
                        host.Flush();
                        setSelectedId(profiles[i].Id);
                    }, rows)
                    .Flex(grow: 1, basis: 0)) with { RowGap = 12 })
            .FlexPadding(16)
            .Flex(basis: 240, shrink: 0);

        Element editor = selected is null
            ? VStack(8,
                    Subtitle("No profiles yet"),
                    Body("Create a blank profile, or pick your headphones on the AutoEQ page."))
                .Padding(24)
            : Component<ProfileEditor, ProfileEditorProps>(new ProfileEditorProps(
                    selected,
                    selected.Id == state.Settings.ActiveProfileId,
                    state.Settings,
                    state.Current,
                    state.ActiveProfile?.Name,
                    Props.OnOpenAutoEq,
                    id => setSelectedId(id),
                    UniqueName(profiles, selected.Name + " (copy)")))
                .WithKey(selected.Id.ToString());

        return FlexRow(list, Border(editor).Flex(grow: 1, basis: 0));
    }

    static string UniqueName(IReadOnlyList<Profile> profiles, string baseName)
    {
        var name = baseName;
        for (var n = 2; profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); n++)
            name = $"{baseName} {n}";
        return name;
    }
}

public record ProfileEditorProps(
    Profile Saved,
    bool IsActive,
    CurvySettings Settings,
    ChainResult Current,
    string? ActiveProfileName,
    Action<string?> OnOpenAutoEq,
    Action<Guid> OnSelect,
    string DuplicateName);

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

        void CommitName()
        {
            var t = nameEdit?.Trim();
            if (!string.IsNullOrEmpty(t) && t != draft.Current.Name) Update(x => x with { Name = t });
            setNameEdit(null);
        }

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
        var series = Layers
            .Where(l => Visible(l.Style))
            .Select(l => new GraphSeries(l.Label, EqChain.Layer(p, settings, l.Layer).ToList(), l.Style))
            .ToList();
        if (Visible(GraphSeriesStyle.Total))
            series.Add(new GraphSeries("Total",
                EqChain.Build(p, settings with { MasterEnabled = true }).Filters, GraphSeriesStyle.Total));

        var graph = Component<EqGraph, EqGraphProps>(new EqGraphProps(series, p.Preference.Bands, OnBandChanged));

        // Legend + visibility toggles in one: a colour swatch matching the line, then a checkbox.
        var isDark = UseIsDarkTheme();
        var seriesToggles = FlexRow(
            Layers.Select(l => (Label: l.Label, Style: l.Style))
                .Append((Label: "Total", Style: GraphSeriesStyle.Total))
                .Select(x => (Element)HStack(6,
                        Border(null)
                            .Background(EqGraph.SeriesHex(x.Style, isDark))
                            .Width(14).Height(x.Style == GraphSeriesStyle.Total ? 4 : 3)
                            .VAlign(VerticalAlignment.Center),
                        CheckBox(Visible(x.Style), on => Toggle(x.Style, on), x.Label)
                            .AutomationName($"Show {x.Label}"))
                    .WithKey(x.Style.ToString()))
                .ToArray()) with { Wrap = FlexWrap.Wrap, ColumnGap = 16, RowGap = 4 };

        // ── Header ───────────────────────────────────────────────────────
        var header = HStack(8,
            TextBox(nameEdit ?? p.Name, setNameEdit)
                .Header("Name")
                .Width(260)
                .OnLostFocus((_, _) => CommitName())
                .OnKeyDown((_, e) =>
                {
                    if (e.Key != VirtualKey.Enter) return;
                    CommitName();
                    e.Handled = true;
                }),
            Button(props.IsActive ? "Active" : "Activate", () => host.Now(e => e.Activate(draft.Current.Id)))
                .IsEnabled(!props.IsActive)
                .VAlign(VerticalAlignment.Bottom),
            Button("Duplicate", Duplicate).VAlign(VerticalAlignment.Bottom),
            Button("Delete", () => setConfirmDelete(true)).VAlign(VerticalAlignment.Bottom));

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
        Element autoEq = p.AutoEq is { } ae
            ? VStack(8,
                BodyStrong("AutoEQ correction"),
                Body(ae.Source.Headphone),
                Caption($"{ae.Source.Source} · {ae.Source.Rig} · {ae.Filters.Count} filters · fetched {ae.Source.FetchedAt.LocalDateTime:d}"),
                HStack(12,
                    ToggleSwitch(p.AutoEqEnabled, on => Update(x => x with { AutoEqEnabled = on }), "On", "Off"),
                    Button("Change source", () => props.OnOpenAutoEq(ae.Source.Headphone))
                        .VAlign(VerticalAlignment.Center)))
            : VStack(8,
                BodyStrong("AutoEQ correction"),
                Caption("No AutoEQ correction in this profile."),
                Button("Choose AutoEQ source", () => props.OnOpenAutoEq(p.Name)));

        var body = VStack(16,
            Card(autoEq),
            Card(ToggleSwitch(p.IgnoreUniversal, on => Update(x => x with { IgnoreUniversal = on }),
                "Ignored", "Applied", "Ignore universal preference")),
            Card(VStack(8,
                BodyStrong("Profile preference"),
                Component<PreferenceEditor, PreferenceEditorProps>(new PreferenceEditorProps(
                    p.Preference, pref => Update(x => x with { Preference = pref }))))),
            Card(Component<FilterTextView, FilterTextViewProps>(
                new FilterTextViewProps(props.Current, props.ActiveProfileName))));

        return (FlexColumn(
                header.Flex(shrink: 0),
                props.IsActive
                    ? null
                    : Caption("Preview: this profile isn't active, so the graph shows what it would sound like.")
                        .Flex(shrink: 0),
                graph.Flex(basis: 280, shrink: 0),
                seriesToggles.Flex(shrink: 0),
                ScrollView(body.Padding(0, 0, 16, 16)).Flex(grow: 1, basis: 0),
                deleteDialog) with { RowGap = 12 })
            .FlexPadding(16);
    }
}
