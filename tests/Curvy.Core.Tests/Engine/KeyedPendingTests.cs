using Curvy.Core.Engine;
using Xunit;

namespace Curvy.Core.Tests.Engine;

public class KeyedPendingTests
{
    [Fact]
    public void SameKey_LatestWins_OtherKeysKept_InFirstInsertionOrder()
    {
        var log = new List<string>();
        var p = new KeyedPending();
        p.Set("profile:a", () => log.Add("a1"));
        p.Set("universal", () => log.Add("u"));
        p.Set("profile:a", () => log.Add("a2"));
        p.Set("profile:b", () => log.Add("b"));

        Assert.True(p.Contains("profile:a"));
        Assert.False(p.Contains("settings"));
        foreach (var action in p.TakeAll()) action();

        Assert.Equal(["a2", "u", "b"], log);
        Assert.True(p.IsEmpty);
    }

    [Fact]
    public void TakeAll_ClearsBeforeRunning_SoReentrantSetIsKeptForNextRound()
    {
        var p = new KeyedPending();
        var ran = 0;
        p.Set("x", () => { ran++; p.Set("x", () => ran += 10); });

        foreach (var action in p.TakeAll()) action();
        Assert.Equal(1, ran);
        Assert.False(p.IsEmpty);

        foreach (var action in p.TakeAll()) action();
        Assert.Equal(11, ran);
        Assert.True(p.IsEmpty);
    }

    [Fact]
    public void Empty_TakeAll_ReturnsNothing()
    {
        Assert.Empty(new KeyedPending().TakeAll());
    }
}
