using System.Text.Json.Serialization;
using Curvy.Core.Model;

namespace Curvy.Core.Storage;

/// <summary>On-disk wrapper of one profile file (<c>profiles/&lt;id&gt;.json</c>).</summary>
internal sealed record ProfileFile(int SchemaVersion, Profile? Profile);

/// <summary>On-disk wrapper of <c>settings.json</c>.</summary>
internal sealed record SettingsFile(int SchemaVersion, CurvySettings? Settings);

/// <summary>
/// Source-generated JSON for profiles and settings: no runtime reflection, so it survives trimming.
/// The options reproduce the original reflection-based format exactly (camelCase names, enums as
/// strings, indented), so existing files keep loading.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ProfileFile))]
[JsonSerializable(typeof(SettingsFile))]
internal sealed partial class StorageJsonContext : JsonSerializerContext;
