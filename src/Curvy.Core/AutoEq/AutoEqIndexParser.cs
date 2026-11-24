using System.Text.RegularExpressions;

namespace Curvy.Core.AutoEq;

public sealed record AutoEqIndexEntry(string Name, string Source, string Rig, string RelativePath)
{
    public string ParametricEqRelativeUrl
    {
        get
        {
            var slash = RelativePath.LastIndexOf('/');
            var last = Uri.UnescapeDataString(slash >= 0 ? RelativePath[(slash + 1)..] : RelativePath);
            return RelativePath + "/" + Uri.EscapeDataString(last + " ParametricEQ.txt");
        }
    }
}

public static partial class AutoEqIndexParser
{
    [GeneratedRegex(@"^- \[(?<name>.+)\]\(\./(?<path>\S+)\) by (?<rest>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex LineRegex();

    public static IReadOnlyList<AutoEqIndexEntry> Parse(string markdown)
    {
        var list = new List<AutoEqIndexEntry>();
        if (string.IsNullOrEmpty(markdown)) return list;
        var rx = LineRegex();
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (!line.StartsWith("- [", StringComparison.Ordinal)) continue;
            var m = rx.Match(line);
            if (!m.Success) continue;
            try
            {
                var path = m.Groups["path"].Value;
                var firstSeg = path.Split('/')[0];
                var source = Uri.UnescapeDataString(firstSeg);
                var rest = m.Groups["rest"].Value;
                var i = rest.IndexOf(" on ", StringComparison.Ordinal);
                var rig = i >= 0 ? rest[(i + 4)..].Trim() : "";
                list.Add(new AutoEqIndexEntry(m.Groups["name"].Value, source, rig, path));
            }
            catch (UriFormatException) { }
        }
        return list;
    }
}
