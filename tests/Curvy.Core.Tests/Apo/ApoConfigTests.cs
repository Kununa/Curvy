using System.Collections.Immutable;
using Curvy.Core.Apo;
using Curvy.Core.Chain;
using Curvy.Core.Filters;
using Xunit;

namespace Curvy.Core.Tests.Apo;

[Collection(EnvCollection.Name)] // FindConfigDirectory_PrefersEnvOverride sets CURVY_APO_CONFIG_DIR
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnsureCurvyInclude_RefusesUtf16_AndLeavesBytesUnchanged(bool littleEndian)
    {
        var enc = new System.Text.UnicodeEncoding(!littleEndian, byteOrderMark: true);
        var bytes = enc.GetPreamble().Concat(enc.GetBytes("Include: peace.txt")).ToArray();
        File.WriteAllBytes(cfg, bytes);

        var ex = Assert.Throws<InvalidOperationException>(() => apo.EnsureCurvyInclude());
        Assert.Contains("UTF-16", ex.Message);
        Assert.Equal(bytes, File.ReadAllBytes(cfg));
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
        apo.SetLineDisabled(0, "Include: peace.txt", true);
        var l = apo.ReadConfigTxt();
        Assert.Equal("# [Curvy] Include: peace.txt", l[0].Text);
        Assert.Equal(ConfigLineKind.CurvyDisabled, l[0].Kind);
        Assert.Equal("Preamp: -3 dB", l[1].Text);
        apo.SetLineDisabled(0, "# [Curvy] Include: peace.txt", false);
        l = apo.ReadConfigTxt();
        Assert.Equal("Include: peace.txt", l[0].Text);
        Assert.Equal(ConfigLineKind.Active, l[0].Kind);
    }

    [Fact]
    public void SetLineDisabled_OnCurvyInclude_Throws()
    {
        File.WriteAllText(cfg, ConfigTxt.CurvyIncludeLine + "\r\n");
        Assert.Throws<InvalidOperationException>(() => apo.SetLineDisabled(0, ConfigTxt.CurvyIncludeLine, true));
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
        Assert.Empty(Directory.GetFiles(dir, "*.tmp", SearchOption.AllDirectories));
        Assert.NotEqual(0xEF, File.ReadAllBytes(apo.OutputPath)[0]);
        Assert.True(apo.IsWritable());
    }

    [Fact]
    public void SetLineDisabled_Throws_WhenTextMismatch()
    {
        File.WriteAllText(cfg, "Include: a.txt\r\nInclude: b.txt\r\n");
        Assert.Throws<ConfigChangedException>(() => apo.SetLineDisabled(0, "Include: b.txt", true));
        Assert.Equal("Include: a.txt\r\nInclude: b.txt\r\n", File.ReadAllText(cfg));
    }

    [Fact]
    public void Windows1252_Config_SurvivesRewrites_ByteExact()
    {
        var latin = System.Text.Encoding.Latin1;
        var line = "Device: Lautsprecher (High Definition Audio-Ger\u00e4t)";
        File.WriteAllBytes(cfg, latin.GetBytes(line + "\r\nInclude: x.txt\r\n"));
        apo.EnsureCurvyInclude();
        apo.SetLineDisabled(1, "Include: x.txt", true);
        var expected = latin.GetBytes(line + "\r\n# [Curvy] Include: x.txt\r\n" + ConfigTxt.CurvyIncludeLine + "\r\n");
        Assert.Equal(expected, File.ReadAllBytes(cfg));
    }

    [Fact]
    public void Bom_IsPreserved_OnRewrite()
    {
        File.WriteAllBytes(cfg, [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes("Include: x.txt\r\n")]);
        apo.EnsureCurvyInclude();
        var b = File.ReadAllBytes(cfg);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, b[..3]);
        Assert.Equal(0x49, b[3]);
    }

    [Fact]
    public void FirstChange_CreatesBackupOfOriginalBytes_AndLaterChangeKeepsIt()
    {
        var orig = System.Text.Encoding.ASCII.GetBytes("Include: x.txt");
        File.WriteAllBytes(cfg, orig);
        apo.EnsureCurvyInclude();
        Assert.Equal(orig, File.ReadAllBytes(cfg + ".curvy-backup"));
        apo.SetLineDisabled(0, "Include: x.txt", true);
        Assert.Equal(orig, File.ReadAllBytes(cfg + ".curvy-backup"));
        Assert.True(apo.RestoreBackup());
        Assert.Equal(orig, File.ReadAllBytes(cfg));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    [Fact]
    public void IsWritable_False_WhenDirMissing_AndDoesNotCreateIt()
    {
        var missing = Path.Combine(dir, "nope");
        Assert.False(new ApoConfig(missing).IsWritable());
        Assert.False(Directory.Exists(missing));
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

    [Fact]
    public void ConfigTxtWriting_RaisedBeforeEachConfigTxtWrite_Only()
    {
        File.WriteAllText(cfg, "Include: peace.txt\r\n");
        var raised = 0;
        apo.ConfigTxtWriting += (_, _) => raised++;
        Assert.True(apo.EnsureCurvyInclude());
        Assert.Equal(1, raised);
        Assert.False(apo.EnsureCurvyInclude()); // nothing to write
        Assert.Equal(1, raised);
        apo.SetLineDisabled(0, "Include: peace.txt", true);
        Assert.Equal(2, raised);
        Assert.True(apo.RestoreBackup());
        Assert.Equal(3, raised);
        apo.WriteOutput(new ChainResult(false, 0, ImmutableList<Filter>.Empty)); // curvy.txt, not config.txt
        Assert.Equal(3, raised);
    }
}
