using System.Text;

namespace Curvy.Core.Apo;

public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static void WriteAllText(string path, string content) =>
        WriteAllBytes(path, Utf8NoBom.GetBytes(content));

    public static void WriteAllBytes(string path, byte[] bytes)
    {
        var tmp = TempName(path);
        try
        {
            File.WriteAllBytes(tmp, bytes);
            MoveWithRetry(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
    }

    public static void CopyAtomic(string source, string dest, bool overwrite)
    {
        var tmp = TempName(dest);
        try
        {
            File.Copy(source, tmp, overwrite: true);
            MoveWithRetry(tmp, dest, overwrite);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
    }

    /// <summary>The APO service may hold the target open briefly; retry the final replace a few times.</summary>
    private static void MoveWithRetry(string tmp, string target, bool overwrite)
    {
        const int attempts = 3;
        for (var i = 1; ; i++)
        {
            try
            {
                File.Move(tmp, target, overwrite);
                return;
            }
            catch (Exception ex) when (i < attempts && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static string TempName(string path) => path + "." + Guid.NewGuid().ToString("N") + ".tmp";
}
