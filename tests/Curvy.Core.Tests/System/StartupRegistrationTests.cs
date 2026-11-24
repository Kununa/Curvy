using Curvy.Core.SystemIntegration;
using Microsoft.Win32;
using Xunit;

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

    [Fact]
    public void Enable_WritesQuotedExeWithTrayArg()
    {
        new StartupRegistration(_root).Enable(@"C:\Apps\Curvy\Curvy.App.exe");
        Assert.Equal("\"C:\\Apps\\Curvy\\Curvy.App.exe\" --tray", RunValue());
    }

    [Fact]
    public void Disable_RemovesValue()
    {
        var reg = new StartupRegistration(_root);
        reg.Enable(@"C:\Curvy.exe");
        reg.Disable();
        Assert.Null(RunValue());
        reg.Disable(); // idempotent
    }

    [Fact]
    public void IsEnabled_ReflectsValue()
    {
        var reg = new StartupRegistration(_root);
        Assert.False(reg.IsEnabled());
        reg.Enable(@"C:\Curvy.exe");
        Assert.True(reg.IsEnabled());
        reg.Disable();
        Assert.False(reg.IsEnabled());
    }

    [Fact]
    public void GetRegisteredExe_ParsesQuotedPath_AndNullWhenAbsent()
    {
        var reg = new StartupRegistration(_root);
        Assert.Null(reg.GetRegisteredExe());
        reg.Enable(@"C:\Program Files\Curvy\Curvy.exe");
        Assert.Equal(@"C:\Program Files\Curvy\Curvy.exe", reg.GetRegisteredExe());
    }
}

public sealed class StartupRegistrationShouldRefreshTests
{
    [Fact]
    public void NotRegistered_NoRefresh() =>
        Assert.False(StartupRegistration.ShouldRefresh(null, @"C:\new\Curvy.exe", _ => true));

    [Fact]
    public void RegisteredExeStillExists_NoRefresh() =>
        Assert.False(StartupRegistration.ShouldRefresh(@"C:\old\Curvy.exe", @"C:\new\Curvy.exe", _ => true));

    [Fact]
    public void RegisteredExeMissing_Refresh() =>
        Assert.True(StartupRegistration.ShouldRefresh(@"C:\old\Curvy.exe", @"C:\new\Curvy.exe", p => p == @"C:\new\Curvy.exe"));

    [Fact]
    public void RegisteredIsCurrent_NoRefresh() =>
        Assert.False(StartupRegistration.ShouldRefresh(@"C:\a\Curvy.exe", @"C:\a\Curvy.exe", _ => false));
}
