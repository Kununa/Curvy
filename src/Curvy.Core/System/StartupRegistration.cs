using Microsoft.Win32;

// Namespace deliberately not "Curvy.Core.System": that would shadow the global System namespace
// for all code under Curvy.Core.
namespace Curvy.Core.SystemIntegration;

/// <summary>
/// "Start with Windows": the value <see cref="ValueName"/> under <see cref="RunKeyPath"/> of
/// <paramref name="root"/> (<c>HKEY_CURRENT_USER</c> in the app; a throwaway subkey in tests),
/// with data <c>"&lt;exe&gt;" --tray</c>.
/// </summary>
public sealed class StartupRegistration(RegistryKey root)
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "Curvy";
    public const string TrayArgument = "--tray";

    public bool IsEnabled()
    {
        using var run = root.OpenSubKey(RunKeyPath);
        return run?.GetValue(ValueName) is string { Length: > 0 };
    }

    public void Enable(string exePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        using var run = root.CreateSubKey(RunKeyPath, writable: true);
        run.SetValue(ValueName, $"\"{exePath}\" {TrayArgument}", RegistryValueKind.String);
    }

    public void Disable()
    {
        using var run = root.OpenSubKey(RunKeyPath, writable: true);
        run?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
