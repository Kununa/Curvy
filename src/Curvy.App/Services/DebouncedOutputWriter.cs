using System.Diagnostics;
using Curvy.Core.Apo;
using Curvy.Core.Chain;

namespace Curvy.App.Services;

/// <summary>
/// Coalesces chain updates and writes the latest one to <c>Curvy\curvy.txt</c> after a quiet
/// period. Never touches <c>config.txt</c>. If no APO config directory is found, writes are
/// skipped silently. Never throws: it runs on a thread-pool timer and on the exit path, where
/// an escaped exception would terminate the process.
/// </summary>
public sealed class DebouncedOutputWriter(TimeSpan delay) : IDisposable
{
    readonly object _gate = new();
    readonly object _writeGate = new();
    ChainResult? _pending;
    Timer? _timer;
    bool _disposed;

    public void Schedule(ChainResult result)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _pending = result;
            _timer ??= new Timer(_ => Flush());
            _timer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Writes any pending result immediately (e.g. before exit). Never throws.</summary>
    public void Flush()
    {
        try
        {
            // Take the pending result under the write gate so an older result can never be
            // written after a newer one (a concurrent flush either wrote it already or will
            // see the newer value).
            lock (_writeGate)
            {
                ChainResult? r;
                lock (_gate)
                {
                    if (_disposed) return;
                    r = _pending;
                    _pending = null;
                }
                if (r is null) return;

                var dir = ApoLocator.FindConfigDirectory();
                if (dir is null) return;
                new ApoConfig(dir).WriteOutput(r);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Curvy: could not write APO output: {ex}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending = null;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
