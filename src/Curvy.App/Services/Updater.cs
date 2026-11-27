using System.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace Curvy.App.Services;

/// <summary>
/// Velopack updates from this repo's GitHub Releases. Checked once per start, in the background, and
/// only in an installed copy (not dev builds); the update is downloaded quietly and then offered as
/// "Restart to update" (window bar + tray). Nothing restarts without the user clicking.
/// <para>
/// Reads the releases anonymously, so the repo (or at least its releases) must be public. GitHub
/// allows 60 anonymous API requests per hour per IP, plenty for one check per start. While the repo
/// is private the check simply finds nothing; failures are never shown to the user.
/// </para>
/// </summary>
static class Updater
{
    public const string RepoUrl = "https://github.com/Kununa/Curvy";

    static UpdateManager? _manager;
    static UpdateInfo? _ready;

    /// <summary>Starts the background check; reports a downloaded update via <see cref="EngineHost.SetUpdateReady"/>.</summary>
    public static void CheckOnStartup(EngineHost host) => _ = Task.Run(async () =>
    {
        try
        {
            var manager = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
            if (!manager.IsInstalled) return;

            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null) return;
            await manager.DownloadUpdatesAsync(update).ConfigureAwait(false);

            _manager = manager;
            _ready = update;
            host.Post(() => host.SetUpdateReady(update.TargetFullRelease.Version.ToString()));
        }
        catch (Exception ex)
        {
            // Offline, rate-limited, no access…: an update check must never bother the user.
            Debug.WriteLine($"Curvy: update check failed: {ex.Message}");
        }
    });

    /// <summary>Saves pending edits, then exits, installs the downloaded update and starts Curvy again.</summary>
    public static void ApplyAndRestart(EngineHost host)
    {
        if (_manager is not { } manager || _ready is not { } update) return;
        host.Flush();
        host.Shutdown();
        manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
    }
}
