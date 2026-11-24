using Microsoft.Win32;

namespace Curvy.Core.Apo;

public static class ApoLocator
{
    public const string EnvOverride = "CURVY_APO_CONFIG_DIR";

    public static string? FindConfigDirectory()
    {
        var env = Environment.GetEnvironmentVariable(EnvOverride);
        if (!string.IsNullOrEmpty(env)) return env;

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\EqualizerAPO");
                if (key is null) continue;
                if (key.GetValue("ConfigPath") is string cp && !string.IsNullOrWhiteSpace(cp)) return cp;
                if (key.GetValue("InstallPath") is string ip && !string.IsNullOrWhiteSpace(ip))
                    return Path.Combine(ip, "config");
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
            }
        }
        return null;
    }
}
