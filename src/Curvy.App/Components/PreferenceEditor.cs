using System.Globalization;
using Curvy.Core.Filters;
using Curvy.Core.Model;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Globalization.NumberFormatting;
using static Microsoft.UI.Reactor.Factories;
using static Curvy.App.Components.ProfileButtons;

namespace Curvy.App.Components;

/// <param name="Title">Section header; the enable switch sits right next to it.</param>
/// <param name="AutoEqFilters">
/// The AutoEQ correction to offer "Copy AutoEQ frequencies" from (null or empty: no button).
/// </param>
public record PreferenceEditorProps(
    Preference Value, Action<Preference> OnChange, string Title = "Preference",
    IReadOnlyList<Filter>? AutoEqFilters = null);

/// <summary>Limits applied wherever the UI edits a filter or tone control.</summary>
public static class EqLimits
{
    public const double MinHz = 20, MaxHz = 20000;
    public const double MinQ = 0.1, MaxQ = 20;
    public const double ToneDb = 12, BandDb = 20;

    public static double Hz(double v) => Math.Clamp(v, MinHz, MaxHz);
    public static double Q(double v) => Math.Clamp(v, MinQ, MaxQ);
    public static double Gain(double v) => Math.Clamp(v, -BandDb, BandDb);
    public static double Tone(double v) => Math.Round(Math.Clamp(v, -ToneDb, ToneDb), 1);

    public static Filter Clamp(Filter f) =>
        f with { FrequencyHz = Hz(f.FrequencyHz), GainDb = Gain(f.GainDb), Q = Q(f.Q) };
}

/// <summary>
/// Editor for a <see cref="Preference"/>: enabled toggle, then Simple / Parametric tabs (exclusive:
/// the selected one is applied) with bass/treble/tilt sliders or a parametric band list. Controlled: every edit emits a
/// whole new <see cref="Preference"/> through <c>OnChange</c>.
/// </summary>
public sealed class PreferenceEditor : Component<PreferenceEditorProps>
{
    static readonly string[] TypeNames = ["Peak", "Low shelf", "High shelf"];
    static readonly FilterType[] Types = [FilterType.Peaking, FilterType.LowShelf, FilterType.HighShelf];

    public static readonly Filter DefaultBand = new(FilterType.Peaking, 1000, 0, 1);

    const int LockColumn = 3;

    static readonly PreferenceMode[] Modes = [PreferenceMode.Simple, PreferenceMode.Parametric];
    static readonly SelectorBarItemData[] ModeTabs = [SelectorBarItem("Simple"), SelectorBarItem("Parametric")];

