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
        var preamp = s.PreampMode == PreampMode.Manual
            ? s.ManualPreampDb
            : ComputePreamp(filters, s.ExtraHeadroomDb);
        return new ChainResult(false, preamp, filters);
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
        var max = PeakGainDb(filters);
        var pre = max <= 0 ? 0 : Math.Floor(-max * 10) / 10;
        return pre - extraHeadroomDb;
    }

    /// <summary>
    /// Highest gain of the combined curve over 20 Hz–20 kHz (a 1024-point grid plus every enabled
    /// filter's centre frequency, so narrow peaks are not missed). 0 when there are no filters.
    /// </summary>
    public static double PeakGainDb(IReadOnlyList<Filter> filters)
    {
        var enabled = filters.Where(f => f.Enabled).ToList();
        if (enabled.Count == 0) return 0;
        var freqs = FrequencyGrid.LogSpaced(1024).Concat(enabled.Select(f => f.FrequencyHz)).ToList();
        return Response.Evaluate(enabled, freqs).Max();
    }
}
