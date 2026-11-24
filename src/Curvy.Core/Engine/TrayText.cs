namespace Curvy.Core.Engine;

public static class TrayText
{
    /// <summary>Win32 tray tooltips hold at most 127 characters (128 with the terminator).</summary>
    public const int MaxTooltipLength = 127;

    /// <summary>
    /// <c>Curvy – &lt;Disconnected | EQ off | active name | No profile&gt;</c>, truncated to
    /// <see cref="MaxTooltipLength"/> characters with an ellipsis, never splitting a surrogate pair.
    /// </summary>
    public static string Tooltip(string? activeProfileName, bool masterEnabled, bool disconnected)
    {
        var state = disconnected ? "Disconnected"
            : !masterEnabled ? "EQ off"
            : activeProfileName ?? "No profile";
        var text = "Curvy – " + state;
        if (text.Length <= MaxTooltipLength) return text;
        var cut = MaxTooltipLength - 1; // room for the ellipsis
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut] + "…";
    }
}