    public override Element Render()
    {
        // Edits fired before the parent re-renders must build on each other, not on the
        // stale Props.Value, so keep the latest emitted value until the next render.
        var latest = UseRef(Props.Value);
        latest.Current = Props.Value;
        var onChange = Props.OnChange;
        var value = Props.Value;
        var tone = value.Tone;
        var (confirmCopy, setConfirmCopy) = UseState(false);

        void Edit(Func<Preference, Preference> f)
        {
            var next = f(latest.Current);
            if (next == latest.Current) return;
            latest.Current = next;
            onChange(next);
        }

        void EditTone(Func<ToneControls, ToneControls> f) => Edit(p => p with { Tone = f(p.Tone) });

        void EditBand(int i, Func<Filter, Filter> f) => Edit(p =>
            i < p.Bands.Count ? p with { Bands = p.Bands.SetItem(i, EqLimits.Clamp(f(p.Bands[i]))) } : p);

        Element ToneSlider(string label, double db, Func<ToneControls, double, ToneControls> set) =>
            Grid([GridSize.Px(64), GridSize.Star(), GridSize.Px(72)], [GridSize.Auto],
                TextBlock(label).VAlign(VerticalAlignment.Center).Grid(row: 0, column: 0),
                Slider(db, -EqLimits.ToneDb, EqLimits.ToneDb, v => EditTone(t => set(t, EqLimits.Tone(v))))
                    .StepFrequency(0.1)
                    .AutomationName($"{label} gain")
                    .Grid(row: 0, column: 1),
                TextBlock($"{db:+0.0;-0.0;0.0} dB").HAlign(HorizontalAlignment.Right)
                    .VAlign(VerticalAlignment.Center).Grid(row: 0, column: 2));

        static Element Num(string header, double v, double min, double max, double step, Action<double> set,
            double? width = 120, string? automationLabel = null) =>
            Component<NumField, NumFieldProps>(new NumFieldProps(header, v, min, max, step, set, width, automationLabel));

        // Compact table: one row per band under a single header line.
        // [on] [type ▾] [Hz][lock] [Gain dB] [Q] [🗑]
        // The lock column is fixed width (not Auto): the header row has no cell there, and an Auto
        // column would collapse in it and shift the Gain/Q captions.
        GridSize[] bandColumns =
        [
            GridSize.Px(32), GridSize.Px(124), GridSize.Px(120), GridSize.Px(44), GridSize.Px(104), GridSize.Px(96),
            GridSize.Auto, // Auto: the remove button sizes itself (last column, nothing to misalign)
        ];
        Element Cell(Element e, int column) =>
            e.Margin(column is 0 or LockColumn ? 0 : 4, 0, 4, 0).VAlign(VerticalAlignment.Center).Grid(row: 0, column: column);

        // Column headers with an info glyph; hovering shows what the value does.
        static Element Header(string text, string help) =>
            // Both line boxes trimmed to what's drawn (cap height / glyph), so centring them lines the
            // icon up with the letters instead of with the text's descender space.
            HStack(4,
                    Caption(text)
                        .Set(t => t.TextLineBounds = TextLineBounds.TrimToCapHeight)
                        .VAlign(VerticalAlignment.Center),
                    TextBlock("") // Info
                        .FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets")
                        .FontSize(12)
                        .Opacity(0.7)
                        .Set(t => t.TextLineBounds = TextLineBounds.Tight)
                        .VAlign(VerticalAlignment.Center))
                .ToolTip(help)
                .AutomationName($"{text}: {help}");

        var bandHeader = Grid(bandColumns, [GridSize.Auto],
            Cell(Header("Type",
                "Shape of the filter. Peak boosts or cuts a range around the frequency. "
                + "Low shelf changes everything below it, high shelf everything above it."), 1),
            Cell(Header("Hz",
                "Frequency the filter works at: the centre of a peak, or the corner of a shelf. "
                + "Bass is roughly 20–250 Hz, mids 250 Hz–4 kHz, treble above."), 2),
            Cell(Header("Gain dB",
                "How much to boost (+) or cut (−). About +6 dB is twice the amplitude; small steps of 1–3 dB "
                + "are usually enough. With the preamp on Auto, boosts are offset so nothing clips."), 4),
            Cell(Header("Q",
                "Width of the filter. Low Q (0.5–1) is broad and gentle, high Q (4+) is narrow and surgical. "
                + "For shelves it sets how steep the transition is."), 5));

        var bandRows = value.Bands.Select((b, i) =>
                Grid(bandColumns, [GridSize.Auto],
                        Cell(CheckBox(b.Enabled, on => EditBand(i, f => f with { Enabled = on }))
                            .MinWidth(0)
                            .ToolTip(b.Enabled ? "Band on" : "Band off")
                            .AutomationName($"Band {i + 1} enabled"), 0),
                        Cell(ComboBox(TypeNames, Array.IndexOf(Types, b.Type),
                                idx => { if (idx >= 0) EditBand(i, f => f with { Type = Types[idx] }); })
                            .AutomationName($"Band {i + 1} type")
                            .HAlign(HorizontalAlignment.Stretch), 1),
                        // A locked band's frequency is read-only here and on the graph.
                        Cell(Num("", b.FrequencyHz, EqLimits.MinHz, EqLimits.MaxHz, 10,
                            x => EditBand(i, f => f with { FrequencyHz = x }), null, $"Band {i + 1} frequency")
                            .IsEnabled(!b.FrequencyLocked), 2),
                        // Padlock glyph from the icon font (the SymbolIcon names have no lock).
                        Cell(ToggleButton(
                                TextBlock(b.FrequencyLocked ? "\uE72E" : "\uE785")
                                    .FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets")
                                    .FontSize(16)
                                    .HAlign(HorizontalAlignment.Center),
                                b.FrequencyLocked,
                                on => EditBand(i, f => f with { FrequencyLocked = on }))
                            .Width(40)
                            .Padding(0, 5, 0, 6)
                            .ToolTip(b.FrequencyLocked
                                ? "Frequency locked: the graph handle moves up and down only"
                                : "Lock frequency (graph handle moves up and down only)")
                            .AutomationName($"Lock band {i + 1} frequency"), LockColumn),
                        Cell(Num("", b.GainDb, -EqLimits.BandDb, EqLimits.BandDb, 0.5,
                            x => EditBand(i, f => f with { GainDb = x }), null, $"Band {i + 1} gain"), 4),
                        Cell(Num("", b.Q, EqLimits.MinQ, EqLimits.MaxQ, 0.1,
                            x => EditBand(i, f => f with { Q = x }), null, $"Band {i + 1} Q"), 5),
                        Cell(IconButton(Symbol.Delete, $"Remove band {i + 1}",
                            () => Edit(p => i < p.Bands.Count ? p with { Bands = p.Bands.RemoveAt(i) } : p)), 6))
                    .HAlign(HorizontalAlignment.Left)
                    .WithKey($"band-{i}"))
            .ToArray<Element>();

        // Copy AutoEQ frequencies: replaces the bands, so ask first when that would drop any gain.
        var autoEq = Props.AutoEqFilters;
        void ApplyAutoEqFrequencies()
        {
            if (autoEq is { Count: > 0 }) Edit(p => p with { Bands = Preference.BandsAt(autoEq) });
        }
        void CopyAutoEqFrequencies()
        {
            if (latest.Current.Bands.Any(b => b.GainDb != 0)) setConfirmCopy(true);
            else ApplyAutoEqFrequencies();
        }
        var copyDialog = ContentDialog("Replace the bands?",
                Body($"The {value.Bands.Count} current bands (and their gains) are replaced by "
                     + $"{autoEq?.Count ?? 0} flat bands at the AutoEQ frequencies."),
                "Replace") with
            {
                IsOpen = confirmCopy,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                OnClosed = result =>
                {
                    setConfirmCopy(false);
                    if (result == ContentDialogResult.Primary) ApplyAutoEqFrequencies();
                },
            };

        return VStack(12,
            // Header with its enable switch on the same line (no On/Off text).
            HStack(12,
                BodyStrong(Props.Title).VAlign(VerticalAlignment.Center),
                ToggleSwitch(value.Enabled, on => Edit(p => p with { Enabled = on }), "", "")
                    .MinWidth(0)
                    .AutomationName($"{Props.Title} enabled")
                    .VAlign(VerticalAlignment.Center)),
            // Disabled: only the header row; the settings come back unchanged when re-enabled.
            !value.Enabled ? null : VStack(12,
                // Exclusive modes: the selected tab is what gets applied; the other keeps its values.
                SelectorBar(ModeTabs, Array.IndexOf(Modes, value.EffectiveMode),
                        i => { if (i >= 0 && i < Modes.Length) Edit(p => p with { Mode = Modes[i] }); })
                    .AutomationName($"{Props.Title} mode"),
                value.EffectiveMode == PreferenceMode.Simple
                    // Simple = just the three sliders (fixed frequencies/Q from ToneControls' defaults).
                    ? VStack(4,
                            ToneSlider("Bass", tone.BassDb, (t, v) => t with { BassDb = v }),
                            ToneSlider("Treble", tone.TrebleDb, (t, v) => t with { TrebleDb = v }),
                            ToneSlider("Tilt", tone.TiltDb, (t, v) => t with { TiltDb = v }))
                        .WithKey("simple")
                    : VStack(8,
                            bandHeader.HAlign(HorizontalAlignment.Left),
                            VStack(6, bandRows),
                            (FlexRow(
                                Button("Add band", () => Edit(p => p with { Bands = p.Bands.Add(DefaultBand) })),
                                Button("Reset to default bands",
                                    () => Edit(p => p with { Bands = Preference.DefaultBands })),
                                autoEq is { Count: > 0 }
                                    ? Button("Copy AutoEQ frequencies", CopyAutoEqFrequencies)
                                        .ToolTip($"Replace the bands with {autoEq.Count} flat, frequency-locked bands at the AutoEQ filters")
                                    : null) with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 8 }),
                            copyDialog)
                        .WithKey("parametric")));
    }
}

