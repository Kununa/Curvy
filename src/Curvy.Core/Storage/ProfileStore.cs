using System.Diagnostics;
using System.Text.Json;
using Curvy.Core.Apo;
using Curvy.Core.Model;

namespace Curvy.Core.Storage;

public sealed class ProfileStore(string dir)
{
    private sealed record Wrapper(int SchemaVersion, Profile? Profile);

    private string PathFor(Guid id) => Path.Combine(dir, id + ".json");

    public IReadOnlyList<Profile> LoadAll()
    {
        var result = new List<Profile>();
        try
        {
            if (!Directory.Exists(dir)) return result;
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            {
                try
                {
                    var w = JsonSerializer.Deserialize<Wrapper>(File.ReadAllText(file), JsonFiles.Options);
                    if (w is null || w.SchemaVersion > JsonFiles.CurrentSchemaVersion || w.Profile is null)
                    {
                        Debug.WriteLine($"Curvy: skipping profile file {file} (unsupported or empty)");
                        continue;
                    }
                    result.Add(w.Profile);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Curvy: skipping profile file {file}: {ex.Message}");
                    JsonFiles.QuarantineCorrupt(file, ex);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Curvy: failed to enumerate profiles in {dir}: {ex.Message}");
        }
        return result;
    }

    public void Save(Profile p)
    {
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(new Wrapper(JsonFiles.CurrentSchemaVersion, p), JsonFiles.Options);
        AtomicFile.WriteAllText(PathFor(p.Id), json);
    }

    public void Delete(Guid id) => File.Delete(PathFor(id));
}
