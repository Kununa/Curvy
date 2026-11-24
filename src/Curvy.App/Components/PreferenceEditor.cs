using System.Globalization;
using Curvy.Core.Filters;
using Curvy.Core.Model;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Globalization.NumberFormatting;
using static Microsoft.UI.Reactor.Factories;

namespace Curvy.App.Components;

public record PreferenceEditorProps(Preference Value, Action<Preference> OnChange);

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
/// Editor for a <see cref="Preference"/>: enabled toggle, bass/treble/tilt sliders with an
/// expander for their frequency/Q, and a parametric band list. Controlled: every edit emits a
/// whole new <see cref="Preference"/> through <c>OnChange</c>.
/// </summary>
public sealed class PreferenceEditor : Component<PreferenceEditorProps>
{
    static readonly string[] TypeNames = ["Peaking", "Low shelf", "High shelf"];
    static readonly FilterType[] Types = [FilterType.Peaking, FilterType.LowShelf, FilterType.HighShelf];

    public static readonly Filter DefaultBand = new(FilterType.Peaking, 1000, 0, 1);

    public override Element Render()
    {
        // Edits fired before the parent re-renders must build on each other, not on the
        // stale Props.Value, so keep the latest emitted value until the next render.
        var latest = UseRef(Props.Value);
        latest.Current = Props.Value;
        var onChange = Props.OnChange;
        var value = Props.Value;
        var tone = value.Tone;

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
            double? width = 120) =>
            Component<NumField, NumFieldProps>(new NumFieldProps(header, v, min, max, step, set, width));

        var toneDetails = Expander("Tone control details",
            VStack(12,
                HStack(12,
                    Num("Bass Hz", tone.BassHz, EqLimits.MinHz, EqLimits.MaxHz, 5,
                        x => EditTone(t => t with { BassHz = x })),
                    Num("Bass Q", tone.BassQ, EqLimits.MinQ, EqLimits.MaxQ, 0.1,
                        x => EditTone(t => t with { BassQ = x }))),
                HStack(12,
                    Num("Treble Hz", tone.TrebleHz, EqLimits.MinHz, EqLimits.MaxHz, 100,
                        x => EditTone(t => t with { TrebleHz = x })),
                    Num("Treble Q", tone.TrebleQ, EqLimits.MinQ, EqLimits.MaxQ, 0.1,
                        x => EditTone(t => t with { TrebleQ = x }))),
                Num("Tilt pivot Hz", tone.TiltPivotHz, EqLimits.MinHz, EqLimits.MaxHz, 50,
                    x => EditTone(t => t with { TiltPivotHz = x }))))
            .HAlign(HorizontalAlignment.Stretch);

        // Two lines per band so the row fits the editor column (~450 DIP at the default window
        // size): [type ▾ | On | Remove] above [Hz | Gain | Q], the number boxes sharing the width.
        var bandRows = value.Bands.Select((b, i) =>
                VStack(4,
                    Grid([GridSize.Star(), GridSize.Auto, GridSize.Auto], [GridSize.Auto],
                        ComboBox(TypeNames, Array.IndexOf(Types, b.Type),
                                idx => { if (idx >= 0) EditBand(i, f => f with { Type = Types[idx] }); })
                            .AutomationName($"Band {i + 1} type")
                            .HAlign(HorizontalAlignment.Stretch)
                            .Grid(row: 0, column: 0),
                        CheckBox(b.Enabled, on => EditBand(i, f => f with { Enabled = on }), "On")
                            .AutomationName($"Band {i + 1} enabled")
                            .Margin(12, 0, 0, 0)
                            .Grid(row: 0, column: 1),
                        Button("Remove",
                                () => Edit(p => i < p.Bands.Count ? p with { Bands = p.Bands.RemoveAt(i) } : p))
                            .AutomationName($"Remove band {i + 1}")
                            .Grid(row: 0, column: 2)),
                    Grid([GridSize.Star(), GridSize.Star(), GridSize.Star()], [GridSize.Auto],
                        Num("Hz", b.FrequencyHz, EqLimits.MinHz, EqLimits.MaxHz, 10,
                                x => EditBand(i, f => f with { FrequencyHz = x }), width: null)
                            .Margin(0, 0, 4, 0).Grid(row: 0, column: 0),
                        Num("Gain dB", b.GainDb, -EqLimits.BandDb, EqLimits.BandDb, 0.5,
                                x => EditBand(i, f => f with { GainDb = x }), width: null)
                            .Margin(4, 0, 4, 0).Grid(row: 0, column: 1),
                        Num("Q", b.Q, EqLimits.MinQ, EqLimits.MaxQ, 0.1,
                                x => EditBand(i, f => f with { Q = x }), width: null)
                            .Margin(4, 0, 0, 0).Grid(row: 0, column: 2)))
                    .WithKey($"band-{i}"))
            .ToArray<Element>();

        return VStack(12,
            ToggleSwitch(value.Enabled, on => Edit(p => p with { Enabled = on }), "Enabled", "Disabled",
                "Preference"),
            VStack(4,
                ToneSlider("Bass", tone.BassDb, (t, v) => t with { BassDb = v }),
                ToneSlider("Treble", tone.TrebleDb, (t, v) => t with { TrebleDb = v }),
                ToneSlider("Tilt", tone.TiltDb, (t, v) => t with { TiltDb = v })),
            toneDetails,
            BodyStrong($"Bands ({value.Bands.Count})"),
            VStack(16, bandRows),
            Button("Add band", () => Edit(p => p with { Bands = p.Bands.Add(DefaultBand) })));
    }
}

public record NumFieldProps(
    string Header, double Value, double Min, double Max, double Step, Action<double> OnCommit, double? Width);

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

        var box = (NumberBox(p.Value, OnValue, p.Header) with { SmallChange = p.Step, LargeChange = p.Step * 10 })
            .Range(p.Min, p.Max)
            .NumberFormatter(Formatter)
            .SpinButtons(NumberBoxSpinButtonPlacementMode.Compact)
            .AutomationName(p.Header);
        Element e = p.Width is { } w ? box.Width(w) : box.HAlign(HorizontalAlignment.Stretch);
        return e.WithKey($"n{nonce}");
    }
}
