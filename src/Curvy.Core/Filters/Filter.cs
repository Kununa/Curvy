namespace Curvy.Core.Filters;

public enum FilterType { Peaking, LowShelf, HighShelf }

/// <param name="FrequencyLocked">Editor-only: the band's graph handle moves gain only. Doesn't affect the sound.</param>
public record Filter(
    FilterType Type, double FrequencyHz, double GainDb, double Q, bool Enabled = true, bool FrequencyLocked = false);
