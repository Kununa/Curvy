using System.Diagnostics;
using System.Runtime.InteropServices;
using Curvy.Core.SystemIntegration;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Curvy.App.Services;

/// <summary>
/// One Curvy per user session (Windows App SDK <see cref="AppInstance"/> keyed "Curvy"). A second
/// launch redirects its activation to the first instance and exits; the first instance then
/// shows its window (unless the redirected launch was <c>--tray</c>).
/// </summary>
public static class SingleInstance
{
    const string Key = "Curvy";

    static readonly Lock Gate = new();
    static Action<bool>? _handler;
    static bool? _pendingTrayOnly;

    /// <summary>True when the command line asks to start in the tray without a window.</summary>
    public static bool IsTrayLaunch(IEnumerable<string> args) =>
        args.Any(a => string.Equals(a.Trim('"'), StartupRegistration.TrayArgument, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Registers this process as the Curvy instance, or redirects to the existing one. Returns false
    /// when this process should exit. Fails open (returns true) if the AppInstance APIs fail.
    /// </summary>
    public static bool TryClaim()
    {
        AppInstance keyInstance;
        try
        {
            keyInstance = AppInstance.FindOrRegisterForKey(Key);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Curvy: single-instance registration failed: {ex}");
            return true;
        }

        if (keyInstance.IsCurrent)
        {
            keyInstance.Activated += OnActivated;
            return true;
        }

        try
        {
            // Let the first instance bring its window to the foreground (we own foreground rights now).
            AllowSetForegroundWindow(keyInstance.ProcessId);
            var args = AppInstance.GetCurrent().GetActivatedEventArgs();
            // Off the calling thread: blocking an STA on a WinRT async call can deadlock.
            Task.Run(() => keyInstance.RedirectActivationToAsync(args).AsTask()).Wait(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Curvy: redirecting to the running instance failed: {ex}");
        }
        return false;
    }

    /// <summary>
    /// Sets the UI's handler for redirected launches (argument: tray-only). Called on a background
    /// thread; the handler must marshal to the UI thread. A launch redirected before a handler was
    /// set is delivered on subscription. Pass null to unsubscribe.
    /// </summary>
    public static void SetHandler(Action<bool>? handler)
    {
        bool? pending;
        lock (Gate)
        {
            _handler = handler;
            pending = handler is null ? null : _pendingTrayOnly;
            _pendingTrayOnly = null;
        }
        if (pending is { } trayOnly) handler!(trayOnly);
    }

    static void OnActivated(object? sender, AppActivationArguments e)
    {
        var trayOnly = e.Kind == ExtendedActivationKind.Launch
                       && e.Data is ILaunchActivatedEventArgs launch
                       && IsTrayLaunch((launch.Arguments ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Action<bool>? handler;
        lock (Gate)
        {
            handler = _handler;
            if (handler is null) _pendingTrayOnly = (_pendingTrayOnly ?? true) && trayOnly;
        }
        handler?.Invoke(trayOnly);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
