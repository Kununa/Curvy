using System.Text;
using Curvy.Core.Chain;

namespace Curvy.Core.Apo;

public sealed class ApoConfig(string configDir)
{
    private const string BackupSuffix = ".curvy-backup";

    public string ConfigTxtPath { get; } = Path.Combine(configDir, "config.txt");
    public string OutputPath { get; } = Path.Combine(configDir, "Curvy", "curvy.txt");
    private string BackupPath => ConfigTxtPath + BackupSuffix;

    public IReadOnlyList<ConfigLine> ReadConfigTxt() => ConfigTxt.Classify(ReadRawLines());

    private List<string> ReadRawLines()
    {
        if (!File.Exists(ConfigTxtPath)) return [];
        var text = File.ReadAllText(ConfigTxtPath);
        if (text.Length == 0) return [];
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        if (lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1); // trailing newline
        return lines;
    }

    private void WriteLines(IEnumerable<string> lines)
    {
        var sb = new StringBuilder();
        foreach (var l in lines) sb.Append(l).Append("\r\n");
        Directory.CreateDirectory(configDir);
        AtomicFile.WriteAllText(ConfigTxtPath, sb.ToString());
    }

    public bool EnsureCurvyInclude()
    {
        var classified = ReadConfigTxt();
        var includes = classified.Where(l => l.Kind == ConfigLineKind.CurvyInclude).ToList();
        if (includes.Count == 1) return false;

        var lines = classified.Select(l => l.Text).ToList();
        if (includes.Count == 0)
        {
            lines.Add(ConfigTxt.CurvyIncludeLine);
        }
        else
        {
            var drop = includes.Skip(1).Select(l => l.Index).ToHashSet();
            lines = lines.Where((_, i) => !drop.Contains(i)).ToList();
        }
        WriteLines(lines);
        return true;
    }

    public void SetLineDisabled(int index, bool disabled)
    {
        var classified = ReadConfigTxt();
        if (index < 0 || index >= classified.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var line = classified[index];
        if (line.Kind == ConfigLineKind.CurvyInclude)
            throw new InvalidOperationException("Curvy's own include line cannot be toggled.");

        string? replacement = null;
        if (disabled && line.Kind is ConfigLineKind.Active)
            replacement = ConfigTxt.DisabledPrefix + line.Text;
        else if (!disabled && line.Kind == ConfigLineKind.CurvyDisabled)
            replacement = line.Text[ConfigTxt.DisabledPrefix.Length..];
        if (replacement is null) return;

        var lines = classified.Select(l => l.Text).ToList();
        lines[index] = replacement;
        WriteLines(lines);
    }

    public void BackupOnce()
    {
        if (File.Exists(BackupPath) || !File.Exists(ConfigTxtPath)) return;
        File.Copy(ConfigTxtPath, BackupPath, overwrite: false);
    }

    public bool RestoreBackup()
    {
        if (!File.Exists(BackupPath)) return false;
        File.Copy(BackupPath, ConfigTxtPath, overwrite: true);
        return true;
    }

    public void WriteOutput(ChainResult r)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
        AtomicFile.WriteAllText(OutputPath, ApoFormatter.Format(r));
    }

    public bool IsWritable()
    {
        try
        {
            Directory.CreateDirectory(configDir);
            var probe = Path.Combine(configDir, ".curvy-write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
