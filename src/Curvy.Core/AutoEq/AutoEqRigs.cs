namespace Curvy.Core.AutoEq;

/// <summary>
/// Measurement rigs the AutoEQ index leaves out. <c>results/INDEX.md</c> only names a rig for some
/// sources ("by crinacle on 711"); for the others ("by oratory1990") the repo keeps it in
/// <c>measurements/&lt;source&gt;/name_index.tsv</c> (columns <c>url, source_name, name, form, rig</c>).
/// </summary>
public static class AutoEqRigs
{
    public const string MeasurementsUrl = "https://raw.githubusercontent.com/jaakkopasanen/AutoEq/master/measurements/";

    /// <summary>Rigs per source: source → ("name|form" and "name") → rig.</summary>
    public sealed class Map : Dictionary<string, Dictionary<string, string>>
    {
        public Map() : base(StringComparer.Ordinal) { }
    }

    public static string NameIndexUrl(string source) => MeasurementsUrl + Uri.EscapeDataString(source) + "/name_index.tsv";

    /// <summary>Sources that have index entries without a rig.</summary>
    public static IReadOnlyList<string> SourcesMissingRig(IEnumerable<AutoEqIndexEntry> entries) =>
        entries.Where(e => string.IsNullOrEmpty(e.Rig)).Select(e => e.Source).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// Parses a <c>name_index.tsv</c>: "name|form" → rig and "name" → rig (first one wins). Rows without a
    /// name or rig are skipped; a file without the expected header yields an empty map.
    /// </summary>
    public static Dictionary<string, string> ParseNameIndex(string tsv)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = tsv.Split('\n');
        if (lines.Length == 0) return map;
        var header = lines[0].TrimEnd('\r').Split('\t');
        int nameCol = Array.IndexOf(header, "name"), formCol = Array.IndexOf(header, "form"), rigCol = Array.IndexOf(header, "rig");
        if (nameCol < 0 || rigCol < 0) return map;

        foreach (var raw in lines.Skip(1))
        {
            var cols = raw.TrimEnd('\r').Split('\t');
            string Col(int i) => i >= 0 && i < cols.Length ? cols[i].Trim() : "";
            var name = Col(nameCol);
            var rig = Col(rigCol);
            if (name.Length == 0 || rig.Length == 0) continue;
            map.TryAdd(Key(name, Col(formCol)), rig);
            map.TryAdd(name, rig);
        }
        return map;
    }

    /// <summary>Fills in the rig of entries that have none, from <paramref name="rigs"/>.</summary>
    public static IReadOnlyList<AutoEqIndexEntry> Apply(IReadOnlyList<AutoEqIndexEntry> entries, Map? rigs)
    {
        if (rigs is null || rigs.Count == 0) return entries;
        var result = new List<AutoEqIndexEntry>(entries.Count);
        foreach (var e in entries)
        {
            if (string.IsNullOrEmpty(e.Rig) && rigs.TryGetValue(e.Source, out var bySource)
                && (bySource.TryGetValue(Key(e.Name, Form(e)), out var rig) || bySource.TryGetValue(e.Name, out rig)))
                result.Add(e with { Rig = rig });
            else
                result.Add(e);
        }
        return result;
    }

    static string Key(string name, string form) => name + "|" + form;

    /// <summary>Form of an entry without a rig: its path is "source/form/name" (e.g. "over-ear").</summary>
    static string Form(AutoEqIndexEntry e)
    {
        var parts = e.RelativePath.Split('/');
        return parts.Length >= 3 ? Uri.UnescapeDataString(parts[1]) : "";
    }
}
