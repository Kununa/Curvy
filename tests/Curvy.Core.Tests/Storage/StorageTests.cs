using System.Collections.Immutable;
using Curvy.Core.Filters;
using Curvy.Core.Model;
using Curvy.Core.Storage;
using Xunit;

namespace Curvy.Core.Tests.Storage;

public sealed class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "curvy-tests-" + Guid.NewGuid().ToString("N"));

    public StorageTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static void AssertPreferenceEqual(Preference e, Preference a)
    {
        Assert.Equal(e.Tone, a.Tone);
        Assert.Equal(e.Enabled, a.Enabled);
        Assert.True(e.Bands.SequenceEqual(a.Bands));
    }

    private static void AssertProfileEqual(Profile e, Profile a)
    {
        Assert.Equal(e.Id, a.Id);
        Assert.Equal(e.Name, a.Name);
        Assert.Equal(e.AutoEqEnabled, a.AutoEqEnabled);
        Assert.Equal(e.IgnoreUniversal, a.IgnoreUniversal);
        Assert.Equal(e.Created, a.Created);
        Assert.Equal(e.Modified, a.Modified);
        Assert.Equal(e.AutoEq is null, a.AutoEq is null);
        if (e.AutoEq is not null)
        {
            Assert.Equal(e.AutoEq.Source, a.AutoEq!.Source);
            Assert.True(e.AutoEq.Filters.SequenceEqual(a.AutoEq.Filters));
        }
        AssertPreferenceEqual(e.Preference, a.Preference);
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

    [Fact]
    public void Profile_RoundTrips()
    {
        var store = new ProfileStore(_dir);
        var p = Sample();
        store.Save(p);
        var loaded = Assert.Single(store.LoadAll());
        AssertProfileEqual(p, loaded);
        Assert.Contains("\"type\": \"LowShelf\"", File.ReadAllText(Path.Combine(_dir, p.Id + ".json")));
    }

    [Fact]
    public void Profile_WithoutAutoEq_RoundTrips()
    {
        var store = new ProfileStore(_dir);
        var p = Profile.New("Plain");
        store.Save(p);
        AssertProfileEqual(p, Assert.Single(store.LoadAll()));
    }

    [Fact]
    public void LoadAll_SkipsCorruptAndFutureVersionFiles()
    {
        var store = new ProfileStore(_dir);
        var p = Sample();
        store.Save(p);
        File.WriteAllText(Path.Combine(_dir, "bad.json"), "{not json");
        File.WriteAllText(Path.Combine(_dir, "future.json"), "{ \"schemaVersion\": 99, \"profile\": {} }");
        var all = store.LoadAll();
        AssertProfileEqual(p, Assert.Single(all));
    }

    [Fact]
    public void LoadAll_MissingDir_ReturnsEmpty() =>
        Assert.Empty(new ProfileStore(Path.Combine(_dir, "nope")).LoadAll());

    [Fact]
    public void Delete_RemovesFile()
    {
        var store = new ProfileStore(_dir);
        var p = Sample();
        store.Save(p);
        store.Delete(p.Id);
        Assert.False(File.Exists(Path.Combine(_dir, p.Id + ".json")));
        Assert.Empty(store.LoadAll());
        store.Delete(p.Id); // idempotent
    }

    [Fact]
    public void Settings_MissingOrCorrupt_ReturnsDefault()
    {
        var store = new SettingsStore(_dir);
        Assert.Equal(CurvySettings.Default, store.Load());
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{garbage");
        Assert.Equal(CurvySettings.Default, store.Load());
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ \"schemaVersion\": 2, \"settings\": { \"masterEnabled\": false } }");
        Assert.Equal(CurvySettings.Default, store.Load());
    }

    [Fact]
    public void Settings_RoundTrips()
    {
        var store = new SettingsStore(_dir);
        var s = new CurvySettings(1, Guid.NewGuid(), false,
            new Preference(new ToneControls(BassDb: 3), ImmutableList.Create(new Filter(FilterType.Peaking, 1000, 2, 1)), true),
            1.5, true, ["Include foo.txt", "Preamp: 3 dB"]);
        store.Save(s);
        var l = store.Load();
        Assert.Equal(s.ActiveProfileId, l.ActiveProfileId);
        Assert.Equal(s.MasterEnabled, l.MasterEnabled);
        Assert.Equal(s.ExtraHeadroomDb, l.ExtraHeadroomDb);
        Assert.Equal(s.StartWithWindows, l.StartWithWindows);
        Assert.True(s.DisabledForeignLines.SequenceEqual(l.DisabledForeignLines));
        AssertPreferenceEqual(s.Universal, l.Universal);
    }
}
