namespace Curvy.Core.Model;

public static class ProfileNames
{
    /// <summary>
    /// <paramref name="baseName"/>, or "<paramref name="baseName"/> 2", " 3", … — the first name no
    /// existing profile uses (case-insensitive).
    /// </summary>
    public static string Unique(IEnumerable<Profile> profiles, string baseName)
    {
        var taken = profiles.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = baseName;
        for (var n = 2; taken.Contains(name); n++)
            name = $"{baseName} {n}";
        return name;
    }
}
