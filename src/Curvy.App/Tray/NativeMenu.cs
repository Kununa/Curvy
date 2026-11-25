using System.Runtime.InteropServices;

namespace Curvy.App.Tray;

/// <summary>One entry of a <see cref="NativeMenu"/>; <c>null</c> text is a separator.</summary>
public sealed record NativeMenuItem(string? Text, Action? OnClick = null, bool Checked = false, bool Enabled = true)
{
    // Cast needed: a bare null would bind to the record's copy constructor.
    public static NativeMenuItem Separator { get; } = new((string?)null);
}

/// <summary>
/// A native Win32 popup menu at the cursor, the way tray menus are meant to work: it closes on any
/// click outside it, on Esc, or when another window takes focus. (A window-based flyout opened from
/// a tray click often can't take the foreground, so it never deactivates and never light-dismisses.)
/// </summary>
static class NativeMenu
{
    const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_CHECKED = 0x8, MF_SEPARATOR = 0x800;
    const uint TPM_RIGHTBUTTON = 0x2, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;
    const uint WM_NULL = 0x0;
    const uint WS_POPUP = 0x80000000, WS_EX_TOOLWINDOW = 0x80;

    static IntPtr _owner;

    /// <summary>Shows the menu at the cursor and runs the chosen item's action. UI thread only; blocks until closed.</summary>
    public static void Show(IReadOnlyList<NativeMenuItem> items)
    {
        var owner = Owner();
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Text is null)
                {
                    AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                    continue;
                }
                var flags = MF_STRING | (item.Checked ? MF_CHECKED : 0) | (item.Enabled ? 0 : MF_GRAYED);
                AppendMenuW(menu, flags, (UIntPtr)(uint)(i + 1), item.Text);
            }

            MenuTheme.Apply(dark: !TaskbarTheme.IsLight());
            GetCursorPos(out var pt);
            // The owner must be the foreground window, or the menu won't close on an outside click.
            SetForegroundWindow(owner);
            var chosen = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_NONOTIFY | TPM_RIGHTBUTTON, pt.X, pt.Y, owner, IntPtr.Zero);
            // Documented workaround so the next tray click opens the menu reliably.
            PostMessageW(owner, WM_NULL, UIntPtr.Zero, IntPtr.Zero);

            if (chosen > 0 && chosen <= items.Count)
                items[chosen - 1].OnClick?.Invoke();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    /// <summary>Hidden, never-shown popup window that owns the menu (created once per process).</summary>
    static IntPtr Owner()
    {
        if (_owner == IntPtr.Zero)
            _owner = CreateWindowExW(WS_EX_TOOLWINDOW, "STATIC", "Curvy tray menu", WS_POPUP,
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        return _owner;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);
    [DllImport("user32.dll", SetLastError = true)] static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll", SetLastError = true)]
    static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string? name);
}

/// <summary>
/// Dark/light native menus. Windows has no public API for this; Explorer, Notepad++ and others use
/// uxtheme's unnamed exports SetPreferredAppMode (ordinal 135) and FlushMenuThemes (136), available
/// since Windows 10 1903. Looked up by ordinal and skipped if missing, so the worst case is a light menu.
/// </summary>
static class MenuTheme
{
    enum PreferredAppMode { Default = 0, AllowDark = 1, ForceDark = 2, ForceLight = 3 }

    delegate PreferredAppMode SetPreferredAppModeFn(PreferredAppMode mode);
    delegate void FlushMenuThemesFn();

    static bool _loaded;
    static SetPreferredAppModeFn? _setMode;
    static FlushMenuThemesFn? _flush;
    static bool? _lastDark;

    public static void Apply(bool dark)
    {
        Load();
        if (_setMode is null || _lastDark == dark) return;
        try
        {
            _setMode(dark ? PreferredAppMode.ForceDark : PreferredAppMode.ForceLight);
            _flush?.Invoke();
            _lastDark = dark;
        }
        catch (Exception)
        {
            _setMode = null; // unexpected export: stay on the default (light) menus
        }
    }

    static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var uxtheme = LoadLibraryExW("uxtheme.dll", IntPtr.Zero, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (uxtheme == IntPtr.Zero) return;
        if (GetProcAddress(uxtheme, (IntPtr)135) is var set && set != IntPtr.Zero)
            _setMode = Marshal.GetDelegateForFunctionPointer<SetPreferredAppModeFn>(set);
        if (GetProcAddress(uxtheme, (IntPtr)136) is var flush && flush != IntPtr.Zero)
            _flush = Marshal.GetDelegateForFunctionPointer<FlushMenuThemesFn>(flush);
    }

    const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x800;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadLibraryExW(string name, IntPtr file, uint flags);
    // Ordinal lookup: lpProcName is the ordinal in the low word, so it's passed as a pointer value.
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);
}
