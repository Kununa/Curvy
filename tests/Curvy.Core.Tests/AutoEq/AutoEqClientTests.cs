using System.Net;
using System.Text.Json;
using Curvy.Core.AutoEq;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Curvy.Core.Tests.AutoEq;

public sealed class AutoEqClientTests : IDisposable
{
    private const string IndexMd = """
# Index
- [Beyerdynamic DT 880](./oratory1990/over-ear/Beyerdynamic%20DT%20880) by oratory1990
- [Sennheiser HD 600](./oratory1990/over-ear/Sennheiser%20HD%20600) by oratory1990
""";

    private const string PeqTxt = "Preamp: -6.4 dB\nFilter 1: ON LSC Fc 105 Hz Gain 7.4 dB Q 0.70\nFilter 2: ON PK Fc 5760 Hz Gain -6.1 dB Q 4.97\n";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "curvy-autoeq-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero));

    public AutoEqClientTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Requests.Add(r);
            return Task.FromResult(f(r));
        }
    }

    private AutoEqClient Client(FakeHandler h) => new(new HttpClient(h), _dir, _time);

    private static HttpResponseMessage Ok(string s) => new(HttpStatusCode.OK) { Content = new StringContent(s) };

    private void SeedCache(DateTimeOffset fetchedAt, string? metaOverride = null)
    {
        File.WriteAllText(Path.Combine(_dir, "INDEX.md"), IndexMd);
        File.WriteAllText(Path.Combine(_dir, "index.meta.json"),
            metaOverride ?? JsonSerializer.Serialize(new { schemaVersion = 1, fetchedAt }));
    }

    [Fact]
    public async Task GetIndex_UsesFreshCache_WithoutNetwork()
    {
        SeedCache(_time.GetUtcNow().AddDays(-1));
        var h = new FakeHandler(_ => throw new InvalidOperationException("network"));
        var list = await Client(h).GetIndexAsync();
        Assert.Equal(2, list.Count);
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task GetIndex_RefreshesStaleCache()
    {
        SeedCache(_time.GetUtcNow().AddDays(-8));
        var h = new FakeHandler(_ => Ok(IndexMd + "\n- [Extra](./x/over-ear/Extra) by x\n"));
        var c = Client(h);
        var list = await c.GetIndexAsync();
        Assert.Equal(3, list.Count);
        Assert.Single(h.Requests);
        Assert.Equal(AutoEqClient.BaseUrl + "INDEX.md", h.Requests[0].RequestUri!.ToString());
        Assert.Equal(_time.GetUtcNow(), c.IndexFetchedAt);
    }

    [Fact]
    public async Task GetIndex_FallsBackToStaleCache_WhenOffline()
    {
        SeedCache(_time.GetUtcNow().AddDays(-8));
        var h = new FakeHandler(_ => throw new HttpRequestException("offline"));
        var list = await Client(h).GetIndexAsync();
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task GetIndex_Throws_AutoEqUnavailable_WhenOfflineAndNoCache()
    {
        var h = new FakeHandler(_ => throw new HttpRequestException("offline"));
        await Assert.ThrowsAsync<AutoEqUnavailableException>(() => Client(h).GetIndexAsync());
    }

    [Fact]
    public async Task GetIndex_Refetches_WhenCacheCorrupt()
    {
        SeedCache(_time.GetUtcNow(), "{ not json");
        var h = new FakeHandler(_ => Ok(IndexMd));
        var list = await Client(h).GetIndexAsync();
        Assert.Equal(2, list.Count);
        Assert.Single(h.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData((HttpStatusCode)429)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetIndex_OnErrorStatus_TreatedAsOffline(HttpStatusCode code)
    {
        var h = new FakeHandler(_ => new HttpResponseMessage(code));
        await Assert.ThrowsAsync<AutoEqUnavailableException>(() => Client(h).GetIndexAsync());
        SeedCache(_time.GetUtcNow().AddDays(-9));
        Assert.Equal(2, (await Client(h).GetIndexAsync()).Count);
    }

    [Fact]
    public async Task GetIndex_GarbageDownload_DoesNotOverwriteCache()
    {
        SeedCache(_time.GetUtcNow().AddDays(-9));
        var h = new FakeHandler(_ => Ok("<html>nope</html>"));
        var list = await Client(h).GetIndexAsync();
        Assert.Equal(2, list.Count);
        Assert.Equal(IndexMd, File.ReadAllText(Path.Combine(_dir, "INDEX.md")));
    }

    [Fact]
    public async Task GetIndex_CallerCancellation_Propagates()
    {
        var h = new FakeHandler(_ => throw new TaskCanceledException());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(h).GetIndexAsync(ct: cts.Token));
    }

    [Fact]
    public async Task FetchCorrection_ParsesFilters_AndFillsProvenance()
    {
        var h = new FakeHandler(_ => Ok(PeqTxt));
        var e = AutoEqIndexParser.Parse(IndexMd)[0];
        var corr = await Client(h).FetchCorrectionAsync(e);
        Assert.Equal(2, corr.Filters.Count);
        Assert.Equal("Beyerdynamic DT 880", corr.Source.Headphone);
        Assert.Equal(e.RelativePath, corr.Source.RelativePath);
        Assert.Equal(_time.GetUtcNow(), corr.Source.FetchedAt);
        Assert.Equal(AutoEqClient.BaseUrl + e.ParametricEqRelativeUrl, h.Requests[0].RequestUri!.OriginalString);
    }

    [Fact]
    public async Task FetchCorrection_404_And_EmptyFile_Throw()
    {
        var e = AutoEqIndexParser.Parse(IndexMd)[0];
        var h404 = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await Assert.ThrowsAsync<AutoEqUnavailableException>(() => Client(h404).FetchCorrectionAsync(e));
        var hEmpty = new FakeHandler(_ => Ok("nothing here"));
        await Assert.ThrowsAsync<AutoEqUnavailableException>(() => Client(hEmpty).FetchCorrectionAsync(e));
    }

    [Fact]
    public async Task Requests_SendCurvyUserAgent()
    {
        var h = new FakeHandler(r => r.RequestUri!.AbsolutePath.EndsWith("INDEX.md") ? Ok(IndexMd) : Ok(PeqTxt));
        var c = Client(h);
        var list = await c.GetIndexAsync();
        await c.FetchCorrectionAsync(list[0]);
        Assert.Equal(2, h.Requests.Count);
        Assert.All(h.Requests, r => Assert.StartsWith("Curvy/", r.Headers.UserAgent.ToString()));
    }
}
