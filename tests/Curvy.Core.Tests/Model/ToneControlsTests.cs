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
    public async Task Preference_ModesAreExclusive()
    {
        var band = new Filter(FilterType.Peaking, 1000, 2, 1);
        var simple = new Preference(new ToneControls(BassDb: 3), [band], true, PreferenceMode.Simple);
        var simpleFilters = simple.AllFilters().ToList();
        await Assert.That(simpleFilters.Count).IsEqualTo(1);
        await Assert.That(simpleFilters[0].Type).IsEqualTo(FilterType.LowShelf);
        await Assert.That(simple.ActiveBands).IsEmpty();

        var parametric = simple with { Mode = PreferenceMode.Parametric };
        await Assert.That(parametric.AllFilters().ToList()).IsEquivalentTo([band]);
        await Assert.That(parametric.ActiveBands).IsEquivalentTo([band]);
    }

    [Test]
    public async Task Preference_WithoutMode_InfersFromBands()
    {
        var band = new Filter(FilterType.Peaking, 1000, 2, 1);
        await Assert.That(new Preference(new ToneControls(BassDb: 3), [band]).EffectiveMode).IsEqualTo(PreferenceMode.Parametric);
        await Assert.That(new Preference(new ToneControls(BassDb: 3), [band with { GainDb = 0 }]).EffectiveMode).IsEqualTo(PreferenceMode.Simple);
        await Assert.That(new Preference(new ToneControls(), []).EffectiveMode).IsEqualTo(PreferenceMode.Simple);
    }

    [Test]
    public async Task Preference_Empty_HasFlatDefaultBands_AndNoFilters()
    {
        var e = Preference.Empty;
        await Assert.That(e.Bands.Count).IsEqualTo(Preference.DefaultBands.Count);
        await Assert.That(e.Bands.All(b => b.GainDb == 0)).IsTrue();
        await Assert.That(e.EffectiveMode).IsEqualTo(PreferenceMode.Simple);
        await Assert.That(e.AllFilters()).IsEmpty();
        await Assert.That((e with { Mode = PreferenceMode.Parametric }).AllFilters()).IsEmpty();
    }

    [Test]
    public async Task NewProfile_PreferenceIsOffByDefault()
    {
        var p = Profile.New("X");
        await Assert.That(p.Preference.Enabled).IsFalse();
        await Assert.That(p.Preference.Bands.Count).IsEqualTo(Preference.DefaultBands.Count);
    }

    [Test]
    public async Task BandsAt_CopiesTypeFrequencyAndQ_FlatAndLocked_SortedByFrequency()
    {
        var bands = Preference.BandsAt([
            new Filter(FilterType.HighShelf, 10000, -3.1, 0.7),
            new Filter(FilterType.LowShelf, 105, 6.5, 0.7, Enabled: false),
            new Filter(FilterType.Peaking, 2000, 2, 1.4),
        ]);
        await Assert.That(bands.Select(b => b.FrequencyHz).ToList()).IsEquivalentTo([105.0, 2000.0, 10000.0]);
        await Assert.That(bands[0].Type).IsEqualTo(FilterType.LowShelf);
        await Assert.That(bands[1].Q).IsEqualTo(1.4);
        await Assert.That(bands.All(b => b.GainDb == 0 && b.Enabled && b.FrequencyLocked)).IsTrue();
    }
}
