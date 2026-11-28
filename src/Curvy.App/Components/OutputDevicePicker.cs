using System.Diagnostics;
using Curvy.App.Audio;
using Curvy.App.Services;
using Curvy.Core.Apo;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Curvy.App.Components;

/// <summary>
/// The Windows default playback device, switchable. Warns when Equalizer APO isn't installed on it
/// (the EQ then does nothing there) and offers Equalizer APO's Device Selector to add it.
/// Follows changes made elsewhere (Windows settings, plugging in headphones) live.
/// </summary>
public sealed class OutputDevicePicker : Component
{
    public override Element Render()
    {
        var host = EngineHost.Instance;
        var (outputs, setOutputs) = UseState(AudioOutputs.Empty);

        UseEffect(() =>
        {
            setOutputs(AudioDevices.Read());
            // Notifications arrive on a background thread: re-read on the UI thread.
            var watch = AudioDevices.Watch(() => host.Post(() => setOutputs(AudioDevices.Read())));
            return () => watch.Dispose();
        }, true);

        var devices = outputs.Devices;
        if (devices.Count == 0) return Empty();

        var selectedIndex = devices.ToList().FindIndex(d => d.Id == outputs.DefaultId);
        var items = devices
            .Select(d => (Element)TextBlock(d.HasEqualizerApo ? d.Name : $"{d.Name} (no Equalizer APO)")
                .TextTrimming(TextTrimming.CharacterEllipsis)
                .AutomationName(d.Name))
            .ToArray();

        var picker = HStack(8,
            Caption("Output device").VAlign(VerticalAlignment.Center),
            ComboBox(items, selectedIndex, i =>
                {
                    if (i < 0 || i >= devices.Count || devices[i].Id == outputs.DefaultId) return;
                    if (AudioDevices.SetDefault(devices[i].Id))
                        setOutputs(outputs with { DefaultId = devices[i].Id }); // the notification confirms it
                    else
                        host.ReportError("Couldn't switch the output device.");
                })
                .Width(260)
                .AutomationName("Output device"));

        if (outputs.Default is not { HasEqualizerApo: false }) return picker;

        // The EQ isn't applied on this device: say so, and offer the fix.
        var selector = ApoDevices.DeviceSelectorPath();
        return HStack(12,
            picker,
            Caption("Equalizer APO isn't installed on this device, so the EQ isn't applied.")
                .Foreground(Theme.SystemCritical)
                .TextWrapping(TextWrapping.Wrap)
                .VAlign(VerticalAlignment.Center),
            selector is null
                ? null
                : Button("Add it…", () => OpenDeviceSelector(host, selector)).VAlign(VerticalAlignment.Center));
    }

    static void OpenDeviceSelector(EngineHost host, string path)
    {
        try
        {
            // Asks for admin rights itself (its manifest); Equalizer APO then wants a restart.
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Cancelling the UAC prompt lands here too (ERROR_CANCELLED): nothing to report then.
            if (ex is System.ComponentModel.Win32Exception { NativeErrorCode: 1223 }) return;
            host.ReportError($"Couldn't open Equalizer APO's Device Selector: {ex.Message}");
        }
    }
}
