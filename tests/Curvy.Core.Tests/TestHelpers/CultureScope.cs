using System.Globalization;

namespace Curvy.Core.Tests;

public static class CultureScope
{
    public static void WithCulture(string name, Action action)
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            action();
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
}
