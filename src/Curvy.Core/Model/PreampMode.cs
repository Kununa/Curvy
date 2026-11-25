namespace Curvy.Core.Model;

/// <summary>How the preamp in curvy.txt is chosen.</summary>
public enum PreampMode
{
    /// <summary>Computed from the combined curve so it never clips (minus extra headroom).</summary>
    Auto,
    /// <summary>A fixed value the user picked (−6, −12 or custom). May clip.</summary>
    Manual,
}
