using System.IO;
using System.Collections.Immutable;
using Curvy.Core.Apo;
using Curvy.Core.Chain;
using Curvy.Core.Filters;
using System.Threading.Tasks;

namespace Curvy.Core.Tests.Apo;

[NotInParallel("env")] // FindConfigDirectory_PrefersEnvOverride sets CURVY_APO_CONFIG_DIR
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

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task EnsureCurvyInclude_RefusesUtf16_AndLeavesBytesUnchanged(bool littleEndian)
    {
        var enc = new System.Text.UnicodeEncoding(!littleEndian, byteOrderMark: true);
        var bytes = enc.GetPreamble().Concat(enc.GetBytes("Include: peace.txt")).ToArray();
        File.WriteAllBytes(cfg, bytes);

        var ex = Assert.Throws<InvalidOperationException>(() => apo.EnsureCurvyInclude());
        await Assert.That(ex.Message).Contains("UTF-16");
        await Assert.That(File.ReadAllBytes(cfg)).IsEquivalentTo(bytes, CollectionOrdering.Matching);
    }

    [Test]
    public async Task EnsureCurvyInclude_AppendsOnce_KeepsOtherLines()
    {
        File.WriteAllText(cfg, "Include: peace.txt\r\nInclude: fletcher.txt");
        await Assert.That(apo.EnsureCurvyInclude()).IsTrue();
        await Assert.That(apo.EnsureCurvyInclude()).IsFalse();
        await Assert.That(File.ReadAllLines(cfg)).IsEquivalentTo(new[] { "Include: peace.txt", "Include: fletcher.txt", ConfigTxt.CurvyIncludeLine }, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments("include: curvy/curvy.txt")]
    [Arguments("  Include:  Curvy\\Curvy.TXT  ")]
    public async Task EnsureCurvyInclude_IsIdempotent_AcrossVariants(string existing)
    {
        File.WriteAllText(cfg, $"Include: peace.txt\r\n{existing}\r\n{existing}\r\n");
        apo.EnsureCurvyInclude();
        await Assert.That(apo.ReadConfigTxt().Count(l => l.Kind == ConfigLineKind.CurvyInclude)).IsEqualTo(1);
    }

    [Test]
    public async Task EnsureCurvyInclude_SingleExisting_LeavesFileUntouched()
    {
        var original = "Include: curvy/curvy.txt\r\nInclude: peace.txt\r\n";
        File.WriteAllText(cfg, original);
        await Assert.That(apo.EnsureCurvyInclude()).IsFalse();
        await Assert.That(File.ReadAllText(cfg)).IsEqualTo(original);
    }

    [Test]
    public async Task EnsureCurvyInclude_CreatesConfigTxt_WhenMissing()
    {
        await Assert.That(File.Exists(cfg)).IsFalse();
        await Assert.That(apo.EnsureCurvyInclude()).IsTrue();
        await Assert.That(File.ReadAllLines(cfg)).IsEquivalentTo(new[] { ConfigTxt.CurvyIncludeLine }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task SetLineDisabled_RoundTrips_WithPrefix()
    {
        File.WriteAllText(cfg, "Include: peace.txt\r\nPreamp: -3 dB\r\n");
        apo.SetLineDisabled(0, "Include: peace.txt", true);
        var l = apo.ReadConfigTxt();
        await Assert.That(l[0].Text).IsEqualTo("# [Curvy] Include: peace.txt");
        await Assert.That(l[0].Kind).IsEqualTo(ConfigLineKind.CurvyDisabled);
        await Assert.That(l[1].Text).IsEqualTo("Preamp: -3 dB");
        apo.SetLineDisabled(0, "# [Curvy] Include: peace.txt", false);
        l = apo.ReadConfigTxt();
        await Assert.That(l[0].Text).IsEqualTo("Include: peace.txt");
        await Assert.That(l[0].Kind).IsEqualTo(ConfigLineKind.Active);
    }

    [Test]
    public void SetLineDisabled_OnCurvyInclude_Throws()
    {
        File.WriteAllText(cfg, ConfigTxt.CurvyIncludeLine + "\r\n");
        Assert.Throws<InvalidOperationException>(() => apo.SetLineDisabled(0, ConfigTxt.CurvyIncludeLine, true));
    }

    [Test]
    public async Task BackupOnce_DoesNotOverwriteExistingBackup()
    {
        File.WriteAllText(cfg, "first");
        apo.BackupOnce();
        File.WriteAllText(cfg, "second");
        apo.BackupOnce();
        await Assert.That(File.ReadAllText(cfg + ".curvy-backup")).IsEqualTo("first");
    }

    [Test]
    public async Task RestoreBackup_RestoresOriginalBytes()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, 65, 13, 10, 66 };
        File.WriteAllBytes(cfg, bytes);
        apo.BackupOnce();
        File.WriteAllText(cfg, "changed");
        await Assert.That(apo.RestoreBackup()).IsTrue();
        await Assert.That(File.ReadAllBytes(cfg)).IsEquivalentTo(bytes, CollectionOrdering.Matching);
    }

    [Test]
    public async Task RestoreBackup_ReturnsFalse_WhenNoBackup() => await Assert.That(apo.RestoreBackup()).IsFalse();

    [Test]
    public async Task WriteOutput_CreatesCurvyDir_AndLeavesNoTmpFile()
    {
        var r = new ChainResult(false, -2.0, ImmutableList<Filter>.Empty);
        apo.WriteOutput(r);
        await Assert.That(File.Exists(apo.OutputPath)).IsTrue();
        await Assert.That(File.ReadAllText(apo.OutputPath)).IsEqualTo(ApoFormatter.Format(r));
        await Assert.That(Directory.GetFiles(dir, "*.tmp", SearchOption.AllDirectories)).IsEmpty();
        await Assert.That(File.ReadAllBytes(apo.OutputPath)[0]).IsNotEqualTo(0xEF);
        await Assert.That(apo.IsWritable()).IsTrue();
    }

    [Test]
    public async Task SetLineDisabled_Throws_WhenTextMismatch()
    {
        File.WriteAllText(cfg, "Include: a.txt\r\nInclude: b.txt\r\n");
        Assert.Throws<ConfigChangedException>(() => apo.SetLineDisabled(0, "Include: b.txt", true));
        await Assert.That(File.ReadAllText(cfg)).IsEqualTo("Include: a.txt\r\nInclude: b.txt\r\n");
    }

    [Test]
    public async Task Windows1252_Config_SurvivesRewrites_ByteExact()
    {
        var latin = System.Text.Encoding.Latin1;
        var line = "Device: Lautsprecher (High Definition Audio-Ger\u00e4t)";
        File.WriteAllBytes(cfg, latin.GetBytes(line + "\r\nInclude: x.txt\r\n"));
        apo.EnsureCurvyInclude();
        apo.SetLineDisabled(1, "Include: x.txt", true);
        var expected = latin.GetBytes(line + "\r\n# [Curvy] Include: x.txt\r\n" + ConfigTxt.CurvyIncludeLine + "\r\n");
        await Assert.That(File.ReadAllBytes(cfg)).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Bom_IsPreserved_OnRewrite()
    {
        File.WriteAllBytes(cfg, [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes("Include: x.txt\r\n")]);
        apo.EnsureCurvyInclude();
        var b = File.ReadAllBytes(cfg);
        await Assert.That(b[..3]).IsEquivalentTo(new byte[] { 0xEF, 0xBB, 0xBF }, CollectionOrdering.Matching);
        await Assert.That(b[3]).IsEqualTo(0x49);
    }

    [Test]
    public async Task FirstChange_CreatesBackupOfOriginalBytes_AndLaterChangeKeepsIt()
    {
        var orig = System.Text.Encoding.ASCII.GetBytes("Include: x.txt");
        File.WriteAllBytes(cfg, orig);
        apo.EnsureCurvyInclude();
        await Assert.That(File.ReadAllBytes(cfg + ".curvy-backup")).IsEquivalentTo(orig, CollectionOrdering.Matching);
        apo.SetLineDisabled(0, "Include: x.txt", true);
        await Assert.That(File.ReadAllBytes(cfg + ".curvy-backup")).IsEquivalentTo(orig, CollectionOrdering.Matching);
        await Assert.That(apo.RestoreBackup()).IsTrue();
        await Assert.That(File.ReadAllBytes(cfg)).IsEquivalentTo(orig, CollectionOrdering.Matching);
        await Assert.That(Directory.GetFiles(dir, "*.tmp")).IsEmpty();
    }

    [Test]
    public async Task IsWritable_False_WhenDirMissing_AndDoesNotCreateIt()
    {
        var missing = Path.Combine(dir, "nope");
        await Assert.That(new ApoConfig(missing).IsWritable()).IsFalse();
        await Assert.That(Directory.Exists(missing)).IsFalse();
    }

    [Test]
    public async Task Classify_FlagsEqLikeLines()
    {
        var c = ConfigTxt.Classify(["Device: Speakers", "Include: peace.txt", "# hi", ""]);
        await Assert.That(c[0].IsEqLike).IsFalse(); await Assert.That(c[1].IsEqLike).IsTrue();
        await Assert.That(c[2].Kind).IsEqualTo(ConfigLineKind.Comment); await Assert.That(c[3].Kind).IsEqualTo(ConfigLineKind.Blank);
    }

    [Test]
    public async Task FindConfigDirectory_PrefersEnvOverride()
    {
        var old = Environment.GetEnvironmentVariable(ApoLocator.EnvOverride);
        try
        {
            Environment.SetEnvironmentVariable(ApoLocator.EnvOverride, dir);
            await Assert.That(ApoLocator.FindConfigDirectory()).IsEqualTo(dir);
        }
        finally { Environment.SetEnvironmentVariable(ApoLocator.EnvOverride, old); }
    }

    [Test]
    public async Task ConfigTxtWriting_RaisedBeforeEachConfigTxtWrite_Only()
    {
        File.WriteAllText(cfg, "Include: peace.txt\r\n");
        var raised = 0;
        apo.ConfigTxtWriting += (_, _) => raised++;
        await Assert.That(apo.EnsureCurvyInclude()).IsTrue();
        await Assert.That(raised).IsEqualTo(1);
        await Assert.That(apo.EnsureCurvyInclude()).IsFalse(); // nothing to write
        await Assert.That(raised).IsEqualTo(1);
        apo.SetLineDisabled(0, "Include: peace.txt", true);
        await Assert.That(raised).IsEqualTo(2);
        await Assert.That(apo.RestoreBackup()).IsTrue();
        await Assert.That(raised).IsEqualTo(3);
        apo.WriteOutput(new ChainResult(false, 0, ImmutableList<Filter>.Empty)); // curvy.txt, not config.txt
        await Assert.That(raised).IsEqualTo(3);
    }
}