using System.Text.Json.Serialization;

namespace Curvy.Core.AutoEq;

/// <summary><c>index.meta.json</c>: when the cached AutoEQ index was downloaded.</summary>
internal sealed record AutoEqCacheMeta(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("fetchedAt")] DateTimeOffset? FetchedAt);

/// <summary>
/// Source-generated JSON for the AutoEQ cache files (<c>index.meta.json</c>, <c>rigs.json</c>), with
/// default options so the files stay as the reflection-based serializer wrote them.
/// </summary>
[JsonSerializable(typeof(AutoEqCacheMeta))]
[JsonSerializable(typeof(AutoEqRigs.Map))]
internal sealed partial class AutoEqJsonContext : JsonSerializerContext;
