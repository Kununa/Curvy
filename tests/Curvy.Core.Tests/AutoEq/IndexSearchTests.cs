using Curvy.Core.AutoEq;
using System.Threading.Tasks;

namespace Curvy.Core.Tests.AutoEq;

public class IndexSearchTests
{
    private const string Md = """
# Index
This is a list of all equalization profiles. Target is in parentheses if there are results with multiple targets
from the same source.

- [Beyerdynamic DT 880 Black Edition](./crinacle/GRAS%2043AG-7%20over-ear/Beyerdynamic%20DT%20880%20Black%20Edition) by crinacle on GRAS 43AG-7
- [Beyerdynamic DT 880](./Auriculares%20Argentina/over-ear/Beyerdynamic%20DT%20880) by Auriculares Argentina
- [Beyerdynamic DT 880](./Rtings/HMS%20II.3%20over-ear/Beyerdynamic%20DT%20880) by Rtings on HMS II.3
- [Beyerdynamic DT 880](./oratory1990/over-ear/Beyerdynamic%20DT%20880) by oratory1990
- [1MORE Aero (ANC Off)](./HypetheSonics/GRAS%20RA0045%20in-ear/1MORE%20Aero%20(ANC%20Off)) by HypetheSonics on GRAS RA0045
- [broken line
""";

    private static IReadOnlyList<AutoEqIndexEntry> Index() => AutoEqIndexParser.Parse(Md);

    [Test]
    public async Task Parse_HandlesParenthesesInNameAndPath()
    {
        var e = Index().Single(x => x.Name.StartsWith("1MORE"));
        await Assert.That(e.Name).IsEqualTo("1MORE Aero (ANC Off)");
        await Assert.That(e.Rig).IsEqualTo("GRAS RA0045");
        await Assert.That(e.Source).IsEqualTo("HypetheSonics");
    }

    [Test]
    public async Task Parse_SkipsNonEntryLines() => await Assert.That(Index().Count).IsEqualTo(5);

    [Test]
    public async Task Parse_SourceDecoded_RigEmptyWithoutOn()
    {
        var e = Index().First(x => x.Source == "Auriculares Argentina");
        await Assert.That(e.Rig).IsEqualTo("");
    }

    [Test]
    public async Task ParametricEqUrl_IsEncoded()
    {
        var e = Index().Single(x => x.Source == "oratory1990");
        await Assert.That(e.ParametricEqRelativeUrl).IsEqualTo("oratory1990/over-ear/Beyerdynamic%20DT%20880/Beyerdynamic%20DT%20880%20ParametricEQ.txt");
    }

    [Test]
    public async Task Search_GroupsByName_DefaultIsOratoryThenCrinacle()
    {
        var r = AutoEqSearch.Search(Index(), "dt 880");
        await Assert.That(r[0].Name).IsEqualTo("Beyerdynamic DT 880");
        await Assert.That(r[0].Results.Count).IsEqualTo(3);
        await Assert.That(r[0].Results[0].Source).IsEqualTo("oratory1990");
        await Assert.That(r[0].Results[1].Source).IsEqualTo("Auriculares Argentina");
    }

    [Test]
    public async Task Search_ExactBeforePrefixBeforeTokens()
    {
        var r = AutoEqSearch.Search(Index(), "Beyerdynamic DT 880");
        await Assert.That(r.Select(x => x.Name).ToArray()).IsEquivalentTo(["Beyerdynamic DT 880", "Beyerdynamic DT 880 Black Edition"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Search_IsCaseAndOrderInsensitive_OnTokens()
    {
        var r = AutoEqSearch.Search(Index(), "880 BEYER");
        await Assert.That(r.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Search_EmptyQuery_ReturnsEmpty()
    {
        await Assert.That(AutoEqSearch.Search(Index(), "  ")).IsEmpty();
        await Assert.That(AutoEqSearch.Search(Index(), "")).IsEmpty();
    }

    [Test]
    public async Task Search_MaxLimitsGroups()
    {
        await Assert.That(AutoEqSearch.Search(Index(), "880", 1)).HasSingleItem();
    }
}