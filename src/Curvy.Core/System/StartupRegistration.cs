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

    /// <summary>The exe path from the registered value (<c>"&lt;exe&gt;" --tray</c>), or null if not registered.</summary>
    public string? GetRegisteredExe()
    {
        using var run = root.OpenSubKey(RunKeyPath);
        if (run?.GetValue(ValueName) is not string { Length: > 0 } v) return null;
        v = v.Trim();
        if (v.StartsWith('"'))
        {
            var end = v.IndexOf('"', 1);
            return end > 1 ? v[1..end] : null;
        }
        var space = v.IndexOf(' ');
        return space > 0 ? v[..space] : v;
    }

    /// <summary>
    /// Whether to rewrite the Run value to <paramref name="currentExe"/> on startup: only when an entry
    /// exists, points elsewhere, and that registered exe is gone (the app was moved/reinstalled). A
    /// registered exe that still exists is left alone so dev builds don't hijack the entry.
    /// </summary>
    public static bool ShouldRefresh(string? registeredExe, string currentExe, Func<string, bool> fileExists) =>
        !string.IsNullOrWhiteSpace(registeredExe)
        && !string.Equals(registeredExe, currentExe, StringComparison.OrdinalIgnoreCase)
        && !fileExists(registeredExe);

    public void Disable()
    {
        using var run = root.OpenSubKey(RunKeyPath, writable: true);
        run?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
