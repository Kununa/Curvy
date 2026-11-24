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
            File.Move(tmp, path, overwrite: true);
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
            File.Move(tmp, dest, overwrite);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
    }

    private static string TempName(string path) => path + "." + Guid.NewGuid().ToString("N") + ".tmp";
}
