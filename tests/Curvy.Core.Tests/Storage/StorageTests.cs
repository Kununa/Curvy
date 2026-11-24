using System.IO;
using System.Collections.Immutable;
using Curvy.Core.Filters;
using Curvy.Core.Model;
using Curvy.Core.Storage;
using System.Threading.Tasks;

namespace Curvy.Core.Tests.Storage;

[NotInParallel("env")]
public sealed class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "curvy-tests-" + Guid.NewGuid().ToString("N"));

    public StorageTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Test]
    public async Task Settings_Corrupt_IsRenamedToBad_AndOverwritesOlderBad()
    {
        var store = new SettingsStore(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json.bad"), "older");
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not json");

        await Assert.That(store.Load().StartWithWindows).IsEqualTo(CurvySettings.Default.StartWithWindows);

        await Assert.That(File.Exists(Path.Combine(_dir, "settings.json"))).IsFalse();
        await Assert.That(File.ReadAllText(Path.Combine(_dir, "settings.json.bad"))).IsEqualTo("{ not json");
    }

    [Test]
    public async Task Profiles_Corrupt_IsRenamedToBad()
    {
        var store = new ProfileStore(_dir);
        var id = Guid.NewGuid();
        File.WriteAllText(Path.Combine(_dir, id + ".json"), "garbage");

        await Assert.That(store.LoadAll()).IsEmpty();

        await Assert.That(File.Exists(Path.Combine(_dir, id + ".json"))).IsFalse();
        await Assert.That(File.ReadAllText(Path.Combine(_dir, id + ".json.bad"))).IsEqualTo("garbage");
    }

    private static async Task AssertPreferenceEqual(Preference e, Preference a)
    {
        await Assert.That(a.Tone).IsEqualTo(e.Tone);
        await Assert.That(a.Enabled).IsEqualTo(e.Enabled);
        await Assert.That(e.Bands.SequenceEqual(a.Bands)).IsTrue();
    }

    private static async Task AssertProfileEqual(Profile e, Profile a)
    {
        await Assert.That(a.Id).IsEqualTo(e.Id);
        await Assert.That(a.Name).IsEqualTo(e.Name);
        await Assert.That(a.AutoEqEnabled).IsEqualTo(e.AutoEqEnabled);
        await Assert.That(a.IgnoreUniversal).IsEqualTo(e.IgnoreUniversal);
        await Assert.That(a.Created).IsEqualTo(e.Created);
        await Assert.That(a.Modified).IsEqualTo(e.Modified);
        await Assert.That(a.AutoEq is null).IsEqualTo(e.AutoEq is null);
        if (e.AutoEq is not null)
        {
            await Assert.That(a.AutoEq!.Source).IsEqualTo(e.AutoEq.Source);
            await Assert.That(e.AutoEq.Filters.SequenceEqual(a.AutoEq.Filters)).IsTrue();
        }
        await AssertPreferenceEqual(e.Preference, a.Preference);
    }

    private static Profile Sample(string name = "HD 600")
    {
        var src = new AutoEqSource("HD 600", "Oratory1990", "GRAS", "oratory1990/over-ear/HD 600", DateTimeOffset.Parse("2026-01-02T03:04:05+00:00"));
        var corr = new AutoEqCorrection(src, ImmutableList.Create(
            new Filter(FilterType.LowShelf, 105, 4.5, 0.7), new Filter(FilterType.Peaking, 2000, -3.2, 1.4, false)));
        var pref = new Preference(new ToneControls(BassDb: 2, TiltDb: -1),
            ImmutableList.Create(new Filter(FilterType.HighShelf, 8000, 1.5, 0.7)), true);
        return new Profile(Guid.NewGuid(), name, corr, true, pref, true,
            DateTimeOffset.Parse("2026-02-01T00:00:00+00:00"), DateTimeOffset.Parse("2026-03-01T00:00:00+00:00"));
    }

    [Test]
    public async Task Profile_RoundTrips()
    {
        var store = new ProfileStore(_dir);
        var p = Sample();
        store.Save(p);
        var loaded = await Assert.That(store.LoadAll()).HasSingleItem();
        await AssertProfileEqual(p, loaded);
        await Assert.That(File.ReadAllText(Path.Combine(_dir, p.Id + ".json"))).Contains("\"type\": \"LowShelf\"");
    }

    [Test]
    public async Task Profile_WithoutAutoEq_RoundTrips()
    {
        var store = new ProfileStore(_dir);
        var p = Profile.New("Plain");
        store.Save(p);
        await Assert.That(store.LoadAll()).HasSingleItem();
    }

    [Test]
    public async Task LoadAll_SkipsCorruptAndFutureVersionFiles()
    {
        var store = new ProfileStore(_dir);
        var p = Sample();
        store.Save(p);
        File.WriteAllText(Path.Combine(_dir, "bad.json"), "{not json");
        File.WriteAllText(Path.Combine(_dir, "future.json"), "{ \"schemaVersion\": 99, \"profile\": {} }");
        var all = store.LoadAll();
        await Assert.That(all).HasSingleItem();
    }

    [Test]
    public async Task LoadAll_MissingDir_ReturnsEmpty() =>
        await Assert.That(new ProfileStore(Path.Combine(_dir, "nope")).LoadAll()).IsEmpty();

    [Test]
    public async Task Delete_RemovesFile()
    {
        var store = new ProfileStore(_dir);
        var p = Sample();
        store.Save(p);
        store.Delete(p.Id);
        await Assert.That(File.Exists(Path.Combine(_dir, p.Id + ".json"))).IsFalse();
        await Assert.That(store.LoadAll()).IsEmpty();
        store.Delete(p.Id); // idempotent
    }

    [Test]
    public async Task Settings_MissingOrCorrupt_ReturnsDefault()
    {
        var store = new SettingsStore(_dir);
        await Assert.That(store.Load().StartWithWindows).IsEqualTo(CurvySettings.Default.StartWithWindows);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{garbage");
        await Assert.That(store.Load().StartWithWindows).IsEqualTo(CurvySettings.Default.StartWithWindows);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ \"schemaVersion\": 2, \"settings\": { \"masterEnabled\": false } }");
        await Assert.That(store.Load().StartWithWindows).IsEqualTo(CurvySettings.Default.StartWithWindows);
    }

    [Test]
    public async Task Settings_RoundTrips()
    {
        var store = new SettingsStore(_dir);
        var s = new CurvySettings(1, Guid.NewGuid(), false,
            new Preference(new ToneControls(BassDb: 3), ImmutableList.Create(new Filter(FilterType.Peaking, 1000, 2, 1)), true),
            1.5, true, ["Include foo.txt", "Preamp: 3 dB"], FirstRunShown: true, IntegrationPaused: true);
        store.Save(s);
        var l = store.Load();
        await Assert.That(l.ActiveProfileId).IsEqualTo(s.ActiveProfileId);
        await Assert.That(l.MasterEnabled).IsEqualTo(s.MasterEnabled);
        await Assert.That(l.ExtraHeadroomDb).IsEqualTo(s.ExtraHeadroomDb);
        await Assert.That(l.StartWithWindows).IsEqualTo(s.StartWithWindows);
        await Assert.That(s.DisabledForeignLines.SequenceEqual(l.DisabledForeignLines)).IsTrue();
        await Assert.That(l.FirstRunShown).IsTrue();
        await Assert.That(l.IntegrationPaused).IsTrue();
        await AssertPreferenceEqual(s.Universal, l.Universal);
    }

    [Test]
    public async Task Settings_MissingNewerProperties_LoadWithDefaults()
    {
        // A settings.json from before firstRunShown / universal / disabledForeignLines existed.
        var id = Guid.NewGuid();
        File.WriteAllText(Path.Combine(_dir, "settings.json"), $$"""
            { "schemaVersion": 1, "settings": { "schemaVersion": 1, "activeProfileId": "{{id}}",
              "masterEnabled": false, "extraHeadroomDb": 2, "startWithWindows": true } }
            """);
        var l = new SettingsStore(_dir).Load();
        await Assert.That(l.ActiveProfileId).IsEqualTo(id);
        await Assert.That(l.MasterEnabled).IsFalse();
        await Assert.That(l.ExtraHeadroomDb).IsEqualTo(2);
        await Assert.That(l.StartWithWindows).IsTrue();
        await Assert.That(l.FirstRunShown).IsFalse();
        await Assert.That(l.IntegrationPaused).IsFalse();
        await Assert.That(l.Universal).IsSameReferenceAs(Preference.Empty);
        await Assert.That(l.DisabledForeignLines).IsNotNull();
        await Assert.That(l.DisabledForeignLines).IsEmpty();
    }

    [Test]
    public async Task Settings_NullUniversalAndLines_AreNormalized()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), """
            { "schemaVersion": 1, "settings": { "schemaVersion": 1, "masterEnabled": true,
              "universal": null, "disabledForeignLines": null } }
            """);
        var l = new SettingsStore(_dir).Load();
        await Assert.That(l.Universal).IsSameReferenceAs(Preference.Empty);
        await Assert.That(l.DisabledForeignLines).IsEmpty();
    }

    [Test]
    public async Task CurvyPaths_Root_HonoursEnvOverride()
    {
        var old = Environment.GetEnvironmentVariable(CurvyPaths.EnvOverride);
        try
        {
            Environment.SetEnvironmentVariable(CurvyPaths.EnvOverride, _dir);
            await Assert.That(CurvyPaths.Root).IsEqualTo(_dir);
            await Assert.That(CurvyPaths.ProfilesDir).IsEqualTo(Path.Combine(_dir, "profiles"));

            Environment.SetEnvironmentVariable(CurvyPaths.EnvOverride, null);
            await Assert.That(CurvyPaths.Root).IsEqualTo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Curvy"));
        }
        finally { Environment.SetEnvironmentVariable(CurvyPaths.EnvOverride, old); }
    }
}