using Curvy.Core.Apo;
using Curvy.Core.Chain;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Curvy.App.Components;

public record FilterTextViewProps(ChainResult Current, string? ActiveProfileName);

/// <summary>
/// The live output: the preamp in large text, plus a collapsible read-only monospace block
/// with exactly what Curvy writes to <c>curvy.txt</c> (<see cref="ApoFormatter.Format"/>).
/// </summary>
public sealed class FilterTextView : Component<FilterTextViewProps>
{
    public override Element Render()
    {
        var (expanded, setExpanded) = UseState(false);
        var current = Props.Current;
        var text = ApoFormatter.Format(current).TrimEnd();
        var source = current.Bypassed
            ? "EQ is off (bypassed)"
            : Props.ActiveProfileName is { } name ? $"Active profile: {name}" : "No profile active";

        var preamp = current.Bypassed ? "—" : $"{current.PreampDb:0.0} dB";

        return VStack(8,
            HStack(16,
                VStack(0,
                    Caption("Preamp"),
                    TitleLarge(preamp).AutomationName("Preamp")),
                Caption(source).VAlign(VerticalAlignment.Bottom).Margin(0, 0, 0, 8)),
            Expander("APO output (curvy.txt)",
                    TextBlock(text)
                        .FontFamily("Cascadia Mono, Consolas")
                        .IsTextSelectionEnabled()
                        .AutomationName("APO output"),
                    expanded, setExpanded)
                .HAlign(HorizontalAlignment.Stretch));
    }
}
