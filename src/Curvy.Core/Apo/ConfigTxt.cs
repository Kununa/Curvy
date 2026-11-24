namespace Curvy.Core.Apo;

public enum ConfigLineKind { Blank, Comment, CurvyInclude, CurvyDisabled, Active }

public record ConfigLine(int Index, string Text, ConfigLineKind Kind)
{
    private static readonly string[] EqPrefixes = ["Include:", "Preamp:", "Filter", "GraphicEQ:", "Convolution:"];

    public bool IsEqLike =>
        Kind == ConfigLineKind.Active &&
        EqPrefixes.Any(p => Text.TrimStart().StartsWith(p, StringComparison.OrdinalIgnoreCase));
}

public static class ConfigTxt
{
    public const string CurvyIncludeLine = @"Include: Curvy\curvy.txt";
    public const string DisabledPrefix = "# [Curvy] ";

    public static IReadOnlyList<ConfigLine> Classify(IReadOnlyList<string> lines)
    {
        var result = new List<ConfigLine>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
            result.Add(new ConfigLine(i, lines[i], KindOf(lines[i])));
        return result;
    }

    private static ConfigLineKind KindOf(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ConfigLineKind.Blank;
        if (text.StartsWith(DisabledPrefix, StringComparison.Ordinal)) return ConfigLineKind.CurvyDisabled;
        if (text.TrimStart().StartsWith('#')) return ConfigLineKind.Comment;
        return IsCurvyInclude(text) ? ConfigLineKind.CurvyInclude : ConfigLineKind.Active;
    }

    private static bool IsCurvyInclude(string text)
    {
        var norm = text.Trim().Replace('/', '\\');
        const string prefix = "Include:";
        if (!norm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var target = norm[prefix.Length..].Trim();
        var expected = CurvyIncludeLine[prefix.Length..].Trim();
        return string.Equals(target, expected, StringComparison.OrdinalIgnoreCase);
    }
}
