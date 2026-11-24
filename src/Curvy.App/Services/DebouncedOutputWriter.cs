using System.Diagnostics;
using Curvy.Core.Apo;
using Curvy.Core.Chain;

namespace Curvy.App.Services;

/// <summary>
/// Coalesces chain updates and writes the latest one to <c>Curvy\curvy.txt</c> after a quiet
/// period. Never touches <c>config.txt</c>. If no APO config directory is found, writes are
/// skipped silently.
/// </summary>
public sealed class DebouncedOutputWriter(TimeSpan delay) : IDisposable
{
    readonly object _gate = new();
    readonly object _writeGate = new();
    ChainResult? _pending;
    Timer? _timer;

    public void Schedule(ChainResult result)
    {
        lock (_gate)
        {
            _pending = result;
            _timer ??= new Timer(_ => Flush());
            _timer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Writes any pending result immediately (e.g. before exit).</summary>
    public void Flush()
    {
        ChainResult? r;
        lock (_gate)
        {
            r = _pending;
            _pending = null;
        }
        if (r is null) return;

        lock (_writeGate)
        {
            var dir = ApoLocator.FindConfigDirectory();
            if (dir is null) return;
            try
            {
                new ApoConfig(dir).WriteOutput(r);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Curvy: could not write APO output: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        lock (_gate) _timer?.Dispose();
    }
}
