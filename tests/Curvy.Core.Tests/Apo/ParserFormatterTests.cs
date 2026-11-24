using Curvy.Core.Apo;
using Curvy.Core.Chain;
using Curvy.Core.Filters;
using static Curvy.Core.Tests.CultureScope;
using System.Threading.Tasks;

namespace Curvy.Core.Tests.Apo;

public class ParserFormatterTests
{
    const string AutoEq = "Preamp: -6.4 dB\nFilter 1: ON LSC Fc 105 Hz Gain 7.4 dB Q 0.70\nFilter 2: ON PK Fc 5760 Hz Gain -6.1 dB Q 4.97\nFilter 3: OFF HSC Fc 10000 Hz Gain -3.1 dB Q 0.70\nFilter 4: ON BP Fc 100 Hz\n";

    [Test]
    public async Task Parser_ReadsAutoEqFile()
    {
        var f = ParametricEqParser.Parse(AutoEq);
        await Assert.That(f.Count).IsEqualTo(3);
        await Assert.That(f[0]).IsEqualTo(new Filter(FilterType.LowShelf, 105, 7.4, 0.70));
        await Assert.That(f[2].Enabled).IsFalse();
    }

    [Test]
    public async Task Parser_UsesInvariantCulture_UnderGermanLocale()
    {
        var gain = 0.0;
        WithCulture("de-DE", () => gain = ParametricEqParser.Parse(AutoEq)[0].GainDb);
        await Assert.That(gain).IsEqualTo(7.4);
    }

    [Test]
    public async Task Parser_ToleratesCrlfWhitespaceCaseAndMalformedLines()
    {
        var text = "# c\r\nPreamp: -1 dB\r\n  filter 1:   on   pk  fc 1000 hz gain 2.5 db q 1.41  \r\nFilter 2: ON PK Fc abc Hz Gain 1 dB Q 1\r\ngarbage\r\nFilter 3: ON PEQ Fc 50 Hz Gain 1 dB Q 0.5\r\nFilter 4: ON HS Fc 9000 Hz Gain 1 dB Q 0.7\r\nFilter 5: ON LS Fc 80 Hz Gain 1 dB Q 0.7\r\n";
        var f = ParametricEqParser.Parse(text);
        await Assert.That(f.Count).IsEqualTo(4);
        await Assert.That(f[0]).IsEqualTo(new Filter(FilterType.Peaking, 1000, 2.5, 1.41));
        await Assert.That(f[1].Type).IsEqualTo(FilterType.Peaking);
        await Assert.That(f[2].Type).IsEqualTo(FilterType.HighShelf);
        await Assert.That(f[3].Type).IsEqualTo(FilterType.LowShelf);
    }

    [Test]
    public async Task Formatter_WritesExactApoText()
    {
        var r = new ChainResult(false, -6.5, [new(FilterType.LowShelf, 105, 7.4, 0.7), new(FilterType.Peaking, 5760.5, -6.1, 4.97),
                                              new(FilterType.Peaking, 1000, 3, 1, Enabled: false)]);
        await Assert.That(ApoFormatter.Format(r)).IsEqualTo(ApoFormatter.Header + "\r\nPreamp: -6.5 dB\r\nFilter 1: ON LSC Fc 105 Hz Gain 7.4 dB Q 0.70\r\nFilter 2: ON PK Fc 5760.5 Hz Gain -6.1 dB Q 4.97\r\n");
    }

    [Test]
    public async Task Formatter_UsesInvariantCulture_UnderGermanLocale()
    {
        var text = "";
        WithCulture("de-DE", () => text = ApoFormatter.Format(new ChainResult(false, -1.5, [new(FilterType.Peaking, 1000, 2.5, 1.41)])));
        await Assert.That(text).DoesNotContain(",");
    }

    [Test]
    public async Task Formatter_Bypassed_WritesHeaderAndOffMarkerOnly() =>
        await Assert.That(ApoFormatter.Format(new ChainResult(true, 0, []))).IsEqualTo(ApoFormatter.Header + "\r\n# EQ off\r\n");

    [Test]
    public async Task RoundTrip_FormatThenParse_PreservesFilters()
    {
        Filter[] input =
        [
            new(FilterType.LowShelf, 105, 7.4, 0.7),
            new(FilterType.Peaking, 5760.5, -6.1, 4.97),
            new(FilterType.HighShelf, 10000, -3.1, 0.71),
        ];
        var text = ApoFormatter.Format(new ChainResult(false, -4, [.. input]));
        await Assert.That(ParametricEqParser.Parse(text)).IsEquivalentTo(input, CollectionOrdering.Matching);
    }
}