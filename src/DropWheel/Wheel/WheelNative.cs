using System.Runtime.InteropServices;

namespace DropWheel.Wheel;

/// <summary>Win32 для размещения колеса: монитор под точкой, его DPI и рабочая область.</summary>
internal static class WheelNative
{
    public static readonly IntPtr HwndTopmost = new(-1);

    public const uint SwpNoActivate = 0x0010;
    public const uint SwpNoOwnerZOrder = 0x0200;

    private const uint MonitorDefaultToNearest = 2;
    private const int MdtEffectiveDpi = 0;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x, Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    /// <summary>Рабочая область (без панели задач) и эффективный DPI монитора под точкой (физические пиксели).</summary>
    public static (Rect Work, uint Dpi) MonitorAt(int x, int y)
    {
        var mon = MonitorFromPoint(new NativePoint(x, y), MonitorDefaultToNearest);

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var work = mon != IntPtr.Zero && GetMonitorInfo(mon, ref info)
            ? info.Work
            : new Rect { Left = int.MinValue / 2, Top = int.MinValue / 2, Right = int.MaxValue / 2, Bottom = int.MaxValue / 2 };

        var dpi = mon != IntPtr.Zero && GetDpiForMonitor(mon, MdtEffectiveDpi, out var dx, out _) == 0 && dx > 0
            ? dx
            : 96u;
        return (work, dpi);
    }

    /// <summary>Позиция курсора в физических пикселях; null — не удалось.</summary>
    public static (int X, int Y)? CursorPosition()
    {
        try
        {
            return GetCursorPos(out var p) ? (p.X, p.Y) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Вывести окно на передний план (для режима клика). Ошибки не важны: Activate всё равно вызывается.</summary>
    public static void TrySetForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try { SetForegroundWindow(hwnd); }
        catch (Exception) { }
    }

    /// <summary>Убрать окно из Alt+Tab.</summary>
    public static void MakeToolWindow(IntPtr hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style | WsExToolWindow));
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint pt, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint pt);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
}
