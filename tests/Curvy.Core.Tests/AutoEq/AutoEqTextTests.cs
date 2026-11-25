using Curvy.Core.AutoEq;

namespace Curvy.Core.Tests.AutoEq;

public sealed class AutoEqTextTests
{
    [Test]
    public async Task Join_SkipsEmptyParts()
    {
        await Assert.That(AutoEqText.Join("HD 600", "oratory1990", "")).IsEqualTo("HD 600 · oratory1990");
        await Assert.That(AutoEqText.Join("HD 600", "crinacle", "GRAS 43AG-7")).IsEqualTo("HD 600 · crinacle · GRAS 43AG-7");
        await Assert.That(AutoEqText.Join(null, " ", "x")).IsEqualTo("x");
    }
}
