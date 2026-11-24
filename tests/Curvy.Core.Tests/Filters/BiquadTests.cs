using Curvy.Core.Filters;

namespace Curvy.Core.Tests.Filters;

public class BiquadTests
{
    [Fact] public void Peaking_AtCenter_EqualsGain() =>
        Assert.Equal(6.0, Biquad.MagnitudeDb(new(FilterType.Peaking, 1000, 6, 1.41), 1000), 2);

    [Fact] public void Peaking_FarAway_IsFlat() =>
        Assert.Equal(0.0, Biquad.MagnitudeDb(new(FilterType.Peaking, 1000, 6, 4), 50), 1);

    [Fact] public void LowShelf_BelowCorner_ReachesGain_AtCorner_HalfGain()
    {
        var f = new Filter(FilterType.LowShelf, 105, 8, 0.7);
        Assert.InRange(Biquad.MagnitudeDb(f, 20), 7.5, 8.5);
        Assert.Equal(4.0, Biquad.MagnitudeDb(f, 105), 1);
        Assert.Equal(0.0, Biquad.MagnitudeDb(f, 10000), 1);
    }

    [Fact] public void HighShelf_AboveCorner_ReachesGain() =>
        Assert.InRange(Biquad.MagnitudeDb(new(FilterType.HighShelf, 10000, -3, 0.7), 19000), -3.3, -2.5);

    [Fact] public void Response_IgnoresDisabled_AndSums()
    {
        var fs = new[] { new Filter(FilterType.Peaking, 1000, 3, 1), new Filter(FilterType.Peaking, 1000, 2, 1),
                         new Filter(FilterType.Peaking, 1000, 9, 1, Enabled: false) };
        Assert.Equal(5.0, Response.Evaluate(fs, new[] { 1000.0 })[0], 2);
    }

    [Fact] public void Grid_IsLogSpaced_WithExactEnds()
    {
        var g = FrequencyGrid.LogSpaced(3);
        Assert.Equal(new[] { 20.0, Math.Sqrt(20 * 20000.0), 20000.0 }, g, new DoubleTol(1e-6));
    }
}
