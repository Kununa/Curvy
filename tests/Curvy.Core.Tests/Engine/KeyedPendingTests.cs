using System.Collections.Generic;
using Curvy.Core.Engine;
using System.Threading.Tasks;

namespace Curvy.Core.Tests.Engine;

public class KeyedPendingTests
{
    [Test]
    public async Task SameKey_LatestWins_OtherKeysKept_InFirstInsertionOrder()
    {
        var log = new List<string>();
        var p = new KeyedPending();
        p.Set("profile:a", () => log.Add("a1"));
        p.Set("universal", () => log.Add("u"));
        p.Set("profile:a", () => log.Add("a2"));
        p.Set("profile:b", () => log.Add("b"));

        await Assert.That(p.Contains("profile:a")).IsTrue();
        await Assert.That(p.Contains("settings")).IsFalse();
        foreach (var action in p.TakeAll()) action();

        await Assert.That(log).IsEquivalentTo(["a2", "u", "b"], EqualityComparer<string>.Default, CollectionOrdering.Matching);
        await Assert.That(p.IsEmpty).IsTrue();
    }

    [Test]
    public async Task TakeAll_ClearsBeforeRunning_SoReentrantSetIsKeptForNextRound()
    {
        var p = new KeyedPending();
        var ran = 0;
        p.Set("x", () => { ran++; p.Set("x", () => ran += 10); });

        foreach (var action in p.TakeAll()) action();
        await Assert.That(ran).IsEqualTo(1);
        await Assert.That(p.IsEmpty).IsFalse();

        foreach (var action in p.TakeAll()) action();
        await Assert.That(ran).IsEqualTo(11);
        await Assert.That(p.IsEmpty).IsTrue();
    }

    [Test]
    public async Task Empty_TakeAll_ReturnsNothing()
    {
        await Assert.That(new KeyedPending().TakeAll()).IsEmpty();
    }
}