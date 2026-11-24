using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Curvy.Core.Filters;

namespace Curvy.Core.Apo;

public static partial class ParametricEqParser
{
    [GeneratedRegex(@"^\s*Filter\s*\d*\s*:\s*(ON|OFF)\s+(\w+)\s+Fc\s+(\S+)\s*Hz\s+Gain\s+(\S+)\s*dB\s+Q\s+(\S+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FilterLine();

    public static ImmutableList<Filter> Parse(string text)
    {
        var result = ImmutableList.CreateBuilder<Filter>();
        foreach (var line in (text ?? "").Split('\n'))
        {
            var m = FilterLine().Match(line.Trim());
            if (!m.Success) continue;
            FilterType type;
            switch (m.Groups[2].Value.ToUpperInvariant())
            {
                case "PK": case "PEQ": type = FilterType.Peaking; break;
                case "LSC": case "LS": type = FilterType.LowShelf; break;
                case "HSC": case "HS": type = FilterType.HighShelf; break;
                default: continue;
            }
            if (!TryNum(m.Groups[3].Value, out var fc) || !TryNum(m.Groups[4].Value, out var gain)
                || !TryNum(m.Groups[5].Value, out var q)) continue;
            var enabled = m.Groups[1].Value.Equals("ON", StringComparison.OrdinalIgnoreCase);
            result.Add(new Filter(type, fc, gain, q, enabled));
        }
        return result.ToImmutable();
    }

    private static bool TryNum(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && double.IsFinite(v);
}
