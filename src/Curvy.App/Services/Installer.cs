using Curvy.Core.Apo;
using Curvy.Core.SystemIntegration;
using Microsoft.Win32;

namespace Curvy.App.Services;

/// <summary>
/// Velopack integration (installer, uninstaller). <c>VelopackApp.Build()…Run()</c> is the first
/// statement in App.cs (vpk checks that it sits in the entry point): when Velopack starts the exe for
/// an install/uninstall hook it handles it and exits; in a normal or never-installed run it returns.
/// <para>
/// Installed to <c>%LocalAppData%\Curvy.App</c> (pack id <c>Curvy.App</c>). That is deliberately not
/// <c>%LocalAppData%\Curvy</c>, where profiles and settings live: the uninstaller deletes its whole
/// install folder, and user data must survive an uninstall/reinstall.
/// </para>
/// </summary>
static class Installer
{
    /// <summary>
    /// Undo what Curvy changed outside its own folders, so the EQ doesn't linger after uninstalling:
    /// the "Start with Windows" entry, and config.txt (restored from the backup taken before Curvy's
    /// first change, which also drops Curvy's include). Best effort: an uninstall must never fail here.
    /// </summary>
    public static void CleanUpBeforeUninstall()
    {
        try { new StartupRegistration(Registry.CurrentUser).Disable(); }
        catch (Exception) { /* nothing registered, or no access */ }

        try
        {
            if (ApoLocator.FindConfigDirectory() is { } dir)
            {
                var apo = new ApoConfig(dir);
                apo.RestoreBackup();
                var output = Path.GetDirectoryName(apo.OutputPath);
                if (output is not null && Directory.Exists(output)) Directory.Delete(output, recursive: true);
            }
        }
        catch (Exception) { /* Equalizer APO gone or config not writable: leave it */ }
    }
}
