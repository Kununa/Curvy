using Curvy.Core.Filters;
using Curvy.Core.Model;

namespace Curvy.Core.Tests.Model;

public class ToneControlsTests
{
    [Fact]
    public void Tilt_Plus6_IsAbout6dB_From20HzTo20k_AndFlatAtPivot()
    {
        var f = new ToneControls(TiltDb: 6).ToFilters().ToList();
        var r = Response.Evaluate(f, new[] { 20.0, 1000, 20000 });
        Assert.InRange(r[2] - r[0], 5.0, 6.5);
        Assert.InRange(r[1], -0.5, 0.5);
    }

    [Fact]
    public void ZeroTone_ProducesNoFilters() => Assert.Empty(new ToneControls().ToFilters());

    [Fact]
    public void Tilt_BecomesTwoShelvesAtPivot()
    {
        var f = new ToneControls(TiltDb: 4, TiltPivotHz: 800).ToFilters().ToList();
        Assert.Equal(2, f.Count);
        Assert.Equal(new Filter(FilterType.LowShelf, 800, -2, 0.5), f[0]);
        Assert.Equal(new Filter(FilterType.HighShelf, 800, 2, 0.5), f[1]);
    }

    [Fact]
    public void Preference_Disabled_YieldsNoFilters()
    {
        var p = new Preference(new ToneControls(BassDb: 3), [new Filter(FilterType.Peaking, 1000, 2, 1)], false);
        Assert.Empty(p.AllFilters());
    }

    [Fact]
    public void Preference_ToneThenBands()
    {
        var band = new Filter(FilterType.Peaking, 1000, 2, 1);
        var p = new Preference(new ToneControls(BassDb: 3), [band], true);
        var all = p.AllFilters().ToList();
        Assert.Equal(2, all.Count);
        Assert.Equal(FilterType.LowShelf, all[0].Type);
        Assert.Equal(band, all[1]);
    }
}
