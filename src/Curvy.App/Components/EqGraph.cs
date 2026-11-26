using Curvy.Core.Filters;
using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;

namespace Curvy.App.Components;

public enum GraphSeriesStyle { AutoEq, ProfilePreference, Universal, Total }

public record GraphSeries(string Label, IReadOnlyList<Filter> Filters, GraphSeriesStyle Style);

public record EqGraphProps(
    IReadOnlyList<GraphSeries> Series,
    IReadOnlyList<Filter> EditableBands,
    Action<int, Filter> OnBandChanged);

/// <summary>
/// Frequency-response graph drawn with plain WinUI shapes on a <see cref="Canvas"/>, hosted in
/// Reactor via <c>XamlHost</c>. X is log10(frequency) with decade ticks; each editable band gets a
/// draggable handle (horizontal = frequency, vertical = gain; gain only when the band's frequency is
/// locked, drawn as a square).
/// </summary>
public sealed class EqGraph : Component<EqGraphProps>
{
    public const double MinHz = 20, MaxHz = 20000, MaxGainDb = 15;

    public override Element Render()
    {
        var props = Props;
        var isDark = UseIsDarkTheme();
        var palette = isDark ? GraphPalette.Dark : GraphPalette.Light;
        return XamlHost(
            () => new EqCanvas(),
            fe => ((EqCanvas)fe).Update(props, palette),
            typeKey: "Curvy.EqGraph");
    }

    /// <summary>Graph colours for the app's light or dark theme (<c>#RRGGBB</c>).</summary>
    internal sealed record GraphPalette(
        string Figure, string Data, string Axis, string Grid, string MinorGrid, string Total, string HandleOutline)
    {
        public static readonly GraphPalette Light =
            new("#FFFFFF", "#FAFAFA", "#303030", "#E3E3E3", "#F0F0F0", "#202020", "#FFFFFF");
        public static readonly GraphPalette Dark =
            new("#202020", "#272727", "#D7D7D7", "#3C3C3C", "#2F2F2F", "#F2F2F2", "#202020");
    }

    /// <summary>
    /// Line colour of a series as <c>#RRGGBB</c>, so callers can draw a matching legend swatch
    /// (the graph has no in-plot legend: it would cover the band handles).
    /// </summary>
    public static string SeriesHex(GraphSeriesStyle style, bool isDark) => style switch
    {
        GraphSeriesStyle.AutoEq => "#3A86FF",
        GraphSeriesStyle.ProfilePreference => "#FF8C00",
        GraphSeriesStyle.Universal => "#2A9D8F",
        _ => (isDark ? GraphPalette.Dark : GraphPalette.Light).Total,
    };
}

/// <summary>
/// The drawing surface behind <see cref="EqGraph"/>, in three layers so a drag stays cheap:
/// <list type="bullet">
/// <item>background (data area, grid, tick labels, axis titles): rebuilt only on resize or theme change;</item>
/// <item>curves: one reused <see cref="Polyline"/> per series, only its points are replaced;</item>
/// <item>handles: reused shapes, only moved (recreated when the band count or a lock changes).</item>
/// </list>
/// </summary>
sealed class EqCanvas : Canvas
{
    // Plot-area insets (DIP): room for the gain labels + axis title on the left, frequency labels +
    // axis title at the bottom.
    const double Left = 58, Right = 16, Top = 12, Bottom = 44;
    const double HandleSize = 12, HandleHitRadius = 12;
    const double LabelFontSize = 12, TitleFontSize = 13;

    static readonly double[] Freqs = FrequencyGrid.LogSpaced(384);
    static readonly double LogMin = Math.Log10(EqGraph.MinHz), LogMax = Math.Log10(EqGraph.MaxHz);

    static readonly (double Hz, string Label)[] MajorTicks =
    [
        (20, "20"), (50, "50"), (100, "100"), (200, "200"), (500, "500"),
        (1000, "1k"), (2000, "2k"), (5000, "5k"), (10000, "10k"), (20000, "20k"),
    ];

    static readonly double[] MinorTicks = BuildMinorTicks();
    static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.OrdinalIgnoreCase);

    readonly Canvas _background = new() { IsHitTestVisible = false };
    readonly Canvas _curves = new() { IsHitTestVisible = false };
    readonly Canvas _handles = new() { IsHitTestVisible = false };
    readonly List<Polyline> _lines = [];
    readonly List<Shape> _handleShapes = [];

    EqGraphProps? _props;
    EqGraph.GraphPalette _palette = EqGraph.GraphPalette.Light;
    (double W, double H, EqGraph.GraphPalette? Palette) _backgroundFor;
    int _dragIndex = -1;

    public EqCanvas()
    {
        Children.Add(_background);
        Children.Add(_curves);
        Children.Add(_handles);
        SizeChanged += (_, _) => Redraw();
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += EndDrag;
        PointerCaptureLost += (_, _) => _dragIndex = -1;
    }

    public void Update(EqGraphProps props, EqGraph.GraphPalette palette)
    {
        _props = props;
        _palette = palette;
        Redraw();
    }

    // ── Coordinates ──────────────────────────────────────────────────────

    double PlotWidth => Math.Max(1, ActualWidth - Left - Right);
    double PlotHeight => Math.Max(1, ActualHeight - Top - Bottom);

    double X(double hz) => Left + (Math.Log10(hz) - LogMin) / (LogMax - LogMin) * PlotWidth;
    double Y(double db) => Top + (EqGraph.MaxGainDb - db) / (2 * EqGraph.MaxGainDb) * PlotHeight;
    double HzAt(double x) => Math.Pow(10, LogMin + (x - Left) / PlotWidth * (LogMax - LogMin));
    double DbAt(double y) => EqGraph.MaxGainDb - (y - Top) / PlotHeight * (2 * EqGraph.MaxGainDb);

    // ── Drawing ──────────────────────────────────────────────────────────

    void Redraw()
    {
        if (_props is null || ActualWidth <= Left + Right || ActualHeight <= Top + Bottom) return;
        if (_backgroundFor != (ActualWidth, ActualHeight, _palette))
        {
            DrawBackground();
            _backgroundFor = (ActualWidth, ActualHeight, _palette);
        }
        DrawCurves(_props.Series);
        DrawHandles(_props.EditableBands);
    }

    void DrawBackground()
    {
        var p = _palette;
        Background = Brush(p.Figure);
        var bg = _background.Children;
        bg.Clear();

        bg.Add(At(new Rectangle { Width = PlotWidth, Height = PlotHeight, Fill = Brush(p.Data) }, Left, Top));

        // Grid: minor frequency lines, then major frequency lines and the gain lines every 5 dB.
        foreach (var hz in MinorTicks)
            bg.Add(Line(X(hz), Top, X(hz), Top + PlotHeight, p.MinorGrid));
        foreach (var (hz, _) in MajorTicks)
            bg.Add(Line(X(hz), Top, X(hz), Top + PlotHeight, p.Grid));
        for (var db = -EqGraph.MaxGainDb; db <= EqGraph.MaxGainDb; db += 5)
            bg.Add(Line(Left, Y(db), Left + PlotWidth, Y(db), p.Grid));

        // Axes, tick labels and titles.
        bg.Add(Line(Left, Top + PlotHeight, Left + PlotWidth, Top + PlotHeight, p.Axis));
        bg.Add(Line(Left, Top, Left, Top + PlotHeight, p.Axis));
        foreach (var (hz, label) in MajorTicks)
            bg.Add(Text(label, X(hz) - 20, Top + PlotHeight + 4, 40, TextAlignment.Center, LabelFontSize, p.Axis));
        for (var db = -EqGraph.MaxGainDb; db <= EqGraph.MaxGainDb; db += 5)
            bg.Add(Text($"{db:0}", Left - 40, Y(db) - 8, 34, TextAlignment.Right, LabelFontSize, p.Axis));
        bg.Add(Text("Frequency (Hz)", Left, Top + PlotHeight + 22, PlotWidth, TextAlignment.Center, TitleFontSize,
            p.Axis, bold: true));
        var yTitle = Text("Gain (dB)", 2, Top + PlotHeight, PlotHeight, TextAlignment.Center, TitleFontSize, p.Axis,
            bold: true);
        yTitle.RenderTransform = new RotateTransform { Angle = -90 };
        bg.Add(yTitle);

        // The curve layer sits on the data area and clips to it.
        _curves.Width = PlotWidth;
        _curves.Height = PlotHeight;
        _curves.Clip = new RectangleGeometry { Rect = new Rect(0, 0, PlotWidth, PlotHeight) };
        SetLeft(_curves, Left);
        SetTop(_curves, Top);
    }

    void DrawCurves(IReadOnlyList<GraphSeries> series)
    {
        // Total last so it sits on top.
        var ordered = series.OrderBy(s => s.Style == GraphSeriesStyle.Total ? 1 : 0).ToList();
        while (_lines.Count < ordered.Count)
        {
            var line = new Polyline { StrokeLineJoin = PenLineJoin.Round };
            _lines.Add(line);
            _curves.Children.Add(line);
        }
        for (var i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            if (i >= ordered.Count)
            {
                line.Visibility = Visibility.Collapsed;
                continue;
            }
            var s = ordered[i];
            var ys = Response.Evaluate(s.Filters, Freqs);
            var points = new PointCollection();
            for (var j = 0; j < Freqs.Length; j++)
                points.Add(new Point(X(Freqs[j]) - Left, Y(ys[j]) - Top));
            line.Points = points;
            line.Stroke = Brush(EqGraph.SeriesHex(s.Style, _palette == EqGraph.GraphPalette.Dark));
            line.StrokeThickness = s.Style == GraphSeriesStyle.Total ? 3.5 : 1.5;
            line.Visibility = Visibility.Visible;
        }
    }

    void DrawHandles(IReadOnlyList<Filter> bands)
    {
        // Circle, or square when the band's frequency is locked. Recreate only when the shapes no
        // longer match (band added/removed, lock toggled); otherwise just move them.
        var matches = _handleShapes.Count == bands.Count
                      && bands.Select((b, i) => b.FrequencyLocked == _handleShapes[i] is Rectangle).All(x => x);
        if (!matches)
        {
            _handles.Children.Clear();
            _handleShapes.Clear();
            foreach (var b in bands)
            {
                Shape shape = b.FrequencyLocked ? new Rectangle() : new Ellipse();
                shape.Width = shape.Height = HandleSize;
                shape.StrokeThickness = 1.5;
                _handleShapes.Add(shape);
                _handles.Children.Add(shape);
            }
        }
        var fill = Brush(EqGraph.SeriesHex(GraphSeriesStyle.ProfilePreference, false));
        var outline = Brush(_palette.HandleOutline);
        for (var i = 0; i < bands.Count; i++)
        {
            var shape = _handleShapes[i];
            shape.Fill = fill;
            shape.Stroke = outline;
            SetLeft(shape, X(Math.Clamp(bands[i].FrequencyHz, EqGraph.MinHz, EqGraph.MaxHz)) - HandleSize / 2);
            SetTop(shape, Y(Math.Clamp(bands[i].GainDb, -EqGraph.MaxGainDb, EqGraph.MaxGainDb)) - HandleSize / 2);
        }
    }

    static UIElement At(UIElement e, double left, double top)
    {
        SetLeft(e, left);
        SetTop(e, top);
        return e;
    }

    static Line Line(double x1, double y1, double x2, double y2, string color) =>
        new() { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = Brush(color), StrokeThickness = 1 };

    static TextBlock Text(string text, double left, double top, double width, TextAlignment align, double size,
        string color, bool bold = false)
    {
        var t = new TextBlock
        {
            Text = text,
            Width = width,
            TextAlignment = align,
            FontSize = size,
            Foreground = Brush(color),
            FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        };
        At(t, left, top);
        return t;
    }

    /// <summary>One brush per colour, created once (UI thread only).</summary>
    static SolidColorBrush Brush(string hex)
    {
        if (!Brushes.TryGetValue(hex, out var brush))
        {
            var v = Convert.ToUInt32(hex.TrimStart('#'), 16);
            Brushes[hex] = brush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, (byte)(v >> 16), (byte)(v >> 8), (byte)v));
        }
        return brush;
    }

    static double[] BuildMinorTicks()
    {
        var list = new List<double>();
        for (var decade = 10.0; decade <= 10000; decade *= 10)
            for (var m = 2; m <= 9; m++)
            {
                var hz = decade * m;
                if (hz is >= EqGraph.MinHz and <= EqGraph.MaxHz && !MajorTicks.Any(t => t.Hz == hz))
                    list.Add(hz);
            }
        return list.ToArray();
    }

    // ── Dragging ─────────────────────────────────────────────────────────

    void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        var bands = _props?.EditableBands;
        if (bands is not { Count: > 0 }) return;
        var pt = e.GetCurrentPoint(this).Position;
        var best = -1;
        var bestDist = HandleHitRadius;
        for (var i = 0; i < bands.Count; i++)
        {
            var dx = X(Math.Clamp(bands[i].FrequencyHz, EqGraph.MinHz, EqGraph.MaxHz)) - pt.X;
            var dy = Y(Math.Clamp(bands[i].GainDb, -EqGraph.MaxGainDb, EqGraph.MaxGainDb)) - pt.Y;
            var d = Math.Sqrt(dx * dx + dy * dy);
            if (d <= bestDist) { best = i; bestDist = d; }
        }
        if (best < 0) return;
        _dragIndex = best;
        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    // Pointer moves arrive far faster than frames (a fast mouse reports hundreds per second) and each
    // band change re-renders the whole editor. So a move only records the position; the band is
    // updated at most once per rendered frame, from the latest position (and on release).
    Point? _pendingPoint;
    bool _frameHooked;

    void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragIndex < 0) return;
        _pendingPoint = e.GetCurrentPoint(this).Position;
        if (!_frameHooked)
        {
            CompositionTarget.Rendering += OnFrame;
            _frameHooked = true;
        }
        e.Handled = true;
    }

    void OnFrame(object? sender, object e)
    {
        CompositionTarget.Rendering -= OnFrame;
        _frameHooked = false;
        ApplyPendingDrag();
    }

    void ApplyPendingDrag()
    {
        var i = _dragIndex;
        var props = _props;
        if (_pendingPoint is not { } pt || props is null || i < 0 || i >= props.EditableBands.Count) return;
        _pendingPoint = null;
        var hz = Math.Clamp(HzAt(pt.X), EqGraph.MinHz, EqGraph.MaxHz);
        var gain = Math.Clamp(DbAt(pt.Y), -EqGraph.MaxGainDb, EqGraph.MaxGainDb);
        var old = props.EditableBands[i];
        var updated = old with
        {
            // A band with its frequency locked moves up and down only.
            FrequencyHz = old.FrequencyLocked ? old.FrequencyHz : Math.Round(hz, hz < 1000 ? 1 : 0),
            GainDb = Math.Round(gain, 1),
            Q = Math.Clamp(old.Q, 0.1, 20),
        };
        if (updated != old) props.OnBandChanged(i, updated);
    }

    void EndDrag(object sender, PointerRoutedEventArgs e)
    {
        if (_dragIndex < 0) return;
        if (_pendingPoint is null) _pendingPoint = e.GetCurrentPoint(this).Position;
        ApplyPendingDrag(); // the final position is never dropped
        if (_frameHooked)
        {
            CompositionTarget.Rendering -= OnFrame;
            _frameHooked = false;
        }
        _dragIndex = -1;
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }
}
