using Microsoft.Win32;

namespace Curvy.Core.Apo;

/// <summary>
/// Which audio devices Equalizer APO is installed on. Its Device Selector records each one under
/// <c>HKLM\SOFTWARE\EqualizerAPO\Child APOs\{endpoint GUID}</c> (the original APOs it wraps).
/// </summary>
public static class ApoDevices
{
    const string ApoKey = @"SOFTWARE\EqualizerAPO";

    /// <summary>Endpoint GUIDs that have Equalizer APO; empty when it isn't installed or can't be read.</summary>
    public static IReadOnlySet<Guid> Installed()
    {
        var result = new HashSet<Guid>();
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var children = baseKey.OpenSubKey(ApoKey + @"\Child APOs");
            foreach (var name in children?.GetSubKeyNames() ?? [])
                if (Guid.TryParse(name, out var g)) result.Add(g);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }
        return result;
    }

    /// <summary>
    /// The endpoint GUID inside a Windows audio device id such as
    /// <c>{0.0.0.00000000}.{1eda1038-37bc-40d3-a90e-c540cc764bf3}</c>; null if it has none.
    /// </summary>
    public static Guid? EndpointGuid(string deviceId)
    {
        var dot = deviceId.LastIndexOf("}.", StringComparison.Ordinal);
        return dot >= 0 && Guid.TryParse(deviceId.AsSpan(dot + 2), out var g) ? g : null;
    }

    /// <summary>Equalizer APO's Device Selector (adds or removes devices; asks for admin rights).</summary>
    public static string? DeviceSelectorPath()
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(ApoKey);
            if (key?.GetValue("InstallPath") is not string dir || string.IsNullOrWhiteSpace(dir)) return null;
            var exe = Path.Combine(dir, "DeviceSelector.exe");
            return File.Exists(exe) ? exe : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
