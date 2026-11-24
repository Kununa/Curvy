using Curvy.Core.Engine;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Curvy.Core.Tests.Engine;

public sealed class RepairGuardTests
{
    private readonly FakeTimeProvider _time = new();

    private void Advance(int seconds) => _time.Advance(TimeSpan.FromSeconds(seconds));

    [Fact]
    public void RepairsWithinLimit_AreAllowed()
    {
        var g = new RepairGuard(_time);
        Assert.True(g.TryAcquire());
        Advance(10);
        Assert.True(g.TryAcquire());
        Advance(10);
        Assert.True(g.TryAcquire());
        Assert.False(g.IsBlocked);
    }

    [Fact]
    public void FourthRepairWithin60s_IsBlocked_AndStaysBlocked()
    {
        var g = new RepairGuard(_time);
        for (var i = 0; i < 3; i++) Assert.True(g.TryAcquire());
        Advance(59);
        Assert.False(g.TryAcquire());
        Assert.True(g.IsBlocked);
        Advance(3600); // no automatic recovery: only a manual reset (or the next app start)
        Assert.False(g.TryAcquire());
    }

    [Fact]
    public void Window_Slides()
    {
        var g = new RepairGuard(_time);
        Assert.True(g.TryAcquire()); // t = 0
        Advance(30);
        Assert.True(g.TryAcquire()); // t = 30
        Assert.True(g.TryAcquire()); // t = 30
        Advance(30);
        Assert.True(g.TryAcquire()); // t = 60: the t = 0 repair left the window
        Assert.False(g.IsBlocked);
        Assert.False(g.TryAcquire()); // three within the last 60 s
    }

    [Fact]
    public void Reset_Unblocks_AndClearsHistory()
    {
        var g = new RepairGuard(_time);
        for (var i = 0; i < 3; i++) g.TryAcquire();
        Assert.False(g.TryAcquire());
        g.Reset();
        Assert.False(g.IsBlocked);
        for (var i = 0; i < 3; i++) Assert.True(g.TryAcquire());
    }
}
