using Curvy.Core.AutoEq;
using Xunit;

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

    [Fact]
    public void Parse_HandlesParenthesesInNameAndPath()
    {
        var e = Index().Single(x => x.Name.StartsWith("1MORE"));
        Assert.Equal("1MORE Aero (ANC Off)", e.Name);
        Assert.Equal("GRAS RA0045", e.Rig);
        Assert.Equal("HypetheSonics", e.Source);
    }

    [Fact]
    public void Parse_SkipsNonEntryLines() => Assert.Equal(5, Index().Count);

    [Fact]
    public void Parse_SourceDecoded_RigEmptyWithoutOn()
    {
        var e = Index().First(x => x.Source == "Auriculares Argentina");
        Assert.Equal("", e.Rig);
    }

    [Fact]
    public void ParametricEqUrl_IsEncoded()
    {
        var e = Index().Single(x => x.Source == "oratory1990");
        Assert.Equal("oratory1990/over-ear/Beyerdynamic%20DT%20880/Beyerdynamic%20DT%20880%20ParametricEQ.txt", e.ParametricEqRelativeUrl);
    }

    [Fact]
    public void Search_GroupsByName_DefaultIsOratoryThenCrinacle()
    {
        var r = AutoEqSearch.Search(Index(), "dt 880");
        Assert.Equal("Beyerdynamic DT 880", r[0].Name);
        Assert.Equal(3, r[0].Results.Count);
        Assert.Equal("oratory1990", r[0].Results[0].Source);
        Assert.Equal("Auriculares Argentina", r[0].Results[1].Source);
    }

    [Fact]
    public void Search_ExactBeforePrefixBeforeTokens()
    {
        var r = AutoEqSearch.Search(Index(), "Beyerdynamic DT 880");
        Assert.Equal(["Beyerdynamic DT 880", "Beyerdynamic DT 880 Black Edition"], r.Select(x => x.Name).ToArray());
    }

    [Fact]
    public void Search_IsCaseAndOrderInsensitive_OnTokens()
    {
        var r = AutoEqSearch.Search(Index(), "880 BEYER");
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        Assert.Empty(AutoEqSearch.Search(Index(), "  "));
        Assert.Empty(AutoEqSearch.Search(Index(), ""));
    }

    [Fact]
    public void Search_MaxLimitsGroups()
    {
        Assert.Single(AutoEqSearch.Search(Index(), "880", 1));
    }
}
