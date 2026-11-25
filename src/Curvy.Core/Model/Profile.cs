using System.Collections.Immutable;
using Curvy.Core.Filters;

namespace Curvy.Core.Model;

public record AutoEqSource(string Headphone, string Source, string Rig, string RelativePath, DateTimeOffset FetchedAt);

public record AutoEqCorrection(AutoEqSource Source, ImmutableList<Filter> Filters);

public record Profile(
    Guid Id, string Name, AutoEqCorrection? AutoEq, bool AutoEqEnabled,
    Preference Preference, bool IgnoreUniversal, DateTimeOffset Created, DateTimeOffset Modified)
{
    public static Profile New(string name, AutoEqCorrection? autoEq = null)
    {
        var now = DateTimeOffset.UtcNow;
        // The profile preference starts off; the AutoEQ correction (if any) is what a new profile is for.
        return new Profile(Guid.NewGuid(), name, autoEq, true, Preference.Empty with { Enabled = false }, false, now, now);
    }
}
