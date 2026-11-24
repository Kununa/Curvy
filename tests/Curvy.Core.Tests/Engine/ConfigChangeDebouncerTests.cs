using Curvy.Core.Engine;
using Microsoft.Extensions.Time.Testing;
using System.Threading.Tasks;

namespace Curvy.Core.Tests.Engine;

public sealed class ConfigChangeDebouncerTests
{
    private readonly FakeTimeProvider _time = new();
    private int _fired;

    private ConfigChangeDebouncer New() => new(_time, () => _fired++);

    private void Advance(int ms) => _time.Advance(TimeSpan.FromMilliseconds(ms));

    [Test]
    public async Task Change_FiresOnceAfter500ms()
    {
        using var d = New();
        d.NotifyChanged();
        Advance(499);
        await Assert.That(_fired).IsEqualTo(0);
        Advance(1);
        await Assert.That(_fired).IsEqualTo(1);
        Advance(5000);
        await Assert.That(_fired).IsEqualTo(1);
    }

    [Test]
    public async Task Burst_IsCoalesced_AndRestartsTheDelay()
    {
        using var d = New();
        d.NotifyChanged();
        Advance(300);
        d.NotifyChanged();
        Advance(300);
        d.NotifyChanged();
        Advance(499);
        await Assert.That(_fired).IsEqualTo(0);
        Advance(1);
        await Assert.That(_fired).IsEqualTo(1);
    }

    [Test]
    public async Task Change_200msAfterOwnWrite_FiresOnceAtWindowEnd()
    {
        using var d = New();
        d.NotifyOwnWrite();
        Advance(200);
        d.NotifyChanged();
        Advance(799);
        await Assert.That(_fired).IsEqualTo(0);
        Advance(1); // 1 s after the own write
        await Assert.That(_fired).IsEqualTo(1);
        Advance(5000);
        await Assert.That(_fired).IsEqualTo(1);
    }

    [Test]
    public async Task Change_LateInOwnWriteWindow_StillWaitsTheDebounce()
    {
        using var d = New();
        d.NotifyOwnWrite();
        Advance(999);
        d.NotifyChanged();
        Advance(499);
        await Assert.That(_fired).IsEqualTo(0);
        Advance(1);
        await Assert.That(_fired).IsEqualTo(1);
    }

    [Test]
    public async Task Change_OneSecondAfterOwnWrite_Fires()
    {
        using var d = New();
        d.NotifyOwnWrite();
        Advance(1000);
        d.NotifyChanged();
        Advance(500);
        await Assert.That(_fired).IsEqualTo(1);
    }

    [Test]
    public async Task OwnWrite_WhileDebouncing_DefersThePendingCheck()
    {
        // The watcher can report a write before Curvy marks it as its own; a real external change
        // could also coincide with it, so the check is deferred to the window's end, never dropped.
        using var d = New();
        d.NotifyChanged();
        Advance(10);
        d.NotifyOwnWrite();
        Advance(490); // timer elapses now, 490 ms after the own write
        await Assert.That(_fired).IsEqualTo(0);
        Advance(509);
        await Assert.That(_fired).IsEqualTo(0);
        Advance(1); // 1 s after the own write
        await Assert.That(_fired).IsEqualTo(1);
        Advance(5000);
        await Assert.That(_fired).IsEqualTo(1);
    }

    [Test]
    public async Task Dispose_StopsPendingFire()
    {
        var d = New();
        d.NotifyChanged();
        d.Dispose();
        Advance(1000);
        d.NotifyChanged(); // no-op after dispose
        Advance(1000);
        await Assert.That(_fired).IsEqualTo(0);
    }
}