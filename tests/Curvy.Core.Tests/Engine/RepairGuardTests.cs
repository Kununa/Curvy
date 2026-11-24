using Curvy.Core.Engine;
using Microsoft.Extensions.Time.Testing;
using System.Threading.Tasks;

namespace Curvy.Core.Tests.Engine;

public sealed class RepairGuardTests
{
    private readonly FakeTimeProvider _time = new();

    private void Advance(int seconds) => _time.Advance(TimeSpan.FromSeconds(seconds));

    [Test]
    public async Task RepairsWithinLimit_AreAllowed()
    {
        var g = new RepairGuard(_time);
        await Assert.That(g.TryAcquire()).IsTrue();
        Advance(10);
        await Assert.That(g.TryAcquire()).IsTrue();
        Advance(10);
        await Assert.That(g.TryAcquire()).IsTrue();
        await Assert.That(g.IsBlocked).IsFalse();
    }

    [Test]
    public async Task FourthRepairWithin60s_IsBlocked_AndStaysBlocked()
    {
        var g = new RepairGuard(_time);
        for (var i = 0; i < 3; i++) await Assert.That(g.TryAcquire()).IsTrue();
        Advance(59);
        await Assert.That(g.TryAcquire()).IsFalse();
        await Assert.That(g.IsBlocked).IsTrue();
        Advance(3600); // no automatic recovery: only a manual reset (or the next app start)
        await Assert.That(g.TryAcquire()).IsFalse();
    }

    [Test]
    public async Task Window_Slides()
    {
        var g = new RepairGuard(_time);
        await Assert.That(g.TryAcquire()).IsTrue(); // t = 0
        Advance(30);
        await Assert.That(g.TryAcquire()).IsTrue(); // t = 30
        await Assert.That(g.TryAcquire()).IsTrue(); // t = 30
        Advance(30);
        await Assert.That(g.TryAcquire()).IsTrue(); // t = 60: the t = 0 repair left the window
        await Assert.That(g.IsBlocked).IsFalse();
        await Assert.That(g.TryAcquire()).IsFalse(); // three within the last 60 s
    }

    [Test]
    public async Task Reset_Unblocks_AndClearsHistory()
    {
        var g = new RepairGuard(_time);
        for (var i = 0; i < 3; i++) g.TryAcquire();
        await Assert.That(g.TryAcquire()).IsFalse();
        g.Reset();
        await Assert.That(g.IsBlocked).IsFalse();
        for (var i = 0; i < 3; i++) await Assert.That(g.TryAcquire()).IsTrue();
    }
}