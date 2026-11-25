using System.Collections.Immutable;

namespace Curvy.Core.Model;

/// <param name="FirstRunShown">True once the first-run "disable other EQ lines" dialog was shown.</param>
/// <param name="IntegrationPaused">
/// True after "Restore original config.txt": Curvy leaves config.txt alone (no include, no repair)
/// until the user reconnects.
/// </param>
/// <param name="PreampMode">Auto (clip-safe, computed) or a fixed manual value.</param>
/// <param name="ManualPreampDb">The preamp used when <paramref name="PreampMode"/> is Manual.</param>
public record CurvySettings(
    int SchemaVersion, Guid? ActiveProfileId, bool MasterEnabled, Preference Universal,
    double ExtraHeadroomDb, bool StartWithWindows, ImmutableList<string> DisabledForeignLines,
    bool FirstRunShown = false,
    bool IntegrationPaused = false,
    PreampMode PreampMode = PreampMode.Auto,
    double ManualPreampDb = -6)
{
    public static CurvySettings Default { get; } =
        new(1, null, true, Preference.Empty, 0, false, []);
}
