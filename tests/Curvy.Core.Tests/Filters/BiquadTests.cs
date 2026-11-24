using Curvy.Core.Filters;
using System.Threading.Tasks;

namespace Curvy.Core.Tests.Filters;

public class BiquadTests
{
    [Test] public async Task Peaking_AtCenter_EqualsGain() =>
        await Assert.That(Biquad.MagnitudeDb(new(FilterType.Peaking, 1000, 6, 1.41), 1000)).IsEqualTo(6.0).Within(0.01);

    [Test] public async Task Peaking_FarAway_IsFlat() =>
        await Assert.That(Biquad.MagnitudeDb(new(FilterType.Peaking, 1000, 6, 4), 50)).IsEqualTo(0.0).Within(0.1);

    [Test] public async Task LowShelf_BelowCorner_ReachesGain_AtCorner_HalfGain()
    {
        var f = new Filter(FilterType.LowShelf, 105, 8, 0.7);
        await Assert.That(Biquad.MagnitudeDb(f, 20)).IsGreaterThanOrEqualTo(7.5).And.IsLessThanOrEqualTo(8.5);
        await Assert.That(Biquad.MagnitudeDb(f, 105)).IsEqualTo(4.0).Within(0.1);
        await Assert.That(Biquad.MagnitudeDb(f, 10000)).IsEqualTo(0.0).Within(0.1);
    }

    [Test] public async Task HighShelf_AboveCorner_ReachesGain() =>
        await Assert.That(Biquad.MagnitudeDb(new(FilterType.HighShelf, 10000, -3, 0.7), 19000)).IsGreaterThanOrEqualTo(-3.3).And.IsLessThanOrEqualTo(-2.5);

    [Test] public async Task Response_IgnoresDisabled_AndSums()
    {
        var fs = new[] { new Filter(FilterType.Peaking, 1000, 3, 1), new Filter(FilterType.Peaking, 1000, 2, 1),
                         new Filter(FilterType.Peaking, 1000, 9, 1, Enabled: false) };
        await Assert.That(Response.Evaluate(fs, new[] { 1000.0 })[0]).IsEqualTo(5.0).Within(0.01);
    }

    [Test] public async Task Grid_IsLogSpaced_WithExactEnds()
    {
        var g = FrequencyGrid.LogSpaced(3);
        double[] expectedG = [20.0, Math.Sqrt(20 * 20000.0), 20000.0];
        await Assert.That(g.Length).IsEqualTo(expectedG.Length);
        for (var i = 0; i < expectedG.Length; i++)
            await Assert.That(g[i]).IsEqualTo(expectedG[i]).Within(1e-6);
    }
}