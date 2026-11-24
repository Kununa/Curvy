using System.Text.Json;
using System.Text.Json.Serialization;

namespace Curvy.Core.Storage;

public static class JsonFiles
{
    public const int CurrentSchemaVersion = 1;

    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Renames an unparseable file to <c>&lt;name&gt;.bad</c> (replacing an older one) so the next save
    /// doesn't destroy it. Best effort; only for JSON parse failures, not transient IO errors.
    /// </summary>
    public static void QuarantineCorrupt(string path, Exception cause)
    {
        if (cause is not JsonException) return;
        try { File.Move(path, path + ".bad", overwrite: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

public static class CurvyPaths
{
    /// <summary>Environment variable that overrides <see cref="Root"/> (development and tests).</summary>
    public const string EnvOverride = "CURVY_DATA_DIR";

    /// <summary><c>%CURVY_DATA_DIR%</c> when set, otherwise <c>%LocalAppData%\Curvy</c>.</summary>
    public static string Root =>
        Environment.GetEnvironmentVariable(EnvOverride) is { Length: > 0 } env
            ? env
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Curvy");

    /// <summary>One JSON file per profile.</summary>
    public static string ProfilesDir => Path.Combine(Root, "profiles");
}
