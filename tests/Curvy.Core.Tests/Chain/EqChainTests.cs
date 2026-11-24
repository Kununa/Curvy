using System.Collections.Immutable;
using Curvy.Core.Chain;
using Curvy.Core.Filters;
using Curvy.Core.Model;

namespace Curvy.Core.Tests.Chain;

public class EqChainTests
{
    static readonly Filter A = new(FilterType.Peaking, 100, -1, 1);
    static readonly Filter P = new(FilterType.Peaking, 200, -2, 1);
    static readonly Filter U = new(FilterType.Peaking, 300, -3, 1);

    static Profile MakeProfile(bool autoEqEnabled = true, bool ignoreUniversal = false)
    {
        var src = new AutoEqSource("HD600", "oratory1990", "GRAS", "x/y", DateTimeOffset.UnixEpoch);
        var ae = new AutoEqCorrection(src, [A]);
        return Profile.New("t", ae) with
        {
            AutoEqEnabled = autoEqEnabled,
            IgnoreUniversal = ignoreUniversal,
            Preference = new Preference(new ToneControls(), [P], true),
        };
    }

    static CurvySettings MakeSettings() =>
        CurvySettings.Default with { Universal = new Preference(new ToneControls(), [U], true) };

    [Fact]
    public void Chain_Order_AutoEq_Profile_Universal()
    {
        var r = EqChain.Build(MakeProfile(), MakeSettings());
        Assert.Equal([A, P, U], r.Filters);
        Assert.False(r.Bypassed);
    }

    [Fact]
    public void Chain_IgnoreUniversal_DropsUniversal()
    {
        var r = EqChain.Build(MakeProfile(ignoreUniversal: true), MakeSettings());
        Assert.Equal([A, P], r.Filters);
        Assert.Empty(EqChain.Layer(MakeProfile(ignoreUniversal: true), MakeSettings(), ChainLayer.Universal));
    }

    [Fact]
    public void Chain_AutoEqDisabled_DropsAutoEq()
    {
        var r = EqChain.Build(MakeProfile(autoEqEnabled: false), MakeSettings());
        Assert.Equal([P, U], r.Filters);
        Assert.Empty(EqChain.Layer(MakeProfile(autoEqEnabled: false), MakeSettings(), ChainLayer.AutoEq));
    }

    [Fact]
    public void Chain_NoActiveProfile_AppliesUniversalOnly()
    {
        var r = EqChain.Build(null, MakeSettings());
        Assert.Equal([U], r.Filters);
        Assert.Empty(EqChain.Layer(null, MakeSettings(), ChainLayer.AutoEq));
        Assert.Empty(EqChain.Layer(null, MakeSettings(), ChainLayer.ProfilePreference));
    }

    [Fact]
    public void Chain_UniversalDisabled_DropsUniversal()
    {
        var s = MakeSettings() with { Universal = MakeSettings().Universal with { Enabled = false } };
        Assert.Empty(EqChain.Layer(MakeProfile(), s, ChainLayer.Universal));
    }

    [Fact]
    public void Chain_MasterOff_IsBypassed() =>
        Assert.True(EqChain.Build(null, CurvySettings.Default with { MasterEnabled = false }).Bypassed);

    [Fact]
    public void Preamp_StacksAllLayers()
    {
        var bass = new Preference(new ToneControls(BassDb: 12), [], true);
        var p = Profile.New("t") with { Preference = bass };
        var s = CurvySettings.Default with { Universal = bass };
        Assert.InRange(EqChain.Build(p, s).PreampDb, -24.5, -23.5);
    }

    [Fact]
    public void Preamp_IsZero_WhenAllCuts() =>
        Assert.Equal(0, EqChain.ComputePreamp([new(FilterType.Peaking, 1000, -6, 1)], 0));

    [Fact]
    public void Preamp_CoversNarrowPeakBetweenGridPoints()
    {
        var narrow = new Filter(FilterType.Peaking, 3517, 9, 12);
        Assert.True(EqChain.ComputePreamp([narrow], 0) <= -9.0);
    }

    [Fact]
    public void Preamp_RoundsDownTo0_1_AndAddsHeadroom()
    {
        Assert.Equal(-6.0, EqChain.ComputePreamp([new(FilterType.Peaking, 1000, 5.93, 1)], 0));
        Assert.Equal(-8.0, EqChain.ComputePreamp([new(FilterType.Peaking, 1000, 5.93, 1)], 2));
    }
}
