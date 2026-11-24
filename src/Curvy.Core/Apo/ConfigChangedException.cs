namespace Curvy.Core.Apo;

/// <summary>
/// config.txt no longer matches what the caller read (another app changed it in between);
/// nothing was written. Re-read the file and try again.
/// </summary>
public sealed class ConfigChangedException() : InvalidOperationException("config.txt changed");
