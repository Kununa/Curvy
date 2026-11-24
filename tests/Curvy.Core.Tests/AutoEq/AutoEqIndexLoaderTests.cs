using System.Collections.Immutable;
using Curvy.Core.AutoEq;
using Curvy.Core.Model;
using Xunit;

namespace Curvy.Core.Tests.AutoEq;

public sealed class AutoEqIndexLoaderTests
{
    private static readonly IReadOnlyList<AutoEqIndexEntry> IndexA =
        [new("Beyerdynamic DT 880", "oratory1990", "over-ear", "oratory1990/over-ear/Beyerdynamic DT 880")];
    private static readonly IReadOnlyList<AutoEqIndexEntry> IndexB =
        [new("Sennheiser HD 600", "oratory1990", "over-ear", "oratory1990/over-ear/Sennheiser HD 600")];

    private sealed class FakeLoad
    {
        public readonly List<(bool Force, TaskCompletionSource<IReadOnlyList<AutoEqIndexEntry>> Tcs)> Calls = [];
        public int Running;
        public int MaxConcurrent;

        public Task<IReadOnlyList<AutoEqIndexEntry>> Invoke(bool force, CancellationToken _)
        {
            var tcs = new TaskCompletionSource<IReadOnlyList<AutoEqIndexEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Calls) Calls.Add((force, tcs));
            var now = Interlocked.Increment(ref Running);
            MaxConcurrent = Math.Max(MaxConcurrent, now);
            return Wrap(tcs.Task);
        }

        private async Task<IReadOnlyList<AutoEqIndexEntry>> Wrap(Task<IReadOnlyList<AutoEqIndexEntry>> t)
        {
            try { return await t; }
            finally { Interlocked.Decrement(ref Running); }
        }

        public async Task WaitForCalls(int n)
        {
            for (var i = 0; i < 200; i++)
            {
                lock (Calls) if (Calls.Count >= n) return;
                await Task.Delay(5);
            }
            throw new TimeoutException($"expected {n} loader calls");
        }
    }

    [Fact]
    public async Task Concurrent_plain_loads_share_one_call()
    {
        var fake = new FakeLoad();
        var loader = new AutoEqIndexLoader(fake.Invoke);

        var t1 = loader.LoadAsync();
        var t2 = loader.LoadAsync();
        Assert.Same(t1, t2);
        await fake.WaitForCalls(1);
        Assert.True(loader.IsLoading);
        fake.Calls[0].Tcs.SetResult(IndexA);

        Assert.Same(IndexA, await t1);
        Assert.Same(IndexA, loader.Current);
        Assert.False(loader.IsLoading);
        Assert.Single(fake.Calls);
    }

    [Fact]
    public async Task Plain_load_after_success_uses_memory()
    {
        var fake = new FakeLoad();
        var loader = new AutoEqIndexLoader(fake.Invoke);
        var t = loader.LoadAsync();
        await fake.WaitForCalls(1);
        fake.Calls[0].Tcs.SetResult(IndexA);
        await t;

        Assert.Same(IndexA, await loader.LoadAsync());
        Assert.Single(fake.Calls);
    }

    [Fact]
    public async Task Forced_refresh_during_plain_load_runs_after_it_never_concurrently()
    {
        var fake = new FakeLoad();
        var loader = new AutoEqIndexLoader(fake.Invoke);

        var plain = loader.LoadAsync();
        var forced = loader.LoadAsync(forceRefresh: true);
        Assert.NotSame(plain, forced);
        Assert.Same(forced, loader.LoadAsync(forceRefresh: true)); // second refresh joins
        await fake.WaitForCalls(1);
        Assert.Single(fake.Calls);

        fake.Calls[0].Tcs.SetResult(IndexA);
        Assert.Same(IndexA, await plain);
        await fake.WaitForCalls(2);
        Assert.True(fake.Calls[1].Force);
        fake.Calls[1].Tcs.SetResult(IndexB);

        Assert.Same(IndexB, await forced);
        Assert.Same(IndexB, loader.Current);
        Assert.Equal(1, fake.MaxConcurrent);
    }

    [Fact]
    public async Task Failure_propagates_keeps_previous_index_and_allows_retry()
    {
        var fake = new FakeLoad();
        var loader = new AutoEqIndexLoader(fake.Invoke);
        var first = loader.LoadAsync();
        await fake.WaitForCalls(1);
        fake.Calls[0].Tcs.SetResult(IndexA);
        await first;

        var refresh = loader.LoadAsync(forceRefresh: true);
        await fake.WaitForCalls(2);
        fake.Calls[1].Tcs.SetException(new AutoEqUnavailableException("offline"));
        await Assert.ThrowsAsync<AutoEqUnavailableException>(() => refresh);
        Assert.Same(IndexA, loader.Current);

        var retry = loader.LoadAsync(forceRefresh: true);
        await fake.WaitForCalls(3);
        fake.Calls[2].Tcs.SetResult(IndexB);
        Assert.Same(IndexB, await retry);
    }

    private static AutoEqCorrection Correction(AutoEqIndexEntry e) =>
        new(new AutoEqSource(e.Name, e.Source, e.Rig, e.RelativePath, DateTimeOffset.UnixEpoch), ImmutableList<Curvy.Core.Filters.Filter>.Empty);

    [Fact]
    public async Task Correction_cache_fetches_each_entry_once()
    {
        var calls = 0;
        var cache = new AutoEqCorrectionCache((e, _) => { calls++; return Task.FromResult(Correction(e)); });
        var entry = IndexA[0];

        Assert.False(cache.TryGet(entry, out _));
        var a = await cache.GetAsync(entry);
        var b = await cache.GetAsync(entry with { }); // value-equal entry hits the cache
        Assert.Same(a, b);
        Assert.True(cache.TryGet(entry, out var hit));
        Assert.Same(a, hit);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Correction_cache_does_not_cache_failures_or_cancellation()
    {
        var calls = 0;
        var cache = new AutoEqCorrectionCache((e, ct) =>
        {
            calls++;
            ct.ThrowIfCancellationRequested();
            return calls == 2
                ? Task.FromException<AutoEqCorrection>(new AutoEqUnavailableException("offline"))
                : Task.FromResult(Correction(e));
        });
        var entry = IndexA[0];

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetAsync(entry, new CancellationToken(true)));
        await Assert.ThrowsAsync<AutoEqUnavailableException>(() => cache.GetAsync(entry));
        Assert.False(cache.TryGet(entry, out _));
        await cache.GetAsync(entry);
        Assert.True(cache.TryGet(entry, out _));
        Assert.Equal(3, calls);
    }
}
