namespace Curvy.Core.Filters;

public enum FilterType { Peaking, LowShelf, HighShelf }

public record Filter(FilterType Type, double FrequencyHz, double GainDb, double Q, bool Enabled = true);
