using System.IO;
using System.Collections.Immutable;
using Curvy.Core.Apo;
using Curvy.Core.Engine;
using Curvy.Core.Filters;
using Curvy.Core.Model;
using Curvy.Core.Storage;
using System.Threading.Tasks;

namespace Curvy.Core.Tests.Engine;

/// <summary>
/// Mirrors what the app's EngineHost does at startup and what the Task 11 manual check does
/// (blank profile, two bands, bass +4, activate), but against temp dirs selected through the
/// same environment overrides the app honours: CURVY_APO_CONFIG_DIR and CURVY_DATA_DIR.
/// </summary>
[NotInParallel("env")]
public sealed class AppStartupPathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "curvy-app-path-" + Guid.NewGuid().ToString("N"));
    private readonly string? _oldApo = Environment.GetEnvironmentVariable(ApoLocator.EnvOverride);
    private readonly string? _oldData = Environment.GetEnvironmentVariable(CurvyPaths.EnvOverride);
    private string ApoDir => Path.Combine(_root, "apo");
    private string DataDir => Path.Combine(_root, "data");

    public AppStartupPathTests()
    {
        Directory.CreateDirectory(ApoDir);
        File.WriteAllText(Path.Combine(ApoDir, "config.txt"), "Include: peace.txt\r\n");
        Environment.SetEnvironmentVariable(ApoLocator.EnvOverride, ApoDir);
        Environment.SetEnvironmentVariable(CurvyPaths.EnvOverride, DataDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ApoLocator.EnvOverride, _oldApo);
        Environment.SetEnvironmentVariable(CurvyPaths.EnvOverride, _oldData);
        try { Directory.Delete(_root, true); } catch { }
    }

    [Test]
    public async Task StartupApply_ThenEditAndActivate_WritesFilterViewText_IncludeAndBackup()
    {
        // EngineHost: locate, check writable, construct over CurvyPaths, Apply().
        var dir = ApoLocator.FindConfigDirectory();
        await Assert.That(dir).IsEqualTo(ApoDir);
        var apo = new ApoConfig(dir!);
        await Assert.That(apo.IsWritable()).IsTrue();
        var engine = new CurvyEngine(new ProfileStore(CurvyPaths.ProfilesDir), new SettingsStore(CurvyPaths.Root), apo);
        engine.Apply();

        var config = File.ReadAllText(apo.ConfigTxtPath);
        await Assert.That(config).Contains("Include: peace.txt");
        await Assert.That(config.Split("\r\n").Count(l => l == ConfigTxt.CurvyIncludeLine)).IsEqualTo(1);
        await Assert.That(File.Exists(apo.ConfigTxtPath + ".curvy-backup")).IsTrue();
        await Assert.That(File.ReadAllText(apo.ConfigTxtPath + ".curvy-backup")).IsEqualTo("Include: peace.txt\r\n");

        // "New blank profile", add two bands, bass +4, then Activate.
        var p = Profile.New("New profile");
        engine.SaveProfile(p);
        p = p with
        {
            Preference = p.Preference with
            {
                Tone = p.Preference.Tone with { BassDb = 4 },
                Bands = ImmutableList.Create(
                    new Filter(FilterType.Peaking, 1000, 0, 1),
                    new Filter(FilterType.Peaking, 2500, 3, 1.5)),
            },
        };
        engine.SaveProfile(p);
        engine.Activate(p.Id);

        await Assert.That(engine.LastError).IsNull();
        await Assert.That(engine.Current.Bypassed).IsFalse();
        await Assert.That(engine.Current.PreampDb < 0).IsTrue(); // +4 bass and +3 band need headroom
        // curvy.txt is exactly what the filter view shows.
        await Assert.That(File.ReadAllText(apo.OutputPath)).IsEqualTo(ApoFormatter.Format(engine.Current));
        await Assert.That(File.Exists(Path.Combine(DataDir, "profiles", p.Id + ".json"))).IsTrue();
    }
}