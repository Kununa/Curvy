using System.IO;
using Curvy.Core.Apo;
using System.Threading.Tasks;

namespace Curvy.Core.Tests.Apo;

public sealed class AtomicFileTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "curvy-atomic-" + Guid.NewGuid().ToString("N"));

    public AtomicFileTests() => Directory.CreateDirectory(dir);
    public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

    [Test]
    public async Task WriteAllText_RetriesWhenTargetBrieflyHeldOpen()
    {
        var path = Path.Combine(dir, "config.txt");
        File.WriteAllText(path, "old");
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(() => { Thread.Sleep(80); held.Dispose(); });

        AtomicFile.WriteAllText(path, "new");
        await release;

        await Assert.That(File.ReadAllText(path)).IsEqualTo("new");
        await Assert.That(Directory.GetFiles(dir, "*.tmp")).IsEmpty();
    }

    [Test]
    public async Task WriteAllText_CleansUpTempAndRethrows_WhenTargetStaysLocked()
    {
        var path = Path.Combine(dir, "config.txt");
        File.WriteAllText(path, "old");
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        await Assert.That(() => AtomicFile.WriteAllText(path, "new")).Throws<Exception>();
        await Assert.That(Directory.GetFiles(dir, "*.tmp")).IsEmpty();
    }
}