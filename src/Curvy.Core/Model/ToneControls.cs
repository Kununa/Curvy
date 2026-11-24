using Curvy.Core.Filters;

namespace Curvy.Core.Model;

public record ToneControls(
    double BassDb = 0, double BassHz = 105, double BassQ = 0.7,
    double TrebleDb = 0, double TrebleHz = 10000, double TrebleQ = 0.7,
    double TiltDb = 0, double TiltPivotHz = 1000)
{
    public IEnumerable<Filter> ToFilters()
    {
        if (BassDb != 0) yield return new Filter(FilterType.LowShelf, BassHz, BassDb, BassQ);
        if (TrebleDb != 0) yield return new Filter(FilterType.HighShelf, TrebleHz, TrebleDb, TrebleQ);
        if (TiltDb != 0)
        {
            yield return new Filter(FilterType.LowShelf, TiltPivotHz, -TiltDb / 2, 0.5);
            yield return new Filter(FilterType.HighShelf, TiltPivotHz, TiltDb / 2, 0.5);
        }
    }
}
