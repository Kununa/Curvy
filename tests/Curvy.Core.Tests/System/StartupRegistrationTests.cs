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
}
