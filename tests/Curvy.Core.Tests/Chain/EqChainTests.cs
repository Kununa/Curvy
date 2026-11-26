using System.Collections.Generic;
using System.Collections.Immutable;
using Curvy.Core.Chain;
using Curvy.Core.Filters;
using Curvy.Core.Model;
using System.Threading.Tasks;

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

    [Test]
    public async Task Chain_Order_AutoEq_Profile_Universal()
    {
        var r = EqChain.Build(MakeProfile(), MakeSettings());
        await Assert.That(r.Filters).IsEquivalentTo([A, P, U], EqualityComparer<Filter>.Default, CollectionOrdering.Matching);
        await Assert.That(r.Bypassed).IsFalse();
    }

    [Test]
    public async Task Chain_IgnoreUniversal_DropsUniversal()
    {
        var r = EqChain.Build(MakeProfile(ignoreUniversal: true), MakeSettings());
        await Assert.That(r.Filters).IsEquivalentTo([A, P], EqualityComparer<Filter>.Default, CollectionOrdering.Matching);
        await Assert.That(EqChain.Layer(MakeProfile(ignoreUniversal: true), MakeSettings(), ChainLayer.Universal)).IsEmpty();
    }

    [Test]
    public async Task Chain_AutoEqDisabled_DropsAutoEq()
    {
        var r = EqChain.Build(MakeProfile(autoEqEnabled: false), MakeSettings());
        await Assert.That(r.Filters).IsEquivalentTo([P, U], EqualityComparer<Filter>.Default, CollectionOrdering.Matching);
        await Assert.That(EqChain.Layer(MakeProfile(autoEqEnabled: false), MakeSettings(), ChainLayer.AutoEq)).IsEmpty();
    }

    [Test]
    public async Task Chain_NoActiveProfile_AppliesUniversalOnly()
    {
        var r = EqChain.Build(null, MakeSettings());
        await Assert.That(r.Filters).IsEquivalentTo([U], EqualityComparer<Filter>.Default, CollectionOrdering.Matching);
        await Assert.That(EqChain.Layer(null, MakeSettings(), ChainLayer.AutoEq)).IsEmpty();
        await Assert.That(EqChain.Layer(null, MakeSettings(), ChainLayer.ProfilePreference)).IsEmpty();
    }

    [Test]
    public async Task Chain_UniversalDisabled_DropsUniversal()
    {
        var s = MakeSettings() with { Universal = MakeSettings().Universal with { Enabled = false } };
        await Assert.That(EqChain.Layer(MakeProfile(), s, ChainLayer.Universal)).IsEmpty();
    }

    [Test]
    public async Task Chain_MasterOff_IsBypassed() =>
        await Assert.That(EqChain.Build(null, CurvySettings.Default with { MasterEnabled = false }).Bypassed).IsTrue();

    [Test]
    public async Task Preamp_StacksAllLayers()
    {
        var bass = new Preference(new ToneControls(BassDb: 12), [], true);
        var p = Profile.New("t") with { Preference = bass };
        var s = CurvySettings.Default with { Universal = bass };
        await Assert.That(EqChain.Build(p, s).PreampDb).IsGreaterThanOrEqualTo(-24.5).And.IsLessThanOrEqualTo(-23.5);
    }

    [Test]
    public async Task Preamp_IsZero_WhenAllCuts() =>
        await Assert.That(EqChain.ComputePreamp([new(FilterType.Peaking, 1000, -6, 1)], 0)).IsEqualTo(0);

    [Test]
    public async Task Preamp_CoversNarrowPeakBetweenGridPoints()
    {
        var narrow = new Filter(FilterType.Peaking, 3517, 9, 12);
        await Assert.That(EqChain.ComputePreamp([narrow], 0) <= -9.0).IsTrue();
    }

    [Test]
    public async Task Preamp_RoundsDownTo0_1_AndAddsHeadroom()
    {
        await Assert.That(EqChain.ComputePreamp([new(FilterType.Peaking, 1000, 5.93, 1)], 0)).IsEqualTo(-6.0);
        await Assert.That(EqChain.ComputePreamp([new(FilterType.Peaking, 1000, 5.93, 1)], 2)).IsEqualTo(-8.0);
    }
}