using Curvy.Core.Engine;
using System.Threading.Tasks;

namespace Curvy.Core.Tests.Engine;

public sealed class TrayTextTests
{
    [Test]
    [Arguments("HD 600", true, false, "Curvy – HD 600")]
    [Arguments(null, true, false, "Curvy – No profile")]
    [Arguments("HD 600", false, false, "Curvy – EQ off")]
    [Arguments("HD 600", true, true, "Curvy – Disconnected")]
    [Arguments(null, false, true, "Curvy – Disconnected")]
    public async Task Tooltip_Variants(string? name, bool master, bool disconnected, string expected) =>
        await Assert.That(TrayText.Tooltip(name, master, disconnected)).IsEqualTo(expected);

    [Test]
    public async Task Tooltip_LongName_TruncatedTo127WithEllipsis()
    {
        var t = TrayText.Tooltip(new string('x', 300), true, false);
        await Assert.That(t.Length).IsEqualTo(TrayText.MaxTooltipLength);
        await Assert.That(t).EndsWith("x…");
    }

    [Test]
    public async Task Tooltip_ExactlyAtLimit_NotTruncated()
    {
        var name = new string('x', TrayText.MaxTooltipLength - "Curvy – ".Length);
        await Assert.That(TrayText.Tooltip(name, true, false)).IsEqualTo("Curvy – " + name);
    }

    [Test]
    public async Task Tooltip_DoesNotSplitSurrogatePair()
    {
        // Prefix (8) + 117 'x' = 125 chars, then an emoji (2 UTF-16 units) straddling the cut at 126.
        var name = new string('x', 117) + "🎧" + new string('y', 50);
        var t = TrayText.Tooltip(name, true, false);
        await Assert.That(t.Length <= TrayText.MaxTooltipLength).IsTrue();
        await Assert.That(t).EndsWith("x…");
        await Assert.That(char.IsHighSurrogate(t[^2])).IsFalse(); // no lone high surrogate before the ellipsis
    }
}