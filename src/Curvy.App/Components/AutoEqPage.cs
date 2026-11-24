using System.Collections.Immutable;
using System.Diagnostics;
using Curvy.App.Services;
using Curvy.Core.AutoEq;
using Curvy.Core.Chain;
using Curvy.Core.Filters;
using Curvy.Core.Model;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Curvy.App.Components;

/// <param name="InitialQuery">Pre-fills the search box (e.g. the headphone from "Change source").</param>
/// <param name="ReplaceForProfileId">
/// When set, the primary action swaps that profile's AutoEQ correction (keeping its preference)
/// instead of creating a new profile.
/// </param>
/// <param name="State">Current engine snapshot (profiles + settings for the preview's Total curve).</param>
/// <param name="OnOpenProfile">Navigates to the Profiles page with the given profile selected.</param>
public record AutoEqPageProps(
    string? InitialQuery,
    Guid? ReplaceForProfileId,
    EngineState State,
    Action<Guid> OnOpenProfile);

/// <summary>
/// AutoEQ browser: search the (cached) AutoEQ index, pick a measurement source per headphone,
/// preview its correction on a graph, and create a profile from it (or swap it into an existing
/// profile). All network work is async, cancelled when the page unmounts, and never blocks the UI.
/// </summary>
public sealed class AutoEqPage : Component<AutoEqPageProps>
{
    const string UnavailableText = "Can't reach AutoEQ. Check your connection.";

    sealed record IndexState(
        IReadOnlyList<AutoEqIndexEntry>? Index, bool Loading, string? Error, DateTimeOffset? FetchedAt, string? StaleWarning = null);

    /// <summary>
    /// Shown when a forced refresh fell back to the cached index (the client serves the stale
    /// cache instead of failing; detected by <see cref="AutoEqClient.IndexFetchedAt"/> not changing).
    /// </summary>
    public static string StaleRefreshMessage(DateTimeOffset? fetchedAt) => fetchedAt is { } at
        ? $"Couldn't refresh; showing the index from {at.LocalDateTime:d}."
        : "Couldn't refresh; showing the cached index.";

    sealed record PreviewState(AutoEqIndexEntry Entry, AutoEqCorrection? Correction);

