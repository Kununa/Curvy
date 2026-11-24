using Curvy.Core.Engine;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Curvy.Core.Tests.Engine;

public sealed class ConfigChangeDebouncerTests
{
    private readonly FakeTimeProvider _time = new();
    private int _fired;

    private ConfigChangeDebouncer New() => new(_time, () => _fired++);

    private void Advance(int ms) => _time.Advance(TimeSpan.FromMilliseconds(ms));

    [Fact]
    public void Change_FiresOnceAfter500ms()
    {
        using var d = New();
        d.NotifyChanged();
        Advance(499);
        Assert.Equal(0, _fired);
        Advance(1);
        Assert.Equal(1, _fired);
        Advance(5000);
        Assert.Equal(1, _fired);
    }

    [Fact]
    public void Burst_IsCoalesced_AndRestartsTheDelay()
    {
        using var d = New();
        d.NotifyChanged();
        Advance(300);
        d.NotifyChanged();
        Advance(300);
        d.NotifyChanged();
        Advance(499);
        Assert.Equal(0, _fired);
        Advance(1);
        Assert.Equal(1, _fired);
    }

    [Fact]
    public void Change_WithinOneSecondOfOwnWrite_IsIgnored()
    {
        using var d = New();
        d.NotifyOwnWrite();
        Advance(999);
        d.NotifyChanged();
        Advance(2000);
        Assert.Equal(0, _fired);
    }

    [Fact]
    public void Change_OneSecondAfterOwnWrite_Fires()
    {
        using var d = New();
        d.NotifyOwnWrite();
        Advance(1000);
        d.NotifyChanged();
        Advance(500);
        Assert.Equal(1, _fired);
    }

    [Fact]
    public void OwnWrite_WhileDebouncing_DropsThePendingCheck()
    {
        // The watcher can report a write before Curvy marks it as its own.
        using var d = New();
        d.NotifyChanged();
        Advance(10);
        d.NotifyOwnWrite();
        Advance(490); // timer elapses now, 490 ms after the own write
        Assert.Equal(0, _fired);
        Advance(2000);
        Assert.Equal(0, _fired);
    }

    [Fact]
    public void Dispose_StopsPendingFire()
    {
        var d = New();
        d.NotifyChanged();
        d.Dispose();
        Advance(1000);
        d.NotifyChanged(); // no-op after dispose
        Advance(1000);
        Assert.Equal(0, _fired);
    }
}
