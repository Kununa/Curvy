using Curvy.Core.Filters;
using Curvy.Core.Model;
using System.Threading.Tasks;

namespace Curvy.Core.Tests.Model;

public class ToneControlsTests
{
    [Test]
    public async Task Tilt_Plus6_IsAbout6dB_From20HzTo20k_AndFlatAtPivot()
    {
        var f = new ToneControls(TiltDb: 6).ToFilters().ToList();
        var r = Response.Evaluate(f, new[] { 20.0, 1000, 20000 });
        await Assert.That(r[2] - r[0]).IsGreaterThanOrEqualTo(5.0).And.IsLessThanOrEqualTo(6.5);
        await Assert.That(r[1]).IsGreaterThanOrEqualTo(-0.5).And.IsLessThanOrEqualTo(0.5);
    }

    [Test]
    public async Task ZeroTone_ProducesNoFilters() => await Assert.That(new ToneControls().ToFilters()).IsEmpty();

    [Test]
    public async Task Tilt_BecomesTwoShelvesAtPivot()
    {
        var f = new ToneControls(TiltDb: 4, TiltPivotHz: 800).ToFilters().ToList();
        await Assert.That(f.Count).IsEqualTo(2);
        await Assert.That(f[0]).IsEqualTo(new Filter(FilterType.LowShelf, 800, -2, 0.5));
        await Assert.That(f[1]).IsEqualTo(new Filter(FilterType.HighShelf, 800, 2, 0.5));
    }

    [Test]
    public async Task Preference_Disabled_YieldsNoFilters()
    {
        var p = new Preference(new ToneControls(BassDb: 3), [new Filter(FilterType.Peaking, 1000, 2, 1)], false);
        await Assert.That(p.AllFilters()).IsEmpty();
    }

    [Test]
    public async Task Preference_ToneThenBands()
    {
        var band = new Filter(FilterType.Peaking, 1000, 2, 1);
        var p = new Preference(new ToneControls(BassDb: 3), [band], true);
        var all = p.AllFilters().ToList();
        await Assert.That(all.Count).IsEqualTo(2);
        await Assert.That(all[0].Type).IsEqualTo(FilterType.LowShelf);
        await Assert.That(all[1]).IsEqualTo(band);
    }
}