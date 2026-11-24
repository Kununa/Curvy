using System.Collections.Immutable;

namespace Curvy.Core.Model;

/// <param name="FirstRunShown">True once the first-run "disable other EQ lines" dialog was shown.</param>
public record CurvySettings(
    int SchemaVersion, Guid? ActiveProfileId, bool MasterEnabled, Preference Universal,
    double ExtraHeadroomDb, bool StartWithWindows, ImmutableList<string> DisabledForeignLines,
    bool FirstRunShown = false)
{
    public static CurvySettings Default { get; } =
        new(1, null, true, Preference.Empty, 0, false, []);
}
