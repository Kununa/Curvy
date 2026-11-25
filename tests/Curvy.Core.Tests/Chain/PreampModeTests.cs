using Curvy.Core.Chain;
using Curvy.Core.Filters;
using Curvy.Core.Model;
using System.Threading.Tasks;

namespace Curvy.Core.Tests.Chain;

public class PreampModeTests
{
    static readonly Profile Boost = Profile.New("b") with
    {
        Preference = new Preference(new ToneControls(BassDb: 6), [], true),
    };

    [Test]
    public async Task Default_IsAuto()
    {
        await Assert.That(CurvySettings.Default.PreampMode).IsEqualTo(PreampMode.Auto);
    }

    [Test]
    public async Task Manual_UsesManualValue_IgnoringAutoAndHeadroom()
    {
        var s = CurvySettings.Default with { PreampMode = PreampMode.Manual, ManualPreampDb = -12, ExtraHeadroomDb = 3 };
        await Assert.That(EqChain.Build(Boost, s).PreampDb).IsEqualTo(-12);
    }

    [Test]
    public async Task Auto_StillComputesFromCurve()
    {
        var pre = EqChain.Build(Boost, CurvySettings.Default).PreampDb;
        await Assert.That(pre).IsLessThanOrEqualTo(-5.5).And.IsGreaterThanOrEqualTo(-6.5);
    }

    [Test]
    public async Task PeakGain_IsMaxOfCombinedCurve()
    {
        var peak = EqChain.PeakGainDb([new Filter(FilterType.Peaking, 1000, 4, 1)]);
        await Assert.That(peak).IsEqualTo(4).Within(0.05);
        await Assert.That(EqChain.PeakGainDb([])).IsEqualTo(0);
    }
}
