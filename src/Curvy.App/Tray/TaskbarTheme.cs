using Microsoft.Win32;

namespace Curvy.App.Tray;

/// <summary>
/// Whether the taskbar (and so the tray) uses the light theme. That's the Windows "mode" setting,
/// which can differ from the app theme (light apps, dark taskbar is a common mix).
/// </summary>
static class TaskbarTheme
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsLight()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue("SystemUsesLightTheme") is int v && v != 0;
        }
        catch (Exception)
        {
            return false; // dark taskbar is the Windows default
        }
    }
}
