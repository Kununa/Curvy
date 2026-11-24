using System.Collections.Immutable;
using Curvy.Core.Apo;
using Curvy.Core.Engine;
using Curvy.Core.Filters;
using Curvy.Core.Model;
using Curvy.Core.Storage;
using Xunit;

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

    private static Profile WithFilter(string name, double hz) =>
        Profile.New(name) with { Preference = new Preference(new ToneControls(), [new Filter(FilterType.Peaking, hz, 3, 1)]) };

    [Fact]
    public void Activate_WritesChainOfThatProfile()
    {
        var e = NewEngine();
        var p = WithFilter("A", 1234);
        e.SaveProfile(p);
        e.Activate(p.Id);
        Assert.Equal(p.Id, e.ActiveProfile!.Id);
        Assert.Contains("Fc 1234 Hz", Output());
    }

    [Fact]
    public void Activate_Null_WritesUniversalOnly()
    {
        var e = NewEngine();
        var p = WithFilter("A", 1234);
        e.SaveProfile(p);
        e.SetUniversal(new Preference(new ToneControls(), [new Filter(FilterType.Peaking, 777, 2, 1)]));
        e.Activate(p.Id);
        e.Activate(null);
        Assert.Null(e.ActiveProfile);
        var o = Output();
        Assert.Contains("Fc 777 Hz", o);
        Assert.DoesNotContain("Fc 1234 Hz", o);
    }

    [Fact]
    public void MasterOff_WritesBypassedFile()
    {
        var e = NewEngine();
        e.SetMasterEnabled(false);
        Assert.Contains("# EQ off", Output());
        Assert.True(e.Current.Bypassed);
    }

    [Fact]
    public void DeleteActive_ClearsActive_AndRewrites()
    {
        var e = NewEngine();
        var p = WithFilter("A", 1234);
        e.SaveProfile(p);
        e.Activate(p.Id);
        e.DeleteProfile(p.Id);
        Assert.Null(e.Settings.ActiveProfileId);
        Assert.Null(e.ActiveProfile);
        Assert.DoesNotContain("Fc 1234 Hz", Output());
        Assert.Empty(e.Profiles);
    }

    [Fact]
    public void Apply_AddsIncludeOnce()
    {
        var e = NewEngine();
        e.Apply();
        e.Apply();
        var n = File.ReadAllLines(NewApo().ConfigTxtPath).Count(l => l == ConfigTxt.CurvyIncludeLine);
        Assert.Equal(1, n);
    }

    [Fact]
    public void DisableForeignLines_BacksUpFirst_AndRemembersText()
    {
        var e = NewEngine();
        var idx = NewApo().ReadConfigTxt().First(l => l.Text == "Include: peace.txt").Index;
        e.DisableForeignLines([idx]);
        Assert.True(File.Exists(NewApo().ConfigTxtPath + ".curvy-backup"));
        Assert.Contains("Include: peace.txt", File.ReadAllText(NewApo().ConfigTxtPath + ".curvy-backup"));
        Assert.Contains("# [Curvy] Include: peace.txt", File.ReadAllText(NewApo().ConfigTxtPath));
        Assert.Equal(["Include: peace.txt"], e.Settings.DisabledForeignLines);
        Assert.Equal(["Include: peace.txt"], new SettingsStore(SetDir).Load().DisabledForeignLines);
    }

    [Fact]
    public void CheckAndRepair_ReAddsMissingInclude()
    {
        var e = NewEngine();
        e.Apply();
        File.WriteAllText(NewApo().ConfigTxtPath, "Include: peace.txt\r\n");
        var h = e.CheckAndRepair();
        Assert.True(h.RepairedInclude);
        Assert.Contains(ConfigTxt.CurvyIncludeLine, File.ReadAllText(NewApo().ConfigTxtPath));
    }

    [Fact]
    public void CheckAndRepair_ReportsReEnabledLine_WithoutDisablingIt()
    {
        var e = NewEngine();
        e.Apply();
        var idx = NewApo().ReadConfigTxt().First(l => l.Text == "Include: peace.txt").Index;
        e.DisableForeignLines([idx]);
        File.AppendAllText(NewApo().ConfigTxtPath, "Include: peace.txt\r\n");
        var h = e.CheckAndRepair();
        Assert.Single(h.ReEnabledLines);
        Assert.Equal("Include: peace.txt", h.ReEnabledLines[0].Text);
        Assert.Contains(h.ActiveEqLikeForeignLines, l => l.Text == "Include: peace.txt");
        var active = NewApo().ReadConfigTxt().Count(l => l.Kind == ConfigLineKind.Active && l.Text == "Include: peace.txt");
        Assert.Equal(1, active);
    }

    [Fact]
    public void Settings_SurviveNewEngineInstance()
    {
        var e = NewEngine();
        var p = WithFilter("A", 1234);
        e.SaveProfile(p);
        e.Activate(p.Id);
        e.SetExtraHeadroom(50);
        var e2 = NewEngine();
        Assert.Equal(p.Id, e2.ActiveProfile!.Id);
        Assert.Equal(12, e2.Settings.ExtraHeadroomDb);
    }

    [Fact]
    public void Apply_RecordsLastError_WhenConfigDirMissing()
    {
        var blocker = Path.Combine(_root, "blocker");
        File.WriteAllText(blocker, "x"); // a file where a directory is needed: not creatable
        var bad = Path.Combine(blocker, "apo");
        var e = new CurvyEngine(new ProfileStore(ProfDir), new SettingsStore(SetDir), new ApoConfig(bad));
        var raised = 0;
        e.Changed += (_, _) => raised++;
        e.Apply();
        Assert.NotNull(e.LastError);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void DisableForeignLines_RejectsNonActiveOrOutOfRange_WithoutWriting()
    {
        var e = NewEngine();
        e.Apply();
        var before = File.ReadAllText(NewApo().ConfigTxtPath);
        var includeIdx = NewApo().ReadConfigTxt().First(l => l.Kind == ConfigLineKind.CurvyInclude).Index;
        var peace = NewApo().ReadConfigTxt().First(l => l.Text == "Include: peace.txt").Index;
        Assert.Throws<ArgumentException>(() => e.DisableForeignLines([peace, includeIdx]));
        Assert.Throws<ArgumentException>(() => e.DisableForeignLines([peace, 99]));
        Assert.Equal(before, File.ReadAllText(NewApo().ConfigTxtPath));
        Assert.Empty(e.Settings.DisabledForeignLines);
    }

    // A deterministic mid-loop failure cannot be injected (indices are validated up front and
    // SetLineDisabled re-reads the file); the finally-persist path is covered by the success case.
    [Fact]
    public void DisableForeignLines_RecordsEachLineAndRaisesChanged()
    {
        File.WriteAllText(NewApo().ConfigTxtPath, "Include: a.txt\r\nInclude: b.txt\r\n");
        var e = NewEngine();
        var raised = 0;
        e.Changed += (_, _) => raised++;
        e.DisableForeignLines([0, 1]);
        Assert.Equal(["Include: a.txt", "Include: b.txt"], e.Settings.DisabledForeignLines);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void CheckAndRepair_RecordsLastError_OnIoFailure()
    {
        var e = NewEngine();
        using var hold = new FileStream(NewApo().ConfigTxtPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var h = e.CheckAndRepair();
        Assert.False(h.RepairedInclude);
        Assert.Empty(h.ReEnabledLines);
        Assert.Empty(h.ActiveEqLikeForeignLines);
        Assert.NotNull(e.LastError);
    }

    [Fact]
    public void Activate_UnknownId_TreatedAsNoProfile()
    {
        var e = NewEngine();
        e.Activate(Guid.NewGuid());
        Assert.Null(e.Settings.ActiveProfileId);
        Assert.Null(new SettingsStore(SetDir).Load().ActiveProfileId);
    }

    [Fact]
    public void SaveProfile_Inactive_DoesNotRewriteOutput_ButRaisesChanged()
    {
        var e = NewEngine();
        e.Apply();
        var path = NewApo().OutputPath;
        File.SetLastWriteTimeUtc(path, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var raised = 0;
        e.Changed += (_, _) => raised++;
        e.SaveProfile(WithFilter("B", 500));
        Assert.Equal(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(path));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Profiles_SortedByName_IgnoringCase()
    {
        var e = NewEngine();
        e.SaveProfile(WithFilter("charlie", 1000));
        e.SaveProfile(WithFilter("Alpha", 1000));
        e.SaveProfile(WithFilter("bravo", 1000));
        Assert.Equal(["Alpha", "bravo", "charlie"], e.Profiles.Select(p => p.Name));
    }

    [Fact]
    public void SetExtraHeadroom_ClampsNegativeToZero()
    {
        var e = NewEngine();
        e.SetExtraHeadroom(-3);
        Assert.Equal(0, e.Settings.ExtraHeadroomDb);
    }
}
