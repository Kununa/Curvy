using System.Collections.Immutable;
using Curvy.Core.Filters;
using Curvy.Core.Model;

namespace Curvy.Core.Chain;

public enum ChainLayer { AutoEq, ProfilePreference, Universal }

public static class EqChain
{
    public static ChainResult Build(Profile? active, CurvySettings s)
    {
        if (!s.MasterEnabled) return new ChainResult(true, 0, []);
        var filters = Enum.GetValues<ChainLayer>()
            .SelectMany(l => Layer(active, s, l))
            .ToImmutableList();
        return new ChainResult(false, ComputePreamp(filters, s.ExtraHeadroomDb), filters);
    }

    public static IEnumerable<Filter> Layer(Profile? active, CurvySettings s, ChainLayer layer) => layer switch
    {
        ChainLayer.AutoEq => active is { AutoEqEnabled: true, AutoEq: { } ae } ? ae.Filters : [],
        ChainLayer.ProfilePreference => active is null ? [] : active.Preference.AllFilters(),
        ChainLayer.Universal => active is { IgnoreUniversal: true } ? [] : s.Universal.AllFilters(),
        _ => [],
    };

    public static double ComputePreamp(IReadOnlyList<Filter> filters, double extraHeadroomDb)
    {
        var enabled = filters.Where(f => f.Enabled).ToList();
        var freqs = FrequencyGrid.LogSpaced(1024).Concat(enabled.Select(f => f.FrequencyHz)).ToList();
        var max = enabled.Count == 0 ? 0 : Response.Evaluate(enabled, freqs).Max();
        var pre = max <= 0 ? 0 : Math.Floor(-max * 10) / 10;
        return pre - extraHeadroomDb;
    }
}
