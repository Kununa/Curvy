using System.Diagnostics;
using System.Text.Json;
using Curvy.Core.Apo;
using Curvy.Core.Model;

namespace Curvy.Core.Storage;

public sealed class SettingsStore(string dir)
{
    private sealed record Wrapper(int SchemaVersion, CurvySettings? Settings);

    private string FilePath => Path.Combine(dir, "settings.json");

    public CurvySettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return CurvySettings.Default;
            var w = JsonSerializer.Deserialize<Wrapper>(File.ReadAllText(FilePath), JsonFiles.Options);
            if (w?.Settings is null || w.SchemaVersion > JsonFiles.CurrentSchemaVersion) return CurvySettings.Default;
            return w.Settings;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Curvy: failed to load settings: {ex.Message}");
            return CurvySettings.Default;
        }
    }

    public void Save(CurvySettings s)
    {
        Directory.CreateDirectory(dir);
        AtomicFile.WriteAllText(FilePath,
            JsonSerializer.Serialize(new Wrapper(JsonFiles.CurrentSchemaVersion, s), JsonFiles.Options));
    }
}
