using System.Globalization;
using System.Text;
using Curvy.Core.Chain;
using Curvy.Core.Filters;

namespace Curvy.Core.Apo;

public static class ApoFormatter
{
    public const string Header = "# Written by Curvy. Hand edits will be overwritten.";

    public static string Format(ChainResult r)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(Header).Append("\r\n");
        if (r.Bypassed)
        {
            sb.Append("# EQ off\r\n");
            return sb.ToString();
        }
        sb.Append("Preamp: ").Append(r.PreampDb.ToString("0.0#", ci)).Append(" dB\r\n");
        var n = 0;
        foreach (var f in r.Filters)
        {
            if (!f.Enabled) continue;
            var t = f.Type switch
            {
                FilterType.Peaking => "PK",
                FilterType.LowShelf => "LSC",
                FilterType.HighShelf => "HSC",
                _ => throw new ArgumentOutOfRangeException(nameof(r)),
            };
            sb.Append("Filter ").Append((++n).ToString(ci)).Append(": ON ").Append(t)
              .Append(" Fc ").Append(f.FrequencyHz.ToString("0.##", ci))
              .Append(" Hz Gain ").Append(f.GainDb.ToString("0.0#", ci))
              .Append(" dB Q ").Append(f.Q.ToString("0.00#", ci)).Append("\r\n");
        }
        return sb.ToString();
    }
}
