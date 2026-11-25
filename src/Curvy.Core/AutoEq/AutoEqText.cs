namespace Curvy.Core.AutoEq;

/// <summary>Display text for AutoEQ results. Some sources (e.g. oratory1990) list no measurement rig.</summary>
public static class AutoEqText
{
    /// <summary>Joins the non-empty parts with " · " (so a missing rig leaves no dangling separator).</summary>
    public static string Join(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
}
