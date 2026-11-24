using System.Collections.Immutable;
using Curvy.Core.Apo;
using Curvy.Core.Chain;
using Curvy.Core.Filters;

namespace Curvy.Core.Tests.Apo;

public sealed class ApoConfigTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "curvy-test-" + Guid.NewGuid().ToString("N"));
    private readonly string cfg;
    private readonly ApoConfig apo;

    public ApoConfigTests()
    {
        Directory.CreateDirectory(dir);
        cfg = Path.Combine(dir, "config.txt");
        apo = new ApoConfig(dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public void EnsureCurvyInclude_AppendsOnce_KeepsOtherLines()
    {
        File.WriteAllText(cfg, "Include: peace.txt\r\nInclude: fletcher.txt");
        Assert.True(apo.EnsureCurvyInclude());
        Assert.False(apo.EnsureCurvyInclude());
        Assert.Equal(new[] { "Include: peace.txt", "Include: fletcher.txt", ConfigTxt.CurvyIncludeLine }, File.ReadAllLines(cfg));
    }

    [Theory]
    [InlineData("include: curvy/curvy.txt")]
    [InlineData("  Include:  Curvy\\Curvy.TXT  ")]
    public void EnsureCurvyInclude_IsIdempotent_AcrossVariants(string existing)
    {
        File.WriteAllText(cfg, $"Include: peace.txt\r\n{existing}\r\n{existing}\r\n");
        apo.EnsureCurvyInclude();
        Assert.Single(apo.ReadConfigTxt(), l => l.Kind == ConfigLineKind.CurvyInclude);
    }

    [Fact]
    public void EnsureCurvyInclude_SingleExisting_LeavesFileUntouched()
    {
        var original = "Include: curvy/curvy.txt\r\nInclude: peace.txt\r\n";
        File.WriteAllText(cfg, original);
        Assert.False(apo.EnsureCurvyInclude());
        Assert.Equal(original, File.ReadAllText(cfg));
    }

    [Fact]
    public void EnsureCurvyInclude_CreatesConfigTxt_WhenMissing()
    {
        Assert.False(File.Exists(cfg));
        Assert.True(apo.EnsureCurvyInclude());
        Assert.Equal(new[] { ConfigTxt.CurvyIncludeLine }, File.ReadAllLines(cfg));
    }

    [Fact]
    public void SetLineDisabled_RoundTrips_WithPrefix()
    {
        File.WriteAllText(cfg, "Include: peace.txt\r\nPreamp: -3 dB\r\n");
        apo.SetLineDisabled(0, true);
        var l = apo.ReadConfigTxt();
        Assert.Equal("# [Curvy] Include: peace.txt", l[0].Text);
        Assert.Equal(ConfigLineKind.CurvyDisabled, l[0].Kind);
        Assert.Equal("Preamp: -3 dB", l[1].Text);
        apo.SetLineDisabled(0, false);
        l = apo.ReadConfigTxt();
        Assert.Equal("Include: peace.txt", l[0].Text);
        Assert.Equal(ConfigLineKind.Active, l[0].Kind);
    }

    [Fact]
    public void SetLineDisabled_OnCurvyInclude_Throws()
    {
        File.WriteAllText(cfg, ConfigTxt.CurvyIncludeLine + "\r\n");
        Assert.Throws<InvalidOperationException>(() => apo.SetLineDisabled(0, true));
    }

    [Fact]
    public void BackupOnce_DoesNotOverwriteExistingBackup()
    {
        File.WriteAllText(cfg, "first");
        apo.BackupOnce();
        File.WriteAllText(cfg, "second");
        apo.BackupOnce();
        Assert.Equal("first", File.ReadAllText(cfg + ".curvy-backup"));
    }

    [Fact]
    public void RestoreBackup_RestoresOriginalBytes()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, 65, 13, 10, 66 };
        File.WriteAllBytes(cfg, bytes);
        apo.BackupOnce();
        File.WriteAllText(cfg, "changed");
        Assert.True(apo.RestoreBackup());
        Assert.Equal(bytes, File.ReadAllBytes(cfg));
    }

    [Fact]
    public void RestoreBackup_ReturnsFalse_WhenNoBackup() => Assert.False(apo.RestoreBackup());

    [Fact]
    public void WriteOutput_CreatesCurvyDir_AndLeavesNoTmpFile()
    {
        var r = new ChainResult(false, -2.0, ImmutableList<Filter>.Empty);
        apo.WriteOutput(r);
        Assert.True(File.Exists(apo.OutputPath));
        Assert.Equal(ApoFormatter.Format(r), File.ReadAllText(apo.OutputPath));
        Assert.False(File.Exists(apo.OutputPath + ".tmp"));
        Assert.NotEqual(0xEF, File.ReadAllBytes(apo.OutputPath)[0]);
        Assert.True(apo.IsWritable());
    }

    [Fact]
    public void Classify_FlagsEqLikeLines()
    {
        var c = ConfigTxt.Classify(["Device: Speakers", "Include: peace.txt", "# hi", ""]);
        Assert.False(c[0].IsEqLike); Assert.True(c[1].IsEqLike);
        Assert.Equal(ConfigLineKind.Comment, c[2].Kind); Assert.Equal(ConfigLineKind.Blank, c[3].Kind);
    }

    [Fact]
    public void FindConfigDirectory_PrefersEnvOverride()
    {
        var old = Environment.GetEnvironmentVariable(ApoLocator.EnvOverride);
        try
        {
            Environment.SetEnvironmentVariable(ApoLocator.EnvOverride, dir);
            Assert.Equal(dir, ApoLocator.FindConfigDirectory());
        }
        finally { Environment.SetEnvironmentVariable(ApoLocator.EnvOverride, old); }
    }
}
