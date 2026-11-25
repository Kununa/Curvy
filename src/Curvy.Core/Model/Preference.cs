using System.Collections.Immutable;
using Curvy.Core.Filters;

namespace Curvy.Core.Model;

/// <summary>Which half of a <see cref="Preference"/> is applied; the other is kept but ignored.</summary>
public enum PreferenceMode { Simple, Parametric }

/// <param name="Tone">Bass / treble / tilt (applied in <see cref="PreferenceMode.Simple"/>).</param>
/// <param name="Bands">Parametric bands (applied in <see cref="PreferenceMode.Parametric"/>).</param>
/// <param name="Mode">
/// Exclusive: only the selected half produces filters. Null for preferences saved before modes
/// existed (see <see cref="EffectiveMode"/>).
/// </param>
public record Preference(
    ToneControls Tone, ImmutableList<Filter> Bands, bool Enabled = true, PreferenceMode? Mode = null)
{
    /// <summary>
    /// <see cref="Mode"/>, or for older preferences: parametric if any band changes the sound,
    /// otherwise simple.
    /// </summary>
    public PreferenceMode EffectiveMode =>
        Mode ?? (Bands.Any(b => b.GainDb != 0) ? PreferenceMode.Parametric : PreferenceMode.Simple);

    /// <summary>Starting layout for parametric mode: shelves at the ends, peaks in between, all flat.</summary>
    public static ImmutableList<Filter> DefaultBands { get; } =
    [
        new(FilterType.LowShelf, 105, 0, 0.7),
        new(FilterType.Peaking, 250, 0, 1),
        new(FilterType.Peaking, 1000, 0, 1),
        new(FilterType.Peaking, 3000, 0, 1.4),
        new(FilterType.Peaking, 6000, 0, 2),
        new(FilterType.HighShelf, 10000, 0, 0.7),
    ];

    public static Preference Empty { get; } = new(new ToneControls(), DefaultBands, true);

    /// <summary>
    /// Bands at the frequencies of <paramref name="filters"/> (e.g. an AutoEQ correction): same type and Q,
    /// 0 dB, frequency locked, sorted by frequency, so only the gains are left to adjust.
    /// </summary>
    public static ImmutableList<Filter> BandsAt(IEnumerable<Filter> filters) =>
        filters.OrderBy(f => f.FrequencyHz)
            .Select(f => new Filter(f.Type, f.FrequencyHz, 0, f.Q, Enabled: true, FrequencyLocked: true))
            .ToImmutableList();

    /// <summary>The bands the user can edit right now (empty when disabled or in simple mode).</summary>
    public ImmutableList<Filter> ActiveBands =>
        Enabled && EffectiveMode == PreferenceMode.Parametric ? Bands : [];

    /// <summary>
    /// Filters this preference contributes: the tone controls or the bands, depending on
    /// <see cref="Mode"/>. Flat (0 dB) bands are skipped; they don't change the sound.
    /// </summary>
    public IEnumerable<Filter> AllFilters() => !Enabled ? []
        : EffectiveMode == PreferenceMode.Simple ? Tone.ToFilters()
        : Bands.Where(b => b.GainDb != 0);
}
