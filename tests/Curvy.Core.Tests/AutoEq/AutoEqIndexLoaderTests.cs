using System.Collections.Immutable;
using Curvy.Core.AutoEq;
using Curvy.Core.Model;
using System.Threading.Tasks;

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

    [Test]
    public async Task Concurrent_plain_loads_share_one_call()
    {
        var fake = new FakeLoad();
        var loader = new AutoEqIndexLoader(fake.Invoke);

        var t1 = loader.LoadAsync();
        var t2 = loader.LoadAsync();
        await Assert.That(ReferenceEquals(t1, t2)).IsTrue();
        await fake.WaitForCalls(1);
        await Assert.That(loader.IsLoading).IsTrue();
        fake.Calls[0].Tcs.SetResult(IndexA);

        await Assert.That(await t1).IsSameReferenceAs(IndexA);
        await Assert.That(loader.Current).IsSameReferenceAs(IndexA);
        await Assert.That(loader.IsLoading).IsFalse();
        await Assert.That(fake.Calls).HasSingleItem();
    }

    [Test]
    public async Task Plain_load_after_success_uses_memory()
    {
        var fake = new FakeLoad();
        var loader = new AutoEqIndexLoader(fake.Invoke);
        var t = loader.LoadAsync();
        await fake.WaitForCalls(1);
        fake.Calls[0].Tcs.SetResult(IndexA);
        await t;

        await Assert.That(await loader.LoadAsync()).IsSameReferenceAs(IndexA);
        await Assert.That(fake.Calls).HasSingleItem();
    }

    [Test]
    public async Task Forced_refresh_during_plain_load_runs_after_it_never_concurrently()
    {
        var fake = new FakeLoad();
        var loader = new AutoEqIndexLoader(fake.Invoke);

        var plain = loader.LoadAsync();
        var forced = loader.LoadAsync(forceRefresh: true);
        await Assert.That(ReferenceEquals(plain, forced)).IsFalse();
        await Assert.That(ReferenceEquals(forced, loader.LoadAsync(forceRefresh: true))).IsTrue(); // second refresh joins
        await fake.WaitForCalls(1);
        await Assert.That(fake.Calls).HasSingleItem();

        fake.Calls[0].Tcs.SetResult(IndexA);
        await Assert.That(await plain).IsSameReferenceAs(IndexA);
        await fake.WaitForCalls(2);
        await Assert.That(fake.Calls[1].Force).IsTrue();
        fake.Calls[1].Tcs.SetResult(IndexB);

        await Assert.That(await forced).IsSameReferenceAs(IndexB);
        await Assert.That(loader.Current).IsSameReferenceAs(IndexB);
        await Assert.That(fake.MaxConcurrent).IsEqualTo(1);
    }

    [Test]
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
        await Assert.That(loader.Current).IsSameReferenceAs(IndexA);

        var retry = loader.LoadAsync(forceRefresh: true);
        await fake.WaitForCalls(3);
        fake.Calls[2].Tcs.SetResult(IndexB);
        await Assert.That(await retry).IsSameReferenceAs(IndexB);
    }

    private static AutoEqCorrection Correction(AutoEqIndexEntry e) =>
        new(new AutoEqSource(e.Name, e.Source, e.Rig, e.RelativePath, DateTimeOffset.UnixEpoch), ImmutableList<Curvy.Core.Filters.Filter>.Empty);

    [Test]
    public async Task Correction_cache_fetches_each_entry_once()
    {
        var calls = 0;
        var cache = new AutoEqCorrectionCache((e, _) => { calls++; return Task.FromResult(Correction(e)); });
        var entry = IndexA[0];

        await Assert.That(cache.TryGet(entry, out _)).IsFalse();
        var a = await cache.GetAsync(entry);
        var b = await cache.GetAsync(entry with { }); // value-equal entry hits the cache
        await Assert.That(b).IsSameReferenceAs(a);
        await Assert.That(cache.TryGet(entry, out var hit)).IsTrue();
        await Assert.That(hit).IsSameReferenceAs(a);
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
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

        await Assert.That(async () => await cache.GetAsync(entry, new CancellationToken(true))).Throws<OperationCanceledException>();
        await Assert.ThrowsAsync<AutoEqUnavailableException>(() => cache.GetAsync(entry));
        await Assert.That(cache.TryGet(entry, out _)).IsFalse();
        await cache.GetAsync(entry);
        await Assert.That(cache.TryGet(entry, out _)).IsTrue();
        await Assert.That(calls).IsEqualTo(3);
    }
}