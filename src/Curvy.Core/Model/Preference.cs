using System.Collections.Immutable;
using Curvy.Core.Filters;

namespace Curvy.Core.Model;

public record Preference(ToneControls Tone, ImmutableList<Filter> Bands, bool Enabled = true)
{
    public static Preference Empty { get; } = new(new ToneControls(), [], true);

    public IEnumerable<Filter> AllFilters() =>
        Enabled ? Tone.ToFilters().Concat(Bands) : [];
}
