using System.Collections.Immutable;
using Curvy.Core.Apo;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Curvy.App.Components;

/// <param name="Lines">Active EQ-like lines of config.txt that aren't Curvy's.</param>
/// <param name="OnDone">Lines to disable, as read (empty when the user keeps them all).</param>
public record FirstRunDialogProps(IReadOnlyList<ConfigLine> Lines, Action<IReadOnlyCollection<ConfigLine>> OnDone);

/// <summary>
/// Shown once: other EQ in config.txt (Peace, another include, a preamp…) would stack with Curvy's.
/// Lists those lines with checkboxes, all checked; confirming disables the checked ones (Curvy
/// comments them out and remembers them, so they can be re-enabled in Settings).
/// </summary>
public sealed class FirstRunDialog : Component<FirstRunDialogProps>
{
    public override Element Render()
    {
        var props = Props;
        var (unchecked_, updateUnchecked) = UseReducer(ImmutableHashSet<int>.Empty);
        var (open, setOpen) = UseState(true);

        var checks = props.Lines
            .Select(l => (Element)CheckBox(!unchecked_.Contains(l.Index),
                    on => updateUnchecked(s => on ? s.Remove(l.Index) : s.Add(l.Index)),
                    l.Text.Trim())
                .AutomationName($"Disable {l.Text.Trim()}")
                .WithKey($"{l.Index}:{l.Text}"))
            .ToArray();

        var content = VStack(12,
            Body("Equalizer APO's config.txt already applies other EQ. Curvy adds its own EQ on top, " +
                 "so these effects would stack and can clip or sound wrong. Curvy can turn them off " +
                 "(it comments them out and keeps a backup of config.txt; you can turn them back on in Settings)."),
            BodyStrong("Turn off:"),
            ScrollView(VStack(4, checks)).MaxHeight(240));

        var checkedCount = props.Lines.Count(l => !unchecked_.Contains(l.Index));

        return ContentDialog("Other EQ is active", content, checkedCount > 0 ? $"Turn off {checkedCount}" : "Continue") with
        {
            IsOpen = open,
            CloseButtonText = "Keep all",
            DefaultButton = ContentDialogButton.Primary,
            OnClosed = result =>
            {
                setOpen(false);
                props.OnDone(result == ContentDialogResult.Primary
                    ? props.Lines.Where(l => !unchecked_.Contains(l.Index)).ToList()
                    : []);
            },
        };
    }
}
