using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Curvy.Core.Apo;
using Curvy.Core.Model;

namespace Curvy.Core.AutoEq;

public sealed class AutoEqUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Fetches the AutoEQ index and per-headphone ParametricEQ files, caching the index on disk
/// (<c>INDEX.md</c> + <c>index.meta.json</c>). Cache policy: a cache is valid when INDEX.md exists, the meta
/// parses and schemaVersion == 1. A valid cache younger than <see cref="MaxIndexAge"/> is used without network.
/// Otherwise the index is refetched; if that fails and INDEX.md still exists (stale or meta corrupt) it is
/// used as an offline fallback; with no usable cache the failure surfaces as <see cref="AutoEqUnavailableException"/>.
/// </summary>
public sealed class AutoEqClient(HttpClient http, string cacheDir, TimeProvider time)
{
    public const string BaseUrl = "https://raw.githubusercontent.com/jaakkopasanen/AutoEq/master/results/";
    public static readonly TimeSpan MaxIndexAge = TimeSpan.FromDays(7);
    private const int SchemaVersion = 1;

    private static readonly string UserAgentVersion =
        (typeof(AutoEqClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? "0.1").Split('+')[0];

    private string IndexPath => Path.Combine(cacheDir, "INDEX.md");
    private string MetaPath => Path.Combine(cacheDir, "index.meta.json");

    public DateTimeOffset? IndexFetchedAt => ReadMeta();

    public async Task<IReadOnlyList<AutoEqIndexEntry>> GetIndexAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        var fetchedAt = ReadMeta();
        var hasFile = File.Exists(IndexPath);
        if (!forceRefresh && hasFile && fetchedAt is { } at && time.GetUtcNow() - at < MaxIndexAge)
        {
            var cached = AutoEqIndexParser.Parse(File.ReadAllText(IndexPath));
            if (cached.Count > 0) return cached;
        }

        try
        {
            var text = await GetStringAsync(BaseUrl + "INDEX.md", ct).ConfigureAwait(false);
            var entries = AutoEqIndexParser.Parse(text);
            if (entries.Count == 0) throw new AutoEqUnavailableException("AutoEQ index was empty or unreadable.");
            Directory.CreateDirectory(cacheDir);
            AtomicFile.WriteAllText(IndexPath, text);
            AtomicFile.WriteAllText(MetaPath,
                JsonSerializer.Serialize(new Meta(SchemaVersion, time.GetUtcNow())));
            return entries;
        }
        catch (AutoEqUnavailableException) when (hasFile && FallbackEntries() is { Count: > 0 })
        {
            return FallbackEntries();
        }
    }

    public async Task<AutoEqCorrection> FetchCorrectionAsync(AutoEqIndexEntry e, CancellationToken ct = default)
    {
        var text = await GetStringAsync(BaseUrl + e.ParametricEqRelativeUrl, ct).ConfigureAwait(false);
        var filters = ParametricEqParser.Parse(text);
        if (filters.Count == 0) throw new AutoEqUnavailableException("AutoEQ file contains no filters.");
        return new AutoEqCorrection(
            new AutoEqSource(e.Name, e.Source, e.Rig, e.RelativePath, time.GetUtcNow()), filters);
    }

    private IReadOnlyList<AutoEqIndexEntry> FallbackEntries()
    {
        try { return AutoEqIndexParser.Parse(File.ReadAllText(IndexPath)); }
        catch (IOException) { return []; }
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Curvy", UserAgentVersion));
            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new AutoEqUnavailableException($"AutoEQ server returned {(int)resp.StatusCode}.");
            return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AutoEqUnavailableException("AutoEQ is unreachable.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new AutoEqUnavailableException("AutoEQ request timed out.", ex);
        }
    }

    private DateTimeOffset? ReadMeta()
    {
        try
        {
            if (!File.Exists(MetaPath)) return null;
            var m = JsonSerializer.Deserialize<Meta>(File.ReadAllText(MetaPath));
            return m is { SchemaVersion: SchemaVersion, FetchedAt: { } f } ? f : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record Meta(
        [property: System.Text.Json.Serialization.JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: System.Text.Json.Serialization.JsonPropertyName("fetchedAt")] DateTimeOffset? FetchedAt);
}
