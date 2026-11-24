using System.Collections.Immutable;
using Curvy.Core.Apo;
using Curvy.Core.Engine;
using Curvy.Core.Filters;
using Curvy.Core.Model;
using Curvy.Core.Storage;
using Xunit;

namespace Curvy.Core.Tests.Engine;

/// <summary>
/// Mirrors what the app's EngineHost does at startup and what the Task 11 manual check does
/// (blank profile, two bands, bass +4, activate), but against temp dirs selected through the
/// same environment overrides the app honours: CURVY_APO_CONFIG_DIR and CURVY_DATA_DIR.
/// </summary>
[Collection(EnvCollection.Name)]
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

    [Fact]
    public void StartupApply_ThenEditAndActivate_WritesFilterViewText_IncludeAndBackup()
    {
        // EngineHost: locate, check writable, construct over CurvyPaths, Apply().
        var dir = ApoLocator.FindConfigDirectory();
        Assert.Equal(ApoDir, dir);
        var apo = new ApoConfig(dir!);
        Assert.True(apo.IsWritable());
        var engine = new CurvyEngine(new ProfileStore(CurvyPaths.ProfilesDir), new SettingsStore(CurvyPaths.Root), apo);
        engine.Apply();

        var config = File.ReadAllText(apo.ConfigTxtPath);
        Assert.Contains("Include: peace.txt", config);
        Assert.Single(config.Split("\r\n"), l => l == ConfigTxt.CurvyIncludeLine);
        Assert.True(File.Exists(apo.ConfigTxtPath + ".curvy-backup"));
        Assert.Equal("Include: peace.txt\r\n", File.ReadAllText(apo.ConfigTxtPath + ".curvy-backup"));

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

        Assert.Null(engine.LastError);
        Assert.False(engine.Current.Bypassed);
        Assert.True(engine.Current.PreampDb < 0); // +4 bass and +3 band need headroom
        // curvy.txt is exactly what the filter view shows.
        Assert.Equal(ApoFormatter.Format(engine.Current), File.ReadAllText(apo.OutputPath));
        Assert.True(File.Exists(Path.Combine(DataDir, "profiles", p.Id + ".json")));
    }
}
