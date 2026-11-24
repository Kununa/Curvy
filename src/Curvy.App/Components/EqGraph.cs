using Curvy.Core.Filters;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using ScottPlot;
using ScottPlot.WinUI;
using static Microsoft.UI.Reactor.Factories;
using PlotColor = ScottPlot.Color;

namespace Curvy.App.Components;

public enum GraphSeriesStyle { AutoEq, ProfilePreference, Universal, Total }

public record GraphSeries(string Label, IReadOnlyList<Filter> Filters, GraphSeriesStyle Style);

public record EqGraphProps(
    IReadOnlyList<GraphSeries> Series,
    IReadOnlyList<Filter> EditableBands,
    Action<int, Filter> OnBandChanged);

/// <summary>
/// Frequency-response graph: a ScottPlot <see cref="WinUIPlot"/> hosted in Reactor via
/// <c>XamlHost</c>. X is log10(frequency) with manual decade ticks; each editable band
/// gets a draggable handle (horizontal = frequency, vertical = gain).
/// </summary>
public sealed class EqGraph : Component<EqGraphProps>
{
    public const double MinHz = 20, MaxHz = 20000, MaxGainDb = 15;
    const float HandleHitRadiusDip = 12;

    static readonly double[] Freqs = FrequencyGrid.LogSpaced(512);
    static readonly double[] Xs = Freqs.Select(Math.Log10).ToArray();

    sealed class Interaction
    {
        public EqGraphProps Props = null!;
        public int DragIndex = -1;
    }

    public override Element Render()
    {
        var interaction = UseRef(new Interaction());
        interaction.Current.Props = Props;
        var props = Props;

        return XamlHost(
            () => CreatePlot(interaction.Current),
            fe => Redraw((WinUIPlot)fe, props),
            typeKey: "Curvy.EqGraph");
    }

    static WinUIPlot CreatePlot(Interaction state)
    {
        var control = new WinUIPlot();
        control.UserInputProcessor.Disable(); // no pan/zoom/menu; we own the pointer
        ConfigureAxes(control.Plot);

        control.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler((_, e) => OnPressed(control, state, e)), handledEventsToo: true);
        control.AddHandler(UIElement.PointerMovedEvent,
            new PointerEventHandler((_, e) => OnMoved(control, state, e)), handledEventsToo: true);
        control.AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler((_, e) => EndDrag(control, state, e)), handledEventsToo: true);
        control.AddHandler(UIElement.PointerCaptureLostEvent,
            new PointerEventHandler((_, _) => state.DragIndex = -1), handledEventsToo: true);
        return control;
    }

    static void ConfigureAxes(Plot plot)
    {
        var xTicks = new ScottPlot.TickGenerators.NumericManual();
        foreach (var (hz, label) in new (double, string)[]
                 {
                     (20, "20"), (50, "50"), (100, "100"), (200, "200"), (500, "500"),
                     (1000, "1k"), (2000, "2k"), (5000, "5k"), (10000, "10k"), (20000, "20k"),
                 })
            xTicks.AddMajor(Math.Log10(hz), label);
        for (var decade = 10.0; decade <= 10000; decade *= 10)
            for (var m = 2; m <= 9; m++)
                if (decade * m is >= MinHz and <= MaxHz)
                    xTicks.AddMinor(Math.Log10(decade * m));
        plot.Axes.Bottom.TickGenerator = xTicks;
        plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericFixedInterval(5);
        plot.XLabel("Frequency (Hz)");
        plot.YLabel("Gain (dB)");
        ApplyLimits(plot);
    }

    static void ApplyLimits(Plot plot) =>
        plot.Axes.SetLimits(Math.Log10(MinHz), Math.Log10(MaxHz), -MaxGainDb, MaxGainDb);

    static PlotColor ColorFor(GraphSeriesStyle style) => style switch
    {
        GraphSeriesStyle.AutoEq => PlotColor.FromHex("#3A86FF"),
        GraphSeriesStyle.ProfilePreference => PlotColor.FromHex("#FF8C00"),
        GraphSeriesStyle.Universal => PlotColor.FromHex("#2A9D8F"),
        _ => PlotColor.FromHex("#202020"),
    };

    static void Redraw(WinUIPlot control, EqGraphProps props)
    {
        var plot = control.Plot;
        plot.Clear();

        // Draw Total last so it sits on top.
        foreach (var s in props.Series.OrderBy(s => s.Style == GraphSeriesStyle.Total ? 1 : 0))
        {
            var ys = Response.Evaluate(s.Filters, Freqs);
            var line = plot.Add.ScatterLine(Xs, ys, ColorFor(s.Style));
            line.LineWidth = s.Style == GraphSeriesStyle.Total ? 3.5f : 1.5f;
            line.LegendText = s.Label;
        }

        for (var i = 0; i < props.EditableBands.Count; i++)
        {
            var b = props.EditableBands[i];
            var marker = plot.Add.Marker(Math.Log10(b.FrequencyHz), b.GainDb, MarkerShape.FilledCircle, 12,
                ColorFor(GraphSeriesStyle.ProfilePreference));
            marker.MarkerLineColor = PlotColor.FromHex("#FFFFFF");
            marker.MarkerLineWidth = 1.5f;
        }

        plot.ShowLegend(Alignment.UpperRight);
        ApplyLimits(plot);
        control.Refresh();
    }

    static Pixel PointerPixel(WinUIPlot control, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(control).Position;
        return new Pixel((float)(p.X * control.DisplayScale), (float)(p.Y * control.DisplayScale));
    }

    static void OnPressed(WinUIPlot control, Interaction state, PointerRoutedEventArgs e)
    {
        var bands = state.Props.EditableBands;
        if (bands.Count == 0) return;
        var px = PointerPixel(control, e);
        var best = -1;
        var bestDist = HandleHitRadiusDip * control.DisplayScale;
        for (var i = 0; i < bands.Count; i++)
        {
            var hp = control.Plot.GetPixel(new Coordinates(Math.Log10(bands[i].FrequencyHz), bands[i].GainDb));
            var d = MathF.Sqrt((hp.X - px.X) * (hp.X - px.X) + (hp.Y - px.Y) * (hp.Y - px.Y));
            if (d <= bestDist) { best = i; bestDist = d; }
        }
        if (best < 0) return;
        state.DragIndex = best;
        control.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    static void OnMoved(WinUIPlot control, Interaction state, PointerRoutedEventArgs e)
    {
        var i = state.DragIndex;
        var bands = state.Props.EditableBands;
        if (i < 0 || i >= bands.Count) return;
        var c = control.Plot.GetCoordinates(PointerPixel(control, e));
        var hz = Math.Clamp(Math.Pow(10, c.X), MinHz, MaxHz);
        var gain = Math.Clamp(c.Y, -MaxGainDb, MaxGainDb);
        var old = bands[i];
        var updated = old with
        {
            FrequencyHz = Math.Round(hz, hz < 1000 ? 1 : 0),
            GainDb = Math.Round(gain, 1),
            Q = Math.Max(0.1, old.Q),
        };
        if (updated != old) state.Props.OnBandChanged(i, updated);
        e.Handled = true;
    }

    static void EndDrag(WinUIPlot control, Interaction state, PointerRoutedEventArgs e)
    {
        if (state.DragIndex < 0) return;
        state.DragIndex = -1;
        control.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }
}
