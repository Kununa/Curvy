using Curvy.App.Services;
using Curvy.Core.Chain;
using Curvy.Core.Filters;
using Curvy.Core.Model;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using static Microsoft.UI.Reactor.Factories;

namespace Curvy.App.Components;

public record UniversalPageProps(EngineState State);

/// <summary>
/// The universal preference: applied on top of every profile (unless a profile ignores it) and on
/// its own when no profile is active. Edits are saved through <see cref="EngineHost.Debounced"/>
/// under <see cref="EngineHost.UniversalKey"/>.
/// </summary>
public sealed class UniversalPage : Component<UniversalPageProps>
{
    public override Element Render()
    {
        var host = EngineHost.Instance;
        var state = Props.State;
        var saved = state.Settings.Universal;
        var draft = UseRef(saved);
        var lastSaved = UseRef(saved);
        var (_, bump) = UseReducer(0);

        // Leaving the page: persist pending edits now.
        UseEffect(() => () => EngineHost.Instance.Flush(), true);

        // Adopt changes made elsewhere unless our own edits are in flight.
        if (!ReferenceEquals(lastSaved.Current, saved))
        {
            lastSaved.Current = saved;
            if (!host.HasPending(EngineHost.UniversalKey) && saved != draft.Current) draft.Current = saved;
        }
        var pref = draft.Current;

        void Update(Preference next)
        {
            if (next == draft.Current) return;
            draft.Current = next;
            bump(n => n + 1);
            host.Debounced(EngineHost.UniversalKey, () => host.Engine.SetUniversal(next));
        }

        void OnBandChanged(int i, Filter band)
        {
            var p = draft.Current;
            if (i < p.Bands.Count) Update(p with { Bands = p.Bands.SetItem(i, EqLimits.Clamp(band)) });
        }

        var active = state.ActiveProfile;
        var settings = state.Settings with { Universal = pref, MasterEnabled = true };
        var series = new List<GraphSeries>
        {
            new("Universal", pref.AllFilters().ToList(), GraphSeriesStyle.Universal),
            new("Total", EqChain.Build(active, settings).Filters, GraphSeriesStyle.Total),
        };

        var totalNote = active switch
        {
            null => "Total: no profile is active, so only the universal preference applies.",
            { IgnoreUniversal: true } => $"Total: the active profile \"{active.Name}\" ignores the universal preference.",
            _ => $"Total: the active profile \"{active.Name}\" plus the universal preference.",
        };

        return (FlexColumn(
                VStack(4,
                    Subtitle("Universal preference"),
                    Caption("Applied on top of every profile (unless a profile ignores it), and on its own when no profile is active."))
                    .Flex(shrink: 0),
                Component<EqGraph, EqGraphProps>(new EqGraphProps(series, pref.ActiveBands, OnBandChanged))
                    .Flex(basis: 280, shrink: 0),
                Caption(totalNote).Flex(shrink: 0),
                ScrollView(
                        Card(Component<PreferenceEditor, PreferenceEditorProps>(
                                new PreferenceEditorProps(pref, Update)))
                            .Margin(0, 0, 16, 16))
                    .Flex(grow: 1, basis: 0)) with { RowGap = 12 })
            .FlexPadding(16);
    }
}
