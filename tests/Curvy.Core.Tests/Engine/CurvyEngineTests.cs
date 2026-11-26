using System.Collections.Generic;
using System.IO;
using System.Collections.Immutable;
using Curvy.Core.Apo;
using Curvy.Core.Engine;
using Curvy.Core.Filters;
using Curvy.Core.Model;
using Curvy.Core.Storage;
using System.Threading.Tasks;

namespace Curvy.Core.Tests.Engine;

public sealed class CurvyEngineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "curvy-engine-" + Guid.NewGuid().ToString("N"));
    private string ProfDir => Path.Combine(_root, "profiles");
    private string SetDir => Path.Combine(_root, "settings");
    private string ApoDir => Path.Combine(_root, "apo");

    public CurvyEngineTests()
    {
        Directory.CreateDirectory(ApoDir);
        File.WriteAllText(Path.Combine(ApoDir, "config.txt"), "Include: peace.txt\r\n");
    }
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private ApoConfig NewApo() => new(ApoDir);
    private CurvyEngine NewEngine() => new(new ProfileStore(ProfDir), new SettingsStore(SetDir), NewApo());
    private string Output() => File.ReadAllText(NewApo().OutputPath);
    private ConfigLine Line(int index) => NewApo().ReadConfigTxt()[index];
    private ConfigLine Peace() => NewApo().ReadConfigTxt().First(l => l.Text == "Include: peace.txt");
    private string BackupPath => NewApo().ConfigTxtPath + ".curvy-backup";

    private static Profile WithFilter(string name, double hz) =>
        Profile.New(name) with { Preference = new Preference(new ToneControls(), [new Filter(FilterType.Peaking, hz, 3, 1)]) };

    [Test]
    public async Task Activate_WritesChainOfThatProfile()
    {
        var e = NewEngine();
        var p = WithFilter("A", 1234);
        e.SaveProfile(p);
        e.Activate(p.Id);
        await Assert.That(e.ActiveProfile!.Id).IsEqualTo(p.Id);
        await Assert.That(Output()).Contains("Fc 1234 Hz");
    }

    [Test]
    public async Task Activate_Null_WritesUniversalOnly()
    {
        var e = NewEngine();
        var p = WithFilter("A", 1234);
        e.SaveProfile(p);
        e.SetUniversal(new Preference(new ToneControls(), [new Filter(FilterType.Peaking, 777, 2, 1)]));
        e.Activate(p.Id);
        e.Activate(null);
        await Assert.That(e.ActiveProfile).IsNull();
        var o = Output();
        await Assert.That(o).Contains("Fc 777 Hz");
        await Assert.That(o).DoesNotContain("Fc 1234 Hz");
    }

    [Test]
    public async Task MasterOff_WritesBypassedFile()
    {
        var e = NewEngine();
        e.SetMasterEnabled(false);
        await Assert.That(Output()).Contains("# EQ off");
        await Assert.That(e.Current.Bypassed).IsTrue();
    }

    [Test]
    public async Task DeleteActive_ClearsActive_AndRewrites()
    {
        var e = NewEngine();
        var p = WithFilter("A", 1234);
        e.SaveProfile(p);
        e.Activate(p.Id);
        e.DeleteProfile(p.Id);
        await Assert.That(e.Settings.ActiveProfileId).IsNull();
        await Assert.That(e.ActiveProfile).IsNull();
        await Assert.That(Output()).DoesNotContain("Fc 1234 Hz");
        await Assert.That(e.Profiles).IsEmpty();
    }

    [Test]
    public async Task Apply_AddsIncludeOnce()
    {
        var e = NewEngine();
        e.Apply();
        e.Apply();
        var n = File.ReadAllLines(NewApo().ConfigTxtPath).Count(l => l == ConfigTxt.CurvyIncludeLine);
        await Assert.That(n).IsEqualTo(1);
    }

    [Test]
    public async Task DisableForeignLines_BacksUpFirst_AndRemembersText()
    {
        var e = NewEngine();
        var idx = NewApo().ReadConfigTxt().First(l => l.Text == "Include: peace.txt").Index;
        e.DisableForeignLines([Line(idx)]);
        await Assert.That(File.Exists(NewApo().ConfigTxtPath + ".curvy-backup")).IsTrue();
        await Assert.That(File.ReadAllText(NewApo().ConfigTxtPath + ".curvy-backup")).Contains("Include: peace.txt");
        await Assert.That(File.ReadAllText(NewApo().ConfigTxtPath)).Contains("# [Curvy] Include: peace.txt");
        await Assert.That(e.Settings.DisabledForeignLines).IsEquivalentTo(["Include: peace.txt"], EqualityComparer<string>.Default, CollectionOrdering.Matching);
        await Assert.That(new SettingsStore(SetDir).Load().DisabledForeignLines).IsEquivalentTo(["Include: peace.txt"], EqualityComparer<string>.Default, CollectionOrdering.Matching);
    }

    [Test]
    public async Task CheckAndRepair_ReAddsMissingInclude()
    {
        var e = NewEngine();
        e.Apply();
        File.WriteAllText(NewApo().ConfigTxtPath, "Include: peace.txt\r\n");
        var h = e.CheckAndRepair();
        await Assert.That(h.RepairedInclude).IsTrue();
        await Assert.That(File.ReadAllText(NewApo().ConfigTxtPath)).Contains(ConfigTxt.CurvyIncludeLine);
    }

    [Test]
    public async Task CheckAndRepair_ReportsReEnabledLine_WithoutDisablingIt()
    {
        var e = NewEngine();
        e.Apply();
        var idx = NewApo().ReadConfigTxt().First(l => l.Text == "Include: peace.txt").Index;
        e.DisableForeignLines([Line(idx)]);
        File.AppendAllText(NewApo().ConfigTxtPath, "Include: peace.txt\r\n");
        var h = e.CheckAndRepair();
        await Assert.That(h.ReEnabledLines).HasSingleItem();
        await Assert.That(h.ReEnabledLines[0].Text).IsEqualTo("Include: peace.txt");
        await Assert.That(h.ActiveEqLikeForeignLines.Any(l => l.Text == "Include: peace.txt")).IsTrue();
        var active = NewApo().ReadConfigTxt().Count(l => l.Kind == ConfigLineKind.Active && l.Text == "Include: peace.txt");
        await Assert.That(active).IsEqualTo(1);
    }

    [Test]
    public async Task Settings_SurviveNewEngineInstance()
    {
        var e = NewEngine();
        var p = WithFilter("A", 1234);
        e.SaveProfile(p);
        e.Activate(p.Id);
        e.SetExtraHeadroom(50);
        var e2 = NewEngine();
        await Assert.That(e2.ActiveProfile!.Id).IsEqualTo(p.Id);
        await Assert.That(e2.Settings.ExtraHeadroomDb).IsEqualTo(12);
    }

    [Test]
    public async Task Apply_RecordsLastError_WhenConfigDirMissing()
    {
        var blocker = Path.Combine(_root, "blocker");
        File.WriteAllText(blocker, "x"); // a file where a directory is needed: not creatable
        var bad = Path.Combine(blocker, "apo");
        var e = new CurvyEngine(new ProfileStore(ProfDir), new SettingsStore(SetDir), new ApoConfig(bad));
        var raised = 0;
        e.Changed += (_, _) => raised++;
        e.Apply();
        await Assert.That(e.LastError).IsNotNull();
        await Assert.That(raised).IsEqualTo(1);
    }

    [Test]
    public async Task DisableForeignLines_RejectsNonActiveOrOutOfRange_WithoutWriting()
    {
        var e = NewEngine();
        e.Apply();
        var before = File.ReadAllText(NewApo().ConfigTxtPath);
        var includeIdx = NewApo().ReadConfigTxt().First(l => l.Kind == ConfigLineKind.CurvyInclude).Index;
        var peace = NewApo().ReadConfigTxt().First(l => l.Text == "Include: peace.txt").Index;
        Assert.Throws<ArgumentException>(() => e.DisableForeignLines([Line(peace), Line(includeIdx)]));
        await Assert.That(() =>
            e.DisableForeignLines([Line(peace), new ConfigLine(99, "Include: x.txt", ConfigLineKind.Active)])).Throws<InvalidOperationException>();
        await Assert.That(File.ReadAllText(NewApo().ConfigTxtPath)).IsEqualTo(before);
        await Assert.That(e.Settings.DisabledForeignLines).IsEmpty();
    }

    // A deterministic mid-loop failure cannot be injected (indices are validated up front and
    // SetLineDisabled re-reads the file); the finally-persist path is covered by the success case.
    [Test]
    public async Task DisableForeignLines_RecordsEachLineAndRaisesChanged()
    {
        File.WriteAllText(NewApo().ConfigTxtPath, "Include: a.txt\r\nInclude: b.txt\r\n");
        var e = NewEngine();
        var raised = 0;
        e.Changed += (_, _) => raised++;
        e.DisableForeignLines([Line(0), Line(1)]);
        await Assert.That(e.Settings.DisabledForeignLines).IsEquivalentTo(["Include: a.txt", "Include: b.txt"], EqualityComparer<string>.Default, CollectionOrdering.Matching);
        await Assert.That(raised).IsEqualTo(1);
    }

    [Test]
    public async Task CheckAndRepair_RecordsLastError_OnIoFailure()
    {
        var e = NewEngine();
        using var hold = new FileStream(NewApo().ConfigTxtPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var h = e.CheckAndRepair();
        await Assert.That(h.RepairedInclude).IsFalse();
        await Assert.That(h.ReEnabledLines).IsEmpty();
        await Assert.That(h.ActiveEqLikeForeignLines).IsEmpty();
        await Assert.That(e.LastError).IsNotNull();
    }

    [Test]
    public async Task Activate_UnknownId_TreatedAsNoProfile()
    {
        var e = NewEngine();
        e.Activate(Guid.NewGuid());
        await Assert.That(e.Settings.ActiveProfileId).IsNull();
        await Assert.That(new SettingsStore(SetDir).Load().ActiveProfileId).IsNull();
    }

    [Test]
    public async Task SaveProfile_Inactive_DoesNotRewriteOutput_ButRaisesChanged()
    {
        var e = NewEngine();
        e.Apply();
        var path = NewApo().OutputPath;
        File.SetLastWriteTimeUtc(path, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var raised = 0;
        e.Changed += (_, _) => raised++;
        e.SaveProfile(WithFilter("B", 500));
        await Assert.That(File.GetLastWriteTimeUtc(path)).IsEqualTo(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(raised).IsEqualTo(1);
    }

    [Test]
    public async Task Profiles_SortedByName_IgnoringCase()
    {
        var e = NewEngine();
        e.SaveProfile(WithFilter("charlie", 1000));
        e.SaveProfile(WithFilter("Alpha", 1000));
        e.SaveProfile(WithFilter("bravo", 1000));
        await Assert.That(e.Profiles.Select(p => p.Name)).IsEquivalentTo(["Alpha", "bravo", "charlie"], EqualityComparer<string>.Default, CollectionOrdering.Matching);
    }

    [Test]
    public async Task SetExtraHeadroom_ClampsNegativeToZero()
    {
        var e = NewEngine();
        e.SetExtraHeadroom(-3);
        await Assert.That(e.Settings.ExtraHeadroomDb).IsEqualTo(0);
    }

    [Test]
    public async Task SetPreamp_ManualClampsAndPersists_AndWritesOutput()
    {
        var e = NewEngine();
        e.SetPreamp(PreampMode.Manual, -50);
        await Assert.That(e.Settings.PreampMode).IsEqualTo(PreampMode.Manual);
        await Assert.That(e.Settings.ManualPreampDb).IsEqualTo(-30);
        await Assert.That(e.Current.PreampDb).IsEqualTo(-30);
        e.SetPreamp(PreampMode.Manual, 20);
        await Assert.That(e.Settings.ManualPreampDb).IsEqualTo(12);
        var e2 = NewEngine();
        await Assert.That(e2.Settings.PreampMode).IsEqualTo(PreampMode.Manual);
        await Assert.That(File.ReadAllText(Path.Combine(ApoDir, "Curvy", "curvy.txt"))).Contains("Preamp: 12");
    }

    [Test]
    public async Task SetFirstRunShown_Persists_WithoutRewritingOutput()
    {
        var e = NewEngine();
        e.Apply();
        var path = NewApo().OutputPath;
        var stamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);
        var raised = 0;
        e.Changed += (_, _) => raised++;
        e.SetFirstRunShown(true);
        await Assert.That(e.Settings.FirstRunShown).IsTrue();
        await Assert.That(new SettingsStore(SetDir).Load().FirstRunShown).IsTrue();
        await Assert.That(File.GetLastWriteTimeUtc(path)).IsEqualTo(stamp);
        await Assert.That(raised).IsEqualTo(1);
    }

    [Test]
    public async Task SetStartWithWindows_Persists()
    {
        var e = NewEngine();
        e.SetStartWithWindows(true);
        await Assert.That(e.Settings.StartWithWindows).IsTrue();
        await Assert.That(new SettingsStore(SetDir).Load().StartWithWindows).IsTrue();
        e.SetStartWithWindows(false);
        await Assert.That(new SettingsStore(SetDir).Load().StartWithWindows).IsFalse();
    }

    [Test]
    public async Task EnableForeignLine_ReEnablesIt_AndForgetsIt()
    {
        var e = NewEngine();
        e.Apply();
        var idx = NewApo().ReadConfigTxt().First(l => l.Text == "Include: peace.txt").Index;
        e.DisableForeignLines([Line(idx)]);
        var disabled = NewApo().ReadConfigTxt()[idx];
        await Assert.That(disabled.Kind).IsEqualTo(ConfigLineKind.CurvyDisabled);

        e.EnableForeignLine(idx, disabled.Text);

        await Assert.That(NewApo().ReadConfigTxt()[idx].Kind).IsEqualTo(ConfigLineKind.Active);
        await Assert.That(e.Settings.DisabledForeignLines).IsEmpty();
        await Assert.That(new SettingsStore(SetDir).Load().DisabledForeignLines).IsEmpty();
        await Assert.That(e.CheckAndRepair().ReEnabledLines).IsEmpty(); // a deliberate re-enable is not "another app"
    }

    [Test]
    public async Task EnableForeignLine_Throws_WhenFileChanged()
    {
        var e = NewEngine();
        e.Apply();
        var idx = NewApo().ReadConfigTxt().First(l => l.Text == "Include: peace.txt").Index;
        e.DisableForeignLines([Line(idx)]);
        Assert.Throws<ConfigChangedException>(() => e.EnableForeignLine(idx, "# [Curvy] Include: other.txt"));
        await Assert.That(e.Settings.DisabledForeignLines).IsEquivalentTo(["Include: peace.txt"], EqualityComparer<string>.Default, CollectionOrdering.Matching);
    }

    [Test]
    public async Task ReadConfigLines_And_ConfigTxtPath_ExposeConfig()
    {
        var e = NewEngine();
        await Assert.That(e.ConfigTxtPath).IsEqualTo(NewApo().ConfigTxtPath);
        await Assert.That(e.ReadConfigLines()[0].Text).IsEqualTo("Include: peace.txt");
    }


    [Test]
    public async Task RestoreOriginalConfig_ReturnsFalse_WithoutBackup()
    {
        var e = NewEngine();
        await Assert.That(e.RestoreOriginalConfig()).IsFalse();
        await Assert.That(File.ReadAllText(NewApo().ConfigTxtPath)).IsEqualTo("Include: peace.txt\r\n");
    }

    // ── Fix round 1 ─────────────────────────────────────────────────────

    [Test]
    public async Task DisableForeignLines_ShiftedLine_Throws_AndLeavesFileUnchanged()
    {
        var e = NewEngine();
        e.Apply();
        var peace = Peace(); // read, then another app inserts a line above it
        File.WriteAllText(NewApo().ConfigTxtPath, "Preamp: -3 dB\r\n" + File.ReadAllText(NewApo().ConfigTxtPath));
        var before = File.ReadAllBytes(NewApo().ConfigTxtPath);

        Assert.Throws<ConfigChangedException>(() => e.DisableForeignLines([peace]));

        await Assert.That(File.ReadAllBytes(NewApo().ConfigTxtPath)).IsEquivalentTo(before, EqualityComparer<byte>.Default, CollectionOrdering.Matching);
        await Assert.That(e.Settings.DisabledForeignLines).IsEmpty();
    }

    [Test]
    public async Task CheckAndRepair_RepairDenied_ReportsMissing_WithoutWriting()
    {
        var e = NewEngine();
        e.Apply();
        File.WriteAllText(NewApo().ConfigTxtPath, "Include: peace.txt\r\n");
        var h = e.CheckAndRepair(allowRepair: () => false);
        await Assert.That(h.RepairedInclude).IsFalse();
        await Assert.That(h.IncludeMissing).IsTrue();
        await Assert.That(File.ReadAllText(NewApo().ConfigTxtPath)).IsEqualTo("Include: peace.txt\r\n");
    }

    [Test]
    public async Task CheckAndRepair_RepairAllowed_DoesNotReportMissing()
    {
        var e = NewEngine();
        e.Apply();
        File.WriteAllText(NewApo().ConfigTxtPath, "Include: peace.txt\r\n");
        var asked = 0;
        var h = e.CheckAndRepair(() => { asked++; return true; });
        await Assert.That(h.RepairedInclude).IsTrue();
        await Assert.That(h.IncludeMissing).IsFalse();
        await Assert.That(asked).IsEqualTo(1);
        await Assert.That(CountAsks(e)).IsEqualTo(0); // include present: the guard isn't consulted

        static int CountAsks(CurvyEngine engine)
        {
            var n = 0;
            engine.CheckAndRepair(() => { n++; return true; });
            return n;
        }
    }

    [Test]
    public async Task EnableForeignLine_OnReEnabledActiveLine_JustForgetsIt()
    {
        // "Keep it on" for a line another app re-enabled.
        var e = NewEngine();
        e.Apply();
        e.DisableForeignLines([Peace()]);
        var disabled = NewApo().ReadConfigTxt().First(l => l.Kind == ConfigLineKind.CurvyDisabled);
        var text = File.ReadAllText(NewApo().ConfigTxtPath)
            .Replace(disabled.Text, "Include: peace.txt");
        File.WriteAllText(NewApo().ConfigTxtPath, text);
        var reEnabled = await Assert.That(e.CheckAndRepair().ReEnabledLines).HasSingleItem();
        var before = File.ReadAllBytes(NewApo().ConfigTxtPath);

        e.EnableForeignLine(reEnabled.Index, reEnabled.Text);

        await Assert.That(e.Settings.DisabledForeignLines).IsEmpty();
        await Assert.That(e.CheckAndRepair().ReEnabledLines).IsEmpty();
        await Assert.That(File.ReadAllBytes(NewApo().ConfigTxtPath)).IsEquivalentTo(before, EqualityComparer<byte>.Default, CollectionOrdering.Matching);
    }

    [Test]
    public async Task RestoreOriginalConfig_LeavesConfigByteIdentical_AndPauses()
    {
        var original = File.ReadAllBytes(NewApo().ConfigTxtPath);
        var e = NewEngine();
        e.Apply(); // first config.txt write backs up the original
        e.DisableForeignLines([Peace()]);

        await Assert.That(e.RestoreOriginalConfig()).IsTrue();

        await Assert.That(File.ReadAllBytes(NewApo().ConfigTxtPath)).IsEquivalentTo(original, EqualityComparer<byte>.Default, CollectionOrdering.Matching);
        await Assert.That(e.Settings.IntegrationPaused).IsTrue();
        await Assert.That(e.Settings.DisabledForeignLines).IsEmpty();
        await Assert.That(new SettingsStore(SetDir).Load().IntegrationPaused).IsTrue();
    }

    [Test]
    public async Task CheckAndRepair_WhilePaused_DoesNotReAddInclude()
    {
        var original = File.ReadAllBytes(NewApo().ConfigTxtPath);
        var e = NewEngine();
        e.Apply();
        e.RestoreOriginalConfig();

        var h = e.CheckAndRepair();

        await Assert.That(h.RepairedInclude).IsFalse();
        await Assert.That(h.IncludeMissing).IsFalse();
        await Assert.That(h.ReEnabledLines).IsEmpty();
        await Assert.That(h.ActiveEqLikeForeignLines).IsEmpty();
        await Assert.That(File.ReadAllBytes(NewApo().ConfigTxtPath)).IsEquivalentTo(original, EqualityComparer<byte>.Default, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Apply_WhilePaused_DoesNotTouchConfigTxt()
    {
        var original = File.ReadAllBytes(NewApo().ConfigTxtPath);
        var e = NewEngine();
        e.Apply();
        e.RestoreOriginalConfig();
        File.Delete(NewApo().OutputPath);

        e.SetMasterEnabled(false); // Mutate -> Apply
        e.Apply();

        await Assert.That(File.ReadAllBytes(NewApo().ConfigTxtPath)).IsEquivalentTo(original, EqualityComparer<byte>.Default, CollectionOrdering.Matching);
        await Assert.That(Output()).Contains("# EQ off"); // curvy.txt is still written
        await Assert.That(NewEngine().Settings.IntegrationPaused).IsTrue(); // survives a restart
    }

    [Test]
    public async Task Resume_ReAddsInclude_KeepsBackup()
    {
        var original = File.ReadAllBytes(NewApo().ConfigTxtPath);
        var e = NewEngine();
        e.Apply();
        e.SetFirstRunShown(true);
        e.RestoreOriginalConfig();

        e.ResumeIntegration();

        await Assert.That(e.Settings.IntegrationPaused).IsFalse();
        await Assert.That(e.Settings.FirstRunShown).IsFalse(); // the first-run check runs again
        await Assert.That(NewApo().ReadConfigTxt().Any(l => l.Kind == ConfigLineKind.CurvyInclude)).IsTrue();
        await Assert.That(File.ReadAllBytes(BackupPath)).IsEquivalentTo(original, EqualityComparer<byte>.Default, CollectionOrdering.Matching);
    }

    [Test]
    public async Task RestoreOriginalConfig_ReturnsFalse_WithoutBackup_AndDoesNotPause()
    {
        var e = NewEngine();
        await Assert.That(e.RestoreOriginalConfig()).IsFalse();
        await Assert.That(e.Settings.IntegrationPaused).IsFalse();
    }
}