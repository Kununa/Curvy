using System.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace Curvy.App.Services;

/// <summary>
/// Velopack updates from this repo's GitHub Releases. Checked once per start in the background (and
/// on demand from Settings), only in an installed copy (not dev builds); a found update is downloaded
/// quietly and then offered as "Restart to update" (window bar, tray, Settings). Nothing restarts
/// without the user clicking.
/// <para>
/// Reads the releases anonymously from the public repo. GitHub allows 60 anonymous API requests per
/// hour per IP, plenty for one check per start. A failed startup check is never shown as an error
/// bar; Settings shows it quietly.
/// </para>
/// </summary>
static class Updater
{
    public const string RepoUrl = "https://github.com/Kununa/Curvy";

    static UpdateManager? _manager;
    static UpdateInfo? _ready;
    static int _running; // 1 while a check runs: Settings' button can't start a second one

    /// <summary>Starts a background check; reports each stage via <see cref="EngineHost.SetUpdateStatus"/>.</summary>
    public static void Check(EngineHost host)
    {
        if (_ready is not null || Interlocked.Exchange(ref _running, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            void Report(UpdateStatus status) => host.Post(() => host.SetUpdateStatus(status));
            try
            {
                var manager = _manager ?? new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
                if (!manager.IsInstalled)
                {
                    Report(new(UpdateKind.NotInstalled));
                    return;
                }
                _manager = manager;
                Report(new(UpdateKind.Checking));

                var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
                if (update is null)
                {
                    Report(new(UpdateKind.UpToDate));
                    return;
                }
                var version = update.TargetFullRelease.Version.ToString();
                Report(new(UpdateKind.Downloading, version));
                await manager.DownloadUpdatesAsync(update).ConfigureAwait(false);

                _ready = update;
                Report(new(UpdateKind.Ready, version));
            }
            catch (Exception ex)
            {
                // Offline, rate-limited…: an update check must never bother the user.
                Debug.WriteLine($"Curvy: update check failed: {ex.Message}");
                Report(new(UpdateKind.Failed, Error: ex.Message));
            }
            finally
            {
                Volatile.Write(ref _running, 0);
            }
        });
    }

    /// <summary>Saves pending edits, then exits, installs the downloaded update and starts Curvy again.</summary>
    public static void ApplyAndRestart(EngineHost host)
    {
        if (_manager is not { } manager || _ready is not { } update) return;
        host.Flush();
        host.Shutdown();
        manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
    }
}
