using System.Diagnostics;
using Curvy.App.Services;
using Curvy.Core.AutoEq;
using Curvy.Core.Model;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Curvy.App.Components;

/// <param name="PlaceholderText">Hint shown in the empty search box.</param>
/// <param name="OnPicked">Called on the UI thread with the fetched correction.</param>
public record AutoEqPickerProps(string PlaceholderText, Action<AutoEqCorrection> OnPicked);

/// <summary>
/// Inline AutoEQ search: type a headphone name, pick "Name · Source · Rig" from the suggestions,
/// and the correction is fetched and handed to <see cref="AutoEqPickerProps.OnPicked"/>.
/// Uses the same cached index and correction cache as the AutoEQ page.
/// </summary>
public sealed class AutoEqPicker : Component<AutoEqPickerProps>
{
    const int MaxSuggestions = 20;

    static string Label(AutoEqIndexEntry e) => AutoEqText.Join(e.Name, e.Source, e.Rig);

    public override Element Render()
    {
        var host = EngineHost.Instance;
        var props = Props;

        var (text, setText) = UseState("");
        var (matches, setMatches) = UseState<IReadOnlyList<AutoEqIndexEntry>>(Array.Empty<AutoEqIndexEntry>());
        var (busy, setBusy) = UseState<AutoEqIndexEntry?>(null);
        var (error, setError) = UseState<string?>(null);
        var life = UseRef<CancellationTokenSource?>(null);

        CancellationToken Token() => life.Current?.Token ?? CancellationToken.None;

        // Mount: warm the index (memory → disk cache → network) so the first keystroke has results.
        UseEffect(() =>
        {
            var cts = life.Current = new CancellationTokenSource();
            _ = host.AutoEqIndex.LoadAsync(false).ContinueWith(_ => { }, TaskScheduler.Default);
            return () => cts.Cancel();
        }, true);

        static IReadOnlyList<AutoEqIndexEntry> Find(IReadOnlyList<AutoEqIndexEntry> index, string query) =>
            string.IsNullOrWhiteSpace(query)
                ? []
                : AutoEqSearch.Search(index, query, MaxSuggestions)
                    .SelectMany(h => h.Results)
                    .DistinctBy(Label)
                    .Take(MaxSuggestions)
                    .ToList();

        async Task SearchAsync(string query, CancellationToken ct)
        {
            if (host.AutoEqIndex.Current is { } idx)
            {
                setMatches(Find(idx, query));
                return;
            }
            try
            {
                var loaded = await host.AutoEqIndex.LoadAsync(false).WaitAsync(ct);
                host.Post(() => { if (!ct.IsCancellationRequested) setMatches(Find(loaded, query)); });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                host.Post(() => { if (!ct.IsCancellationRequested) setError($"The AutoEQ index isn't available: {ex.Message}"); });
            }
        }

        void OnTextChanged(string t)
        {
            setText(t);
            setError(null);
            _ = SearchAsync(t, Token());
        }

        async Task PickAsync(AutoEqIndexEntry entry, CancellationToken ct)
        {
            setBusy(entry);
            setError(null);
            try
            {
                var correction = await host.AutoEqCorrections.GetAsync(entry, ct);
                host.Post(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    setBusy(null);
                    setText("");
                    setMatches([]);
                    props.OnPicked(correction);
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Curvy: AutoEQ fetch failed: {ex}");
                host.Post(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    setBusy(null);
                    setError($"Couldn't fetch {entry.Name}: {ex.Message}");
                });
            }
        }

        // Submitting (Enter or clicking a suggestion) applies the exact match, else the top result.
        void OnSubmitted(string submitted)
        {
            if (busy is not null) return;
            var entry = matches.FirstOrDefault(e => Label(e) == submitted) ?? matches.FirstOrDefault();
            if (entry is not null) _ = PickAsync(entry, Token());
        }

        var box = AutoSuggestBox(text, OnTextChanged, OnSubmitted).AutomationName("Search AutoEQ") with
        {
            Suggestions = matches.Select(Label).ToArray(),
            PlaceholderText = props.PlaceholderText,
            QueryIcon = SymbolIcon("Find"),
            OnSuggestionChosen = setText,
        };

        return VStack(4,
            HStack(8,
                box.Width(420).IsEnabled(busy is null),
                busy is { } b
                    ? HStack(8,
                            ProgressRing().Width(16).Height(16).VAlign(VerticalAlignment.Center),
                            Caption($"Fetching {b.Name}…").VAlign(VerticalAlignment.Center))
                        .WithKey("busy")
                    : null),
            error is { } err ? Caption(err).Foreground(Theme.SystemCritical) : null);
    }
}
