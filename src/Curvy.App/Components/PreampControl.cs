using Curvy.App.Services;
using Curvy.Core.Chain;
using Curvy.Core.Model;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Curvy.App.Components;

/// <param name="Settings">Current settings (preamp mode and manual value).</param>
/// <param name="Current">What is applied right now (its preamp and filters drive the readout).</param>
public record PreampControlProps(CurvySettings Settings, ChainResult Current);

/// <summary>
/// Preamp quick switch: Auto (clip-safe) · −6 dB · −12 dB · Custom, plus the applied value and a
/// clipping warning when a manual preamp leaves less headroom than the curve needs.
/// </summary>
public sealed class PreampControl : Component<PreampControlProps>
{
    enum Choice { Auto, Minus6, Minus12, Custom }

    public override Element Render()
    {
        var host = EngineHost.Instance;
        var s = Props.Settings;
        var current = Props.Current;

        // "Custom" is sticky while the user is on it, even if they type −6 or −12.
        var (customPicked, setCustomPicked) = UseState(false);
        // Optimistic selection: shown immediately on click; the engine write follows on the
        // settings debounce, so the click never waits for disk I/O or a page re-render.
        var (pending, setPending) = UseState<Choice?>(null);

        var saved = s.PreampMode == PreampMode.Auto ? Choice.Auto
            : customPicked ? Choice.Custom
            : s.ManualPreampDb == -6 ? Choice.Minus6
            : s.ManualPreampDb == -12 ? Choice.Minus12
            : Choice.Custom;
        var choice = pending ?? saved;

        // Once the engine has caught up, drop the optimistic value.
        UseEffect(() => { if (pending is { } p && p == saved) setPending(null); }, saved, pending);

        void Pick(Choice c)
        {
            if (c == choice) return;
            setCustomPicked(c == Choice.Custom);
            setPending(c);
            var manual = s.ManualPreampDb;
            host.Debounced(EngineHost.SettingsKey, () =>
            {
                switch (c)
                {
                    case Choice.Auto: host.Engine.SetPreamp(PreampMode.Auto, manual); break;
                    case Choice.Minus6: host.Engine.SetPreamp(PreampMode.Manual, -6); break;
                    case Choice.Minus12: host.Engine.SetPreamp(PreampMode.Manual, -12); break;
                    case Choice.Custom: host.Engine.SetPreamp(PreampMode.Manual, manual); break;
                }
            });
        }

        // Segmented group made of standard WinUI toggle buttons (native Fluent look: the selected
        // one gets the accent fill). Inner corners are squared and borders overlap so the four
        // buttons read as one control. Radio semantics: the selected button isn't clickable, so
        // it can never be toggled off.
        var options = new[]
        {
            (Choice.Auto, "Auto"), (Choice.Minus6, "−6 dB"), (Choice.Minus12, "−12 dB"), (Choice.Custom, "Custom"),
        };
        const double Radius = 4;
        var sections = new Element[options.Length];
        for (var i = 0; i < options.Length; i++)
        {
            var (c, label) = options[i];
            var selected = choice == c;
            var left = i == 0 ? Radius : 0;
            var right = i == options.Length - 1 ? Radius : 0;
            sections[i] = ToggleButton(label, selected, on => { if (on) Pick(c); })
                .IsHitTestVisible(!selected)
                .CornerRadius(left, right, right, left)
                .Margin(i == 0 ? 0 : -1, 0, 0, 0)
                .AutomationName(selected ? $"Preamp {label}, selected" : $"Preamp {label}")
                .WithKey(c.ToString());
        }
        var segmented = HStack(0, sections).VAlign(VerticalAlignment.Center);

        // Clipping check only matters for a manual preamp (auto never clips by construction).
        var clipBy = s.PreampMode == PreampMode.Manual && !current.Bypassed
            ? EqChain.PeakGainDb(current.Filters) + current.PreampDb
            : 0;

        return VStack(4,
                HStack(8,
                    Caption("Preamp").VAlign(VerticalAlignment.Center),
                    segmented,
                    choice == Choice.Custom
                        ? Component<NumField, NumFieldProps>(new NumFieldProps(
                                "", s.ManualPreampDb, -30, 12, 0.5,
                                v => host.Debounced(EngineHost.SettingsKey,
                                    () => host.Engine.SetPreamp(PreampMode.Manual, v)),
                                96))
                            .WithKey("custom")
                        : null),
                clipBy > 0.05
                    ? Caption($"May clip by up to {clipBy:0.0} dB — lower the preamp or use Auto.")
                        .Foreground(Theme.SystemCritical)
                        .HAlign(HorizontalAlignment.Right)
                    : null)
            .HAlign(HorizontalAlignment.Right);
    }
}
