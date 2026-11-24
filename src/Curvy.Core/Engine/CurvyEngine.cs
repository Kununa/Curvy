using Curvy.Core.Apo;
using Curvy.Core.Chain;
using Curvy.Core.Model;
using Curvy.Core.Storage;

namespace Curvy.Core.Engine;

public record ConfigHealth(
    bool RepairedInclude,
    IReadOnlyList<ConfigLine> ReEnabledLines,
    IReadOnlyList<ConfigLine> ActiveEqLikeForeignLines);

public sealed class CurvyEngine
{
    private readonly ProfileStore _profiles;
    private readonly SettingsStore _settings;
    private readonly ApoConfig _apo;
    private List<Profile> _list;

    public CurvyEngine(ProfileStore profiles, SettingsStore settings, ApoConfig apo)
    {
        _profiles = profiles;
        _settings = settings;
        _apo = apo;
        _list = Sort(profiles.LoadAll());
        Settings = settings.Load();
        Current = EqChain.Build(ActiveProfile, Settings);
    }

    public IReadOnlyList<Profile> Profiles => _list;
    public CurvySettings Settings { get; private set; }
    public Profile? ActiveProfile =>
        Settings.ActiveProfileId is { } id ? _list.FirstOrDefault(p => p.Id == id) : null;
    public ChainResult Current { get; private set; }
    public string? LastError { get; private set; }
    public event EventHandler? Changed;

    private static List<Profile> Sort(IEnumerable<Profile> ps) =>
        ps.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();

    private void Mutate(CurvySettings s)
    {
        _settings.Save(s);
        Settings = s;
        Apply();
    }

    public void Activate(Guid? id) =>
        Mutate(Settings with { ActiveProfileId = id is { } g && _list.Any(p => p.Id == g) ? g : null });

    public void SetMasterEnabled(bool on) => Mutate(Settings with { MasterEnabled = on });

    public void SetUniversal(Preference p) => Mutate(Settings with { Universal = p });

    public void SetExtraHeadroom(double db) =>
        Mutate(Settings with { ExtraHeadroomDb = Math.Clamp(db, 0, 12) });

    public void SaveProfile(Profile p)
    {
        var stamped = p with { Modified = DateTimeOffset.UtcNow };
        _profiles.Save(stamped);
        _list = Sort(_list.Where(x => x.Id != stamped.Id).Append(stamped));
        if (stamped.Id == Settings.ActiveProfileId) Apply();
        else Changed?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteProfile(Guid id)
    {
        _profiles.Delete(id);
        _list = Sort(_list.Where(x => x.Id != id));
        Mutate(Settings.ActiveProfileId == id ? Settings with { ActiveProfileId = null } : Settings);
    }

    public void Apply()
    {
        Current = EqChain.Build(ActiveProfile, Settings);
        try
        {
            _apo.EnsureCurvyInclude();
            _apo.WriteOutput(Current);
            LastError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = ex.Message;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public ConfigHealth CheckAndRepair()
    {
        try
        {
            var repaired = _apo.EnsureCurvyInclude();
            var lines = _apo.ReadConfigTxt();
            var remembered = Settings.DisabledForeignLines;
            var reEnabled = lines
                .Where(l => l.Kind == ConfigLineKind.Active && remembered.Contains(l.Text.Trim()))
                .ToList();
            var eqLike = lines.Where(l => l.Kind == ConfigLineKind.Active && l.IsEqLike).ToList();
            return new ConfigHealth(repaired, reEnabled, eqLike);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = ex.Message;
            return new ConfigHealth(false, [], []);
        }
    }

    public void DisableForeignLines(IEnumerable<int> indices)
    {
        var wanted = indices.Distinct().ToList();
        var lines = _apo.ReadConfigTxt();
        foreach (var i in wanted)
        {
            if (i < 0 || i >= lines.Count || lines[i].Kind != ConfigLineKind.Active)
                throw new ArgumentException($"Line {i} is not an active config.txt line.", nameof(indices));
        }
        _apo.BackupOnce();
        var disabled = Settings.DisabledForeignLines;
        try
        {
            foreach (var i in wanted)
            {
                _apo.SetLineDisabled(i, lines[i].Text, true);
                var t = lines[i].Text.Trim();
                if (!disabled.Contains(t)) disabled = disabled.Add(t);
            }
        }
        finally
        {
            if (disabled.Count != Settings.DisabledForeignLines.Count)
                Mutate(Settings with { DisabledForeignLines = disabled });
        }
    }
}