    public override Element Render()
    {
        var host = EngineHost.Instance;
        var props = Props;
        var state = props.State;

        var replaceTarget = props.ReplaceForProfileId is { } rid
            ? state.Profiles.FirstOrDefault(p => p.Id == rid)
            : null;

        var (query, setQuery) = UseState(props.InitialQuery ?? "");
        var (searchQuery, setSearchQuery) = UseState(props.InitialQuery ?? "");
        // Initial index state computed once (IndexFetchedAt reads a file): reuse the in-memory index if any.
        var initialIndex = UseRef<IndexState?>(null);
        initialIndex.Current ??= host.AutoEqIndex.Current is { } mem
            ? new IndexState(mem, false, null, host.AutoEq.IndexFetchedAt)
            : new IndexState(null, true, null, null);
        var (index, updateIndex) = UseReducer(initialIndex.Current);
        var (sourceChoice, updateSourceChoice) = UseReducer(ImmutableDictionary<string, int>.Empty);
        var (preview, updatePreview) = UseReducer<PreviewState?>(null);
        var (fetchError, setFetchError) = UseState<string?>(null);
        var (busyEntry, updateBusyEntry) = UseReducer<AutoEqIndexEntry?>(null);
        var life = UseRef<CancellationTokenSource?>(null); // created on mount, cancelled on unmount
        var previewWanted = UseRef<AutoEqIndexEntry?>(null);

        CancellationToken Token() => life.Current?.Token ?? CancellationToken.None;

        // ── Index loading ────────────────────────────────────────────────
        async Task LoadIndexAsync(bool forceRefresh, CancellationToken ct)
        {
            if (forceRefresh || host.AutoEqIndex.Current is null)
                updateIndex(s => s with { Loading = true, Error = null, StaleWarning = null });
            var before = forceRefresh ? host.AutoEq.IndexFetchedAt : null;
            try
            {
                var loaded = await host.AutoEqIndex.LoadAsync(forceRefresh).WaitAsync(ct);
                host.Post(() =>
                {
                    if (forceRefresh) host.AutoEqCorrections.Clear();
                    if (ct.IsCancellationRequested) return;
                    var fetchedAt = host.AutoEq.IndexFetchedAt;
                    var stale = forceRefresh && fetchedAt == before ? StaleRefreshMessage(fetchedAt) : null;
                    updateIndex(_ => new IndexState(loaded, false, null, fetchedAt, stale));
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Page went away; the shared load keeps running and fills the cache.
            }
            catch (AutoEqUnavailableException ex)
            {
                host.Post(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    // A failed refresh keeps showing the index we already have.
                    var current = host.AutoEqIndex.Current;
                    updateIndex(s => s with { Index = current, Loading = false, Error = ex.Message });
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Curvy: AutoEQ index load failed: {ex}");
                host.ReportError($"Couldn't load the AutoEQ index: {ex.Message}");
                host.Post(() =>
                {
                    if (!ct.IsCancellationRequested) updateIndex(s => s with { Loading = false });
                });
            }
        }

        void LoadIndex(bool forceRefresh) => _ = LoadIndexAsync(forceRefresh, Token());

        // Mount: load (memory → disk cache → network). Unmount: cancel everything this page awaits.
        UseEffect(() =>
        {
            var cts = life.Current = new CancellationTokenSource();
            _ = LoadIndexAsync(false, cts.Token);
            return () => cts.Cancel();
        }, true);

        // Debounce typing: search 150 ms after the last keystroke.
        UseEffect(() =>
        {
            if (query == searchQuery) return () => { };
            var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = EngineHost.DebounceDelay;
            timer.IsRepeating = false;
            timer.Tick += (_, _) => setSearchQuery(query);
            timer.Start();
            return () => timer.Stop();
        }, query);

        var results = UseMemo(
            () => index.Index is { } idx ? AutoEqSearch.Search(idx, searchQuery, 50) : [],
            index.Index, searchQuery);

        // ── Corrections (preview + create/replace) ───────────────────────
        void HandleFetchFailure(Exception ex, CancellationToken ct)
        {
            switch (ex)
            {
                case OperationCanceledException when ct.IsCancellationRequested:
                    return;
                case AutoEqUnavailableException:
                    host.Post(() => { if (!ct.IsCancellationRequested) setFetchError($"{UnavailableText} ({ex.Message})"); });
                    return;
                default:
                    Debug.WriteLine($"Curvy: AutoEQ fetch failed: {ex}");
                    host.ReportError($"Couldn't fetch the AutoEQ correction: {ex.Message}");
                    return;
            }
        }

        async Task PreviewAsync(AutoEqIndexEntry entry, CancellationToken ct)
        {
            previewWanted.Current = entry;
            setFetchError(null);
            if (host.AutoEqCorrections.TryGet(entry, out var cached))
            {
                updatePreview(_ => new PreviewState(entry, cached));
                return;
            }
            updatePreview(_ => new PreviewState(entry, null));
            try
            {
                var correction = await host.AutoEqCorrections.GetAsync(entry, ct);
                host.Post(() =>
                {
                    if (ct.IsCancellationRequested || previewWanted.Current != entry) return;
                    updatePreview(_ => new PreviewState(entry, correction));
                });
            }
            catch (Exception ex)
            {
                host.Post(() =>
                {
                    // Superseded by another preview (or the page went away): not this preview's error to show.
                    if (ct.IsCancellationRequested || previewWanted.Current != entry) return;
                    previewWanted.Current = null;
                    updatePreview(_ => null);
                    HandleFetchFailure(ex, ct);
                });
            }
        }

        async Task ApplyEntryAsync(AutoEqIndexEntry entry, Guid? replaceId, CancellationToken ct)
        {
            setFetchError(null);
            updateBusyEntry(_ => entry);
            try
            {
                var correction = await host.AutoEqCorrections.GetAsync(entry, ct);
                host.Post(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    updateBusyEntry(_ => null);
                    if (replaceId is { } id)
                    {
                        var replaced = false;
                        host.Now(e =>
                        {
                            // Re-read after Now's flush so pending preference edits aren't lost.
                            var current = e.Profiles.FirstOrDefault(p => p.Id == id)
                                          ?? throw new InvalidOperationException("That profile no longer exists.");
                            e.SaveProfile(current with { AutoEq = correction, AutoEqEnabled = true });
                            replaced = true;
                        });
                        if (replaced) props.OnOpenProfile(id);
                    }
                    else
                    {
                        Guid? createdId = null;
                        host.Now(e =>
                        {
                            // Named after the engine's current list (after Now's flush), e.g. "HD 600 2".
                            var profile = Profile.New(ProfileNames.Unique(e.Profiles, entry.Name), correction);
                            e.SaveProfile(profile);
                            createdId = profile.Id;
                            e.Activate(profile.Id);
                        });
                        if (createdId is { } newId) props.OnOpenProfile(newId);
                    }
                });
            }
            catch (Exception ex)
            {
                host.Post(() => { if (!ct.IsCancellationRequested) updateBusyEntry(_ => null); });
                HandleFetchFailure(ex, ct);
            }
        }

        // ── Results ──────────────────────────────────────────────────────
        var primaryText = replaceTarget is null ? "Create profile" : $"Use for {replaceTarget.Name}";
        var busy = busyEntry is not null;

        AutoEqIndexEntry Chosen(AutoEqHeadphone h) =>
            h.Results[Math.Clamp(sourceChoice.GetValueOrDefault(h.Name), 0, h.Results.Count - 1)];

        Element Row(AutoEqHeadphone h)
        {
            var entry = Chosen(h);
            var isPreviewed = preview?.Entry == entry;
            var isBusy = busyEntry == entry;
            return (FlexRow(
                        TextBlock(h.Name)
                            .TextTrimming(TextTrimming.CharacterEllipsis)
                            .VAlign(VerticalAlignment.Center)
                            .Flex(grow: 1, basis: 0),
                        ComboBox(
                                h.Results.Select(r => $"{r.Source} · {r.Rig}").ToArray(),
                                h.Results.ToList().IndexOf(entry),
                                i => updateSourceChoice(d => d.SetItem(h.Name, i)))
                            .AutomationName($"Measurement source for {h.Name}")
                            .Width(260)
                            .Flex(shrink: 0),
                        Button(isPreviewed ? "Previewing" : "Preview", () => _ = PreviewAsync(entry, Token()))
                            .AutomationName($"Preview {h.Name}")
                            .Flex(shrink: 0),
                        Button(isBusy ? "Fetching…" : primaryText,
                                () => _ = ApplyEntryAsync(entry, replaceTarget?.Id, Token()))
                            .IsEnabled(!busy)
                            .AutomationName($"{primaryText}: {h.Name}")
                            .Flex(shrink: 0)) with { ColumnGap = 8 })
                .WithKey(h.Name);
        }

        Element resultsArea;
        if (index.Index is null && index.Loading)
            resultsArea = VStack(8,
                    ProgressRing().Width(32).Height(32).HAlign(HorizontalAlignment.Center),
                    Caption("Loading the AutoEQ index…").HAlign(HorizontalAlignment.Center))
                .VAlign(VerticalAlignment.Center);
        else if (index.Index is null)
            resultsArea = Caption("The AutoEQ index isn't available yet.");
        else if (string.IsNullOrWhiteSpace(searchQuery))
            resultsArea = Caption($"Type a headphone name to search {index.Index.Count:N0} AutoEQ results.");
        else if (results.Count == 0)
            resultsArea = Caption($"No headphones match \"{searchQuery.Trim()}\".");
        else
            resultsArea = ScrollView(VStack(8, results.Select(Row).ToArray()).Padding(0, 0, 16, 8));

        // ── Preview graph ────────────────────────────────────────────────
        Element graphArea;
        if (preview is { Correction: { } shown })
        {
            var asProfile = replaceTarget is null
                ? Profile.New(preview.Entry.Name, shown)
                : replaceTarget with { AutoEq = shown, AutoEqEnabled = true };
            var series = new List<GraphSeries>
            {
                new("AutoEQ", shown.Filters, GraphSeriesStyle.AutoEq),
                new("Total", EqChain.Build(asProfile, state.Settings with { MasterEnabled = true }).Filters,
                    GraphSeriesStyle.Total),
            };
            graphArea = VStack(4,
                Caption($"Preview (not applied): {preview.Entry.Name} · {preview.Entry.Source} · {preview.Entry.Rig} · {shown.Filters.Count} filters"
                        + (replaceTarget is null ? "" : $" · Total includes {replaceTarget.Name}'s preference")),
                Component<EqGraph, EqGraphProps>(new EqGraphProps(series, [], NoBandEdit)).Height(240));
        }
        else if (preview is not null)
            graphArea = HStack(8, ProgressRing().Width(20).Height(20), Caption($"Fetching {preview.Entry.Name}…"));
        else
            graphArea = Caption("Pick \"Preview\" on a result to see its correction curve.");

        // ── Status bars ──────────────────────────────────────────────────
        Element? indexBar = index.Error is { } indexError
            ? InfoBar(UnavailableText, indexError) with
            {
                IsOpen = true,
                IsClosable = false,
                Severity = index.Index is null ? InfoBarSeverity.Error : InfoBarSeverity.Warning,
                ActionButtonContent = "Retry",
                OnActionButtonClick = () => LoadIndex(forceRefresh: index.Index is not null),
            }
            : null;
        Element? staleBar = index.StaleWarning is { } staleText && index.Error is null
            ? InfoBar(null, staleText) with
            {
                IsOpen = true,
                IsClosable = true,
                Severity = InfoBarSeverity.Warning,
                OnClosed = () => updateIndex(s => s with { StaleWarning = null }),
            }
            : null;
        Element? fetchBar = fetchError is { } fe
            ? InfoBar("Couldn't fetch the correction", fe) with
            {
                IsOpen = true,
                IsClosable = true,
                Severity = InfoBarSeverity.Warning,
                OnClosed = () => setFetchError(null),
            }
            : null;

        // ── Footer ───────────────────────────────────────────────────────
        var footer = HStack(12,
            Caption(index.FetchedAt is { } at ? $"Index updated {at.LocalDateTime:d}" : "Index not downloaded yet")
                .VAlign(VerticalAlignment.Center),
            index.Loading && index.Index is not null
                ? ProgressRing().Width(16).Height(16).VAlign(VerticalAlignment.Center)
                : null,
            Button("Refresh", () => LoadIndex(forceRefresh: true))
                .IsEnabled(!index.Loading)
                .AutomationName("Refresh AutoEQ index"));

        var heading = VStack(4,
            Subtitle("AutoEQ"),
            Caption(replaceTarget is null
                ? "Find your headphones and create a profile from their AutoEQ correction."
                : $"Choose a new AutoEQ correction for \"{replaceTarget.Name}\". Its preference is kept."));

        return (FlexColumn(
                heading.Flex(shrink: 0),
                TextBox(query, setQuery, "Search headphones, e.g. DT 880")
                    .AutomationName("Search AutoEQ headphones")
                    .Flex(shrink: 0),
                indexBar?.Flex(shrink: 0),
                staleBar?.Flex(shrink: 0),
                fetchBar?.Flex(shrink: 0),
                graphArea.Flex(shrink: 0),
                resultsArea.Flex(grow: 1, basis: 0),
                footer.Flex(shrink: 0)) with { RowGap = 12 })
            .FlexPadding(16);
    }

    static void NoBandEdit(int index, Filter band) { }
}
