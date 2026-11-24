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
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Curvy");
}