/// <param name="Header">Label above the box; empty for none (then pass <paramref name="AutomationLabel"/>).</param>
/// <param name="AutomationLabel">Screen-reader name; defaults to <paramref name="Header"/>.</param>
public record NumFieldProps(
    string Header, double Value, double Min, double Max, double Step, Action<double> OnCommit, double? Width,
    string? AutomationLabel = null);

/// <summary>
/// A controlled <c>NumberBox</c> that never shows a value the model doesn't have.
/// <para>
/// WinUI's NumberBox commits <c>NaN</c> when its text is cleared (or can't be parsed), and
/// keeps showing an empty box. Rejecting that value is not enough: the model value is
/// unchanged, so Reactor's next render produces the same <c>Value</c> prop and (props being
/// diffed, not re-pushed) never writes it back to the control. On a rejected commit we bump a
/// nonce that is part of the element key, which remounts the box showing the model value.
/// </para>
/// </summary>
public sealed class NumField : Component<NumFieldProps>
{
    /// <summary>
    /// Parses and formats in the same culture the rest of the UI uses (e.g. "0,7" in de-DE), so a
    /// typed decimal is read the way it is displayed; up to two decimals are shown.
    /// </summary>
    static readonly DecimalFormatter Formatter = CreateFormatter();

    static DecimalFormatter CreateFormatter()
    {
        DecimalFormatter f;
        try
        {
            f = new DecimalFormatter(new[] { CultureInfo.CurrentCulture.Name }, RegionInfo.CurrentRegion.TwoLetterISORegionName);
        }
        catch (Exception)
        {
            f = new DecimalFormatter();
        }
        f.IntegerDigits = 1;
        f.FractionDigits = 0;
        f.IsGrouped = false;
        f.NumberRounder = new IncrementNumberRounder { Increment = 0.01, RoundingAlgorithm = RoundingAlgorithm.RoundHalfAwayFromZero };
        return f;
    }

    public override Element Render()
    {
        var p = Props;
        var (nonce, bump) = UseReducer(0);

        void OnValue(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x))
            {
                bump(n => n + 1);
                return;
            }
            var clamped = Math.Clamp(x, p.Min, p.Max);
            if (clamped != x) bump(n => n + 1);
            if (clamped != p.Value) p.OnCommit(clamped);
        }

        var box = (NumberBox(p.Value, OnValue, string.IsNullOrEmpty(p.Header) ? null : p.Header) with { SmallChange = p.Step, LargeChange = p.Step * 10 })
            .Range(p.Min, p.Max)
            .NumberFormatter(Formatter)
            .SpinButtons(NumberBoxSpinButtonPlacementMode.Compact)
            .AutomationName(p.AutomationLabel ?? p.Header);
        Element e = p.Width is { } w ? box.Width(w) : box.HAlign(HorizontalAlignment.Stretch);
        return e.WithKey($"n{nonce}");
    }
}
