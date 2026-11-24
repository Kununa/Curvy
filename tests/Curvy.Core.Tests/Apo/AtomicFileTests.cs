using Curvy.Core.Apo;
using Xunit;

namespace Curvy.Core.Tests.Apo;

public sealed class AtomicFileTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "curvy-atomic-" + Guid.NewGuid().ToString("N"));

    public AtomicFileTests() => Directory.CreateDirectory(dir);
    public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

    [Fact]
    public async Task WriteAllText_RetriesWhenTargetBrieflyHeldOpen()
    {
        var path = Path.Combine(dir, "config.txt");
        File.WriteAllText(path, "old");
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(() => { Thread.Sleep(80); held.Dispose(); });

        AtomicFile.WriteAllText(path, "new");
        await release;

        Assert.Equal("new", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    [Fact]
    public void WriteAllText_CleansUpTempAndRethrows_WhenTargetStaysLocked()
    {
        var path = Path.Combine(dir, "config.txt");
        File.WriteAllText(path, "old");
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.ThrowsAny<Exception>(() => AtomicFile.WriteAllText(path, "new"));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }
}
