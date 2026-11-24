using System.Text;

namespace Curvy.Core.Apo;

public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static void WriteAllText(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, Utf8NoBom);
        File.Move(tmp, path, overwrite: true);
    }
}
