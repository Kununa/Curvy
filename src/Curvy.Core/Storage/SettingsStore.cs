using System.Diagnostics;
using System.Text.Json;
using Curvy.Core.Apo;
using Curvy.Core.Model;

namespace Curvy.Core.Storage;

public sealed class SettingsStore(string dir)
{
    private string FilePath => Path.Combine(dir, "settings.json");

    public CurvySettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return CurvySettings.Default;
            var w = JsonSerializer.Deserialize(File.ReadAllText(FilePath), StorageJsonContext.Default.SettingsFile);
            if (w?.Settings is null || w.SchemaVersion > JsonFiles.CurrentSchemaVersion) return CurvySettings.Default;
            return Normalize(w.Settings);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Curvy: failed to load settings: {ex.Message}");
            JsonFiles.QuarantineCorrupt(FilePath, ex);
            return CurvySettings.Default;
        }
    }

    /// <summary>
    /// Properties missing from older files deserialize as null (positional record); replace those
    /// with their defaults so callers never see a null <see cref="CurvySettings.Universal"/> or
    /// <see cref="CurvySettings.DisabledForeignLines"/>.
    /// </summary>
    private static CurvySettings Normalize(CurvySettings s) => s with
    {
        Universal = s.Universal ?? Preference.Empty,
        DisabledForeignLines = s.DisabledForeignLines ?? [],
    };

    public void Save(CurvySettings s)
    {
        Directory.CreateDirectory(dir);
        AtomicFile.WriteAllText(FilePath,
            JsonSerializer.Serialize(new SettingsFile(JsonFiles.CurrentSchemaVersion, s), StorageJsonContext.Default.SettingsFile));
    }
}
