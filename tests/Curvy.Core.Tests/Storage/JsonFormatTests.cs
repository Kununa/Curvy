using System.Collections.Immutable;
using System.IO;
using Curvy.Core.Filters;
using Curvy.Core.Model;
using Curvy.Core.Storage;

namespace Curvy.Core.Tests.Storage;

/// <summary>
/// The on-disk format must not change with the move to source-generated JSON: files written by the
/// earlier reflection-based serializer (these literals are verbatim output of it) must still load,
/// and new files keep the same property names and string enums.
/// </summary>
public sealed class JsonFormatTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "curvy-jsonfmt-" + Guid.NewGuid().ToString("N"));

    public JsonFormatTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private const string LegacyProfile = """
        {
          "schemaVersion": 1,
          "profile": {
            "id": "11111111-2222-3333-4444-555555555555",
            "name": "HD 600",
            "autoEq": {
              "source": {
                "headphone": "HD 600",
                "source": "oratory1990",
                "rig": "GRAS 45BC-10",
                "relativePath": "oratory1990/over-ear/HD%20600",
                "fetchedAt": "2026-01-02T03:04:05+00:00"
              },
              "filters": [
                { "type": "LowShelf", "frequencyHz": 105, "gainDb": 6.5, "q": 0.7, "enabled": true, "frequencyLocked": false }
              ]
            },
            "autoEqEnabled": true,
            "preference": {
              "tone": { "bassDb": 2, "bassHz": 105, "bassQ": 0.7, "trebleDb": 0, "trebleHz": 10000, "trebleQ": 0.7, "tiltDb": 0, "tiltPivotHz": 1000 },
              "bands": [
                { "type": "Peaking", "frequencyHz": 1000, "gainDb": 1.5, "q": 1.4, "enabled": true, "frequencyLocked": true }
              ],
              "enabled": true,
              "mode": "Parametric",
              "effectiveMode": "Parametric",
              "activeBands": [
                { "type": "Peaking", "frequencyHz": 1000, "gainDb": 1.5, "q": 1.4, "enabled": true, "frequencyLocked": true }
              ]
            },
            "ignoreUniversal": false,
            "created": "2026-02-01T00:00:00+00:00",
            "modified": "2026-03-01T00:00:00+00:00"
          }
        }
        """;

    private const string LegacySettings = """
        {
          "schemaVersion": 1,
          "settings": {
            "schemaVersion": 1,
            "activeProfileId": "11111111-2222-3333-4444-555555555555",
            "masterEnabled": true,
            "universal": {
              "tone": { "bassDb": 0, "bassHz": 105, "bassQ": 0.7, "trebleDb": 0, "trebleHz": 10000, "trebleQ": 0.7, "tiltDb": 0, "tiltPivotHz": 1000 },
              "bands": [
                { "type": "HighShelf", "frequencyHz": 10000, "gainDb": 0, "q": 0.7, "enabled": true, "frequencyLocked": false }
              ],
              "enabled": true,
              "mode": null,
              "effectiveMode": "Simple",
              "activeBands": []
            },
            "extraHeadroomDb": 0,
            "startWithWindows": false,
            "disabledForeignLines": [ "Include: peace.txt" ],
            "firstRunShown": false,
            "integrationPaused": false,
            "preampMode": "Manual",
            "manualPreampDb": -6
          }
        }
        """;

    [Test]
    public async Task LegacyProfileFile_Loads()
    {
        var dir = Path.Combine(_dir, "profiles");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "11111111-2222-3333-4444-555555555555.json"), LegacyProfile);

        var p = await Assert.That(new ProfileStore(dir).LoadAll()).HasSingleItem();
        await Assert.That(p!.Name).IsEqualTo("HD 600");
        await Assert.That(p.AutoEq!.Source.Rig).IsEqualTo("GRAS 45BC-10");
        await Assert.That(p.AutoEq.Filters[0].Type).IsEqualTo(FilterType.LowShelf);
        await Assert.That(p.Preference.Mode).IsEqualTo(PreferenceMode.Parametric);
        await Assert.That(p.Preference.Tone.BassDb).IsEqualTo(2);
        await Assert.That(p.Preference.Bands[0].FrequencyLocked).IsTrue();
        await Assert.That(p.Created).IsEqualTo(DateTimeOffset.Parse("2026-02-01T00:00:00+00:00"));
    }

    [Test]
    public async Task LegacySettingsFile_Loads()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), LegacySettings);

        var s = new SettingsStore(_dir).Load();
        await Assert.That(s.ActiveProfileId).IsEqualTo(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        await Assert.That(s.PreampMode).IsEqualTo(PreampMode.Manual);
        await Assert.That(s.Universal.Mode).IsNull();
        await Assert.That(s.Universal.Bands[0].Type).IsEqualTo(FilterType.HighShelf);
        await Assert.That(s.DisabledForeignLines.SequenceEqual(["Include: peace.txt"])).IsTrue();
    }

    [Test]
    public async Task SavedProfile_KeepsFormat_WithoutDerivedValues()
    {
        var dir = Path.Combine(_dir, "profiles");
        var pref = new Preference(new ToneControls(BassDb: 2),
            ImmutableList.Create(new Filter(FilterType.Peaking, 1000, 1.5, 1.4, FrequencyLocked: true)), true,
            PreferenceMode.Parametric);
        var p = Profile.New("HD 600") with { Preference = pref };
        new ProfileStore(dir).Save(p);

        var json = File.ReadAllText(Path.Combine(dir, p.Id + ".json"));
        await Assert.That(json).Contains("\"schemaVersion\": 1");
        await Assert.That(json).Contains("\"profile\": {");
        await Assert.That(json).Contains("\"type\": \"Peaking\"");
        await Assert.That(json).Contains("\"mode\": \"Parametric\"");
        await Assert.That(json).Contains("\"frequencyLocked\": true");
        await Assert.That(json).Contains("\"ignoreUniversal\": false");
        await Assert.That(json).DoesNotContain("effectiveMode");
        await Assert.That(json).DoesNotContain("activeBands");
    }
}
