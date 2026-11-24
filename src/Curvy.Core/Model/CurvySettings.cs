using System.Collections.Immutable;

namespace Curvy.Core.Model;

public record CurvySettings(
    int SchemaVersion, Guid? ActiveProfileId, bool MasterEnabled, Preference Universal,
    double ExtraHeadroomDb, bool StartWithWindows, ImmutableList<string> DisabledForeignLines)
{
    public static CurvySettings Default { get; } =
        new(1, null, true, Preference.Empty, 0, false, []);
}
