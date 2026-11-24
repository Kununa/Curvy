using Curvy.Core.Apo;
using Curvy.Core.Chain;
using Curvy.Core.Filters;
using static Curvy.Core.Tests.CultureScope;

namespace Curvy.Core.Tests.Apo;

public class ParserFormatterTests
{
    const string AutoEq = "Preamp: -6.4 dB\nFilter 1: ON LSC Fc 105 Hz Gain 7.4 dB Q 0.70\nFilter 2: ON PK Fc 5760 Hz Gain -6.1 dB Q 4.97\nFilter 3: OFF HSC Fc 10000 Hz Gain -3.1 dB Q 0.70\nFilter 4: ON BP Fc 100 Hz\n";

    [Fact]
    public void Parser_ReadsAutoEqFile()
    {
        var f = ParametricEqParser.Parse(AutoEq);
        Assert.Equal(3, f.Count);
        Assert.Equal(new Filter(FilterType.LowShelf, 105, 7.4, 0.70), f[0]);
        Assert.False(f[2].Enabled);
    }

    [Fact]
    public void Parser_UsesInvariantCulture_UnderGermanLocale() =>
        WithCulture("de-DE", () => Assert.Equal(7.4, ParametricEqParser.Parse(AutoEq)[0].GainDb));

    [Fact]
    public void Parser_ToleratesCrlfWhitespaceCaseAndMalformedLines()
    {
        var text = "# c\r\nPreamp: -1 dB\r\n  filter 1:   on   pk  fc 1000 hz gain 2.5 db q 1.41  \r\nFilter 2: ON PK Fc abc Hz Gain 1 dB Q 1\r\ngarbage\r\nFilter 3: ON PEQ Fc 50 Hz Gain 1 dB Q 0.5\r\nFilter 4: ON HS Fc 9000 Hz Gain 1 dB Q 0.7\r\nFilter 5: ON LS Fc 80 Hz Gain 1 dB Q 0.7\r\n";
        var f = ParametricEqParser.Parse(text);
        Assert.Equal(4, f.Count);
        Assert.Equal(new Filter(FilterType.Peaking, 1000, 2.5, 1.41), f[0]);
        Assert.Equal(FilterType.Peaking, f[1].Type);
        Assert.Equal(FilterType.HighShelf, f[2].Type);
        Assert.Equal(FilterType.LowShelf, f[3].Type);
    }

    [Fact]
    public void Formatter_WritesExactApoText()
    {
        var r = new ChainResult(false, -6.5, [new(FilterType.LowShelf, 105, 7.4, 0.7), new(FilterType.Peaking, 5760.5, -6.1, 4.97),
                                              new(FilterType.Peaking, 1000, 3, 1, Enabled: false)]);
        Assert.Equal(ApoFormatter.Header + "\r\nPreamp: -6.5 dB\r\nFilter 1: ON LSC Fc 105 Hz Gain 7.4 dB Q 0.70\r\nFilter 2: ON PK Fc 5760.5 Hz Gain -6.1 dB Q 4.97\r\n",
                     ApoFormatter.Format(r));
    }

    [Fact]
    public void Formatter_UsesInvariantCulture_UnderGermanLocale() =>
        WithCulture("de-DE", () => Assert.DoesNotContain(",", ApoFormatter.Format(new ChainResult(false, -1.5, [new(FilterType.Peaking, 1000, 2.5, 1.41)]))));

    [Fact]
    public void Formatter_Bypassed_WritesHeaderAndOffMarkerOnly() =>
        Assert.Equal(ApoFormatter.Header + "\r\n# EQ off\r\n", ApoFormatter.Format(new ChainResult(true, 0, [])));

    [Fact]
    public void RoundTrip_FormatThenParse_PreservesFilters()
    {
        Filter[] input =
        [
            new(FilterType.LowShelf, 105, 7.4, 0.7),
            new(FilterType.Peaking, 5760.5, -6.1, 4.97),
            new(FilterType.HighShelf, 10000, -3.1, 0.71),
        ];
        var text = ApoFormatter.Format(new ChainResult(false, -4, [.. input]));
        Assert.Equal(input, ParametricEqParser.Parse(text));
    }
}
