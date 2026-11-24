using Curvy.Core.Engine;
using Xunit;

namespace Curvy.Core.Tests.Engine;

public sealed class TrayTextTests
{
    [Theory]
    [InlineData("HD 600", true, false, "Curvy – HD 600")]
    [InlineData(null, true, false, "Curvy – No profile")]
    [InlineData("HD 600", false, false, "Curvy – EQ off")]
    [InlineData("HD 600", true, true, "Curvy – Disconnected")]
    [InlineData(null, false, true, "Curvy – Disconnected")]
    public void Tooltip_Variants(string? name, bool master, bool disconnected, string expected) =>
        Assert.Equal(expected, TrayText.Tooltip(name, master, disconnected));

    [Fact]
    public void Tooltip_LongName_TruncatedTo127WithEllipsis()
    {
        var t = TrayText.Tooltip(new string('x', 300), true, false);
        Assert.Equal(TrayText.MaxTooltipLength, t.Length);
        Assert.EndsWith("x…", t);
    }

    [Fact]
    public void Tooltip_ExactlyAtLimit_NotTruncated()
    {
        var name = new string('x', TrayText.MaxTooltipLength - "Curvy – ".Length);
        Assert.Equal("Curvy – " + name, TrayText.Tooltip(name, true, false));
    }

    [Fact]
    public void Tooltip_DoesNotSplitSurrogatePair()
    {
        // Prefix (8) + 117 'x' = 125 chars, then an emoji (2 UTF-16 units) straddling the cut at 126.
        var name = new string('x', 117) + "🎧" + new string('y', 50);
        var t = TrayText.Tooltip(name, true, false);
        Assert.True(t.Length <= TrayText.MaxTooltipLength);
        Assert.EndsWith("x…", t);
        Assert.False(char.IsHighSurrogate(t[^2])); // no lone high surrogate before the ellipsis
    }
}
