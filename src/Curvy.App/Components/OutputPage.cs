using Curvy.App.Services;
using Curvy.Core.Apo;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Curvy.App.Components;

public record OutputPageProps(EngineState State);

/// <summary>
/// "Output" tab: what Curvy is writing to Equalizer APO right now — the source (active profile,
/// no profile, or EQ off), the applied preamp, and the exact text of <c>curvy.txt</c>
/// (<see cref="ApoFormatter.Format"/>). Always reflects the active profile, not the one being edited.
/// </summary>
public sealed class OutputPage : Component<OutputPageProps>
{
    public override Element Render()
    {
        var state = Props.State;
        var current = state.Current;
        var text = ApoFormatter.Format(current).TrimEnd();
        var source = current.Bypassed
            ? "EQ is off (bypassed)"
            : state.ActiveProfile?.Name is { } name ? $"Active profile: {name}" : "No profile active (universal preference only)";
        var preamp = current.Bypassed ? "—" : $"{current.PreampDb:0.0} dB";
        var mode = state.Settings.PreampMode == Curvy.Core.Model.PreampMode.Auto ? "Auto" : "Fixed";

        return ScrollView(
            VStack(16,
                Subtitle("Output"),
                Body("What Curvy currently sends to Equalizer APO. This always shows the active profile."),
                Card(HStack(32,
                    VStack(0, Caption("Source"), BodyStrong(source)),
                    VStack(0, Caption($"Preamp ({mode})"), BodyStrong(preamp).AutomationName("Preamp")))),
                Card(VStack(8,
                    BodyStrong("curvy.txt"),
                    TextBlock(text)
                        .FontFamily("Cascadia Mono, Consolas")
                        .IsTextSelectionEnabled()
                        .AutomationName("APO output"))))
                .Padding(24, 16, 24, 24));
    }
}
