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
