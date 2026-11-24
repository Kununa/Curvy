using System.Text;
using Curvy.Core.Chain;

namespace Curvy.Core.Apo;

public sealed class ApoConfig(string configDir)
{
    private const string BackupSuffix = ".curvy-backup";

    public string ConfigTxtPath { get; } = Path.Combine(configDir, "config.txt");
    public string OutputPath { get; } = Path.Combine(configDir, "Curvy", "curvy.txt");
    private string BackupPath => ConfigTxtPath + BackupSuffix;

    private static readonly Encoding Latin1 = Encoding.Latin1;
    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    private sealed record RawFile(List<string> Lines, bool Bom, bool Utf8);

    private RawFile ReadRaw()
    {
        if (!File.Exists(ConfigTxtPath)) return new RawFile([], false, true);
        var bytes = File.ReadAllBytes(ConfigTxtPath);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var body = bom ? bytes.AsSpan(3) : bytes.AsSpan();
        string text;
        var utf8 = true;
        try { text = StrictUtf8.GetString(body); }
        catch (DecoderFallbackException) { text = Latin1.GetString(body); utf8 = false; }
        if (text.Length == 0) return new RawFile([], bom, utf8);
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        if (lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1); // trailing newline
        return new RawFile(lines, bom, utf8);
    }

    /// <summary>
    /// Raised (synchronously) right before Curvy writes <c>config.txt</c>, so a file watcher can
    /// tell Curvy's own writes from other apps' changes. Not raised for <c>curvy.txt</c> or the backup.
    /// </summary>
    public event EventHandler? ConfigTxtWriting;

    public IReadOnlyList<ConfigLine> ReadConfigTxt() => ConfigTxt.Classify(ReadRaw().Lines);

    private void WriteLines(IEnumerable<string> lines, RawFile raw)
    {
        var sb = new StringBuilder();
        foreach (var l in lines) sb.Append(l).Append("\r\n");
        Directory.CreateDirectory(configDir);
        BackupOnce();
        var enc = raw.Utf8 ? StrictUtf8 : Latin1;
        var body = enc.GetBytes(sb.ToString());
        var bytes = raw.Bom ? [.. new byte[] { 0xEF, 0xBB, 0xBF }, .. body] : body;
        ConfigTxtWriting?.Invoke(this, EventArgs.Empty);
        AtomicFile.WriteAllBytes(ConfigTxtPath, bytes);
    }

    public bool EnsureCurvyInclude()
    {
        var raw = ReadRaw();
        var classified = ConfigTxt.Classify(raw.Lines);
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
        WriteLines(lines, raw);
        return true;
    }

    public void SetLineDisabled(int index, string expectedText, bool disabled)
    {
        var raw = ReadRaw();
        var classified = ConfigTxt.Classify(raw.Lines);
        if (index < 0 || index >= classified.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var line = classified[index];
        var stripped = line.Kind == ConfigLineKind.CurvyDisabled ? line.Text[ConfigTxt.DisabledPrefix.Length..] : line.Text;
        if (line.Text != expectedText && stripped != expectedText)
            throw new ConfigChangedException();
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
        WriteLines(lines, raw);
    }

    public void BackupOnce()
    {
        if (File.Exists(BackupPath) || !File.Exists(ConfigTxtPath)) return;
        try { AtomicFile.CopyAtomic(ConfigTxtPath, BackupPath, overwrite: false); }
        catch (IOException) when (File.Exists(BackupPath)) { }
    }

    public bool RestoreBackup()
    {
        if (!File.Exists(BackupPath)) return false;
        ConfigTxtWriting?.Invoke(this, EventArgs.Empty);
        AtomicFile.CopyAtomic(BackupPath, ConfigTxtPath, overwrite: true);
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
            if (!Directory.Exists(configDir)) return false;
            var probe = Path.Combine(configDir, ".curvy-write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
