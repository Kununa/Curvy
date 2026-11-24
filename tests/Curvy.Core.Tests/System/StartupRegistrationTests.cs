using Curvy.Core.SystemIntegration;
using Microsoft.Win32;
using System.Threading.Tasks;

namespace Curvy.Core.Tests.SystemIntegration;

/// <summary>
/// Runs against a throwaway <c>HKCU\Software\CurvyTests\&lt;guid&gt;</c> root (never the real Run key)
/// and deletes it afterwards.
/// </summary>
public sealed class StartupRegistrationTests : IDisposable
{
    private readonly string _rootPath = @"Software\CurvyTests\" + Guid.NewGuid().ToString("N");
    private readonly RegistryKey _root;

    public StartupRegistrationTests() => _root = Registry.CurrentUser.CreateSubKey(_rootPath);

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false);
        using var parent = Registry.CurrentUser.OpenSubKey(@"Software\CurvyTests", writable: true);
        if (parent is { SubKeyCount: 0, ValueCount: 0 })
            Registry.CurrentUser.DeleteSubKey(@"Software\CurvyTests", throwOnMissingSubKey: false);
    }

    private object? RunValue()
    {
        using var run = _root.OpenSubKey(StartupRegistration.RunKeyPath);
        return run?.GetValue(StartupRegistration.ValueName);
    }

    [Test]
    public async Task Enable_WritesQuotedExeWithTrayArg()
    {
        new StartupRegistration(_root).Enable(@"C:\Apps\Curvy\Curvy.App.exe");
        await Assert.That(RunValue()).IsEqualTo("\"C:\\Apps\\Curvy\\Curvy.App.exe\" --tray");
    }

    [Test]
    public async Task Disable_RemovesValue()
    {
        var reg = new StartupRegistration(_root);
        reg.Enable(@"C:\Curvy.exe");
        reg.Disable();
        await Assert.That(RunValue()).IsNull();
        reg.Disable(); // idempotent
    }

    [Test]
    public async Task IsEnabled_ReflectsValue()
    {
        var reg = new StartupRegistration(_root);
        await Assert.That(reg.IsEnabled()).IsFalse();
        reg.Enable(@"C:\Curvy.exe");
        await Assert.That(reg.IsEnabled()).IsTrue();
        reg.Disable();
        await Assert.That(reg.IsEnabled()).IsFalse();
    }

    [Test]
    public async Task GetRegisteredExe_ParsesQuotedPath_AndNullWhenAbsent()
    {
        var reg = new StartupRegistration(_root);
        await Assert.That(reg.GetRegisteredExe()).IsNull();
        reg.Enable(@"C:\Program Files\Curvy\Curvy.exe");
        await Assert.That(reg.GetRegisteredExe()).IsEqualTo(@"C:\Program Files\Curvy\Curvy.exe");
    }
}

public sealed class StartupRegistrationShouldRefreshTests
{
    [Test]
    public async Task NotRegistered_NoRefresh() =>
        await Assert.That(StartupRegistration.ShouldRefresh(null, @"C:\new\Curvy.exe", _ => true)).IsFalse();

    [Test]
    public async Task RegisteredExeStillExists_NoRefresh() =>
        await Assert.That(StartupRegistration.ShouldRefresh(@"C:\old\Curvy.exe", @"C:\new\Curvy.exe", _ => true)).IsFalse();

    [Test]
    public async Task RegisteredExeMissing_Refresh() =>
        await Assert.That(StartupRegistration.ShouldRefresh(@"C:\old\Curvy.exe", @"C:\new\Curvy.exe", p => p == @"C:\new\Curvy.exe")).IsTrue();

    [Test]
    public async Task RegisteredIsCurrent_NoRefresh() =>
        await Assert.That(StartupRegistration.ShouldRefresh(@"C:\a\Curvy.exe", @"C:\a\Curvy.exe", _ => false)).IsFalse();
}