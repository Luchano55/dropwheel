using System.Runtime.InteropServices;

namespace DropWheel.Wheel;

/// <summary>Win32 для окон колеса и окна задач: монитор под точкой, его DPI и рабочая область, стили окон.</summary>
internal static class WheelNative
{
    public static readonly IntPtr HwndTopmost = new(-1);

    public const uint SwpNoActivate = 0x0010;
    public const uint SwpNoOwnerZOrder = 0x0200;

    private const uint MonitorDefaultToNearest = 2;
    private const int MdtEffectiveDpi = 0;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExTransparent = 0x00000020;
    private const long WsExNoActivate = 0x08000000;
    private const long WsSysMenu = 0x00080000;
    private const long WsMinimizeBox = 0x00020000;
    private const long WsMaximizeBox = 0x00010000;

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

    private const uint AbmGetTaskbarPos = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int Size;
        public IntPtr Window;
        public uint CallbackMessage;
        public uint Edge;
        public Rect Rect;
        public IntPtr Param;
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

    /// <summary>Рабочая область, границы и эффективный DPI монитора под точкой (физические пиксели).</summary>
    public static (Rect Work, Rect Bounds, uint Dpi) MonitorAreasAt(int x, int y)
    {
        var (work, dpi) = MonitorAt(x, y);
        var bounds = work;
        try
        {
            var mon = MonitorFromPoint(new NativePoint(x, y), MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref info)) bounds = info.Monitor;
        }
        catch (Exception)
        {
        }
        return (work, bounds, dpi);
    }

    /// <summary>Прямоугольник панели задач (основной), физические пиксели; null — не удалось.</summary>
    public static Rect? TaskbarRect()
    {
        try
        {
            var data = new AppBarData { Size = Marshal.SizeOf<AppBarData>() };
            return SHAppBarMessage(AbmGetTaskbarPos, ref data) != UIntPtr.Zero ? data.Rect : null;
        }
        catch (Exception)
        {
            return null;
        }
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
    public static void MakeToolWindow(IntPtr hwnd) => AddExStyle(hwnd, WsExToolWindow);

    /// <summary>
    /// Окно-«картинка»: не в Alt+Tab, не активируется, мышь и перетаскивание проходят сквозь него
    /// (WS_EX_TRANSPARENT у слоистого окна).
    /// </summary>
    public static void MakeClickThrough(IntPtr hwnd) =>
        AddExStyle(hwnd, WsExToolWindow | WsExTransparent | WsExNoActivate);

    /// <summary>Убрать у окна системное меню и кнопки свернуть/развернуть (их не рисует DWM на «стекле»).</summary>
    public static void RemoveSystemMenu(IntPtr hwnd)
    {
        try
        {
            var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
            SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(style & ~(WsSysMenu | WsMinimizeBox | WsMaximizeBox)));
        }
        catch (Exception)
        {
        }
    }

    private static void AddExStyle(IntPtr hwnd, long add)
    {
        try
        {
            var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style | add));
        }
        catch (Exception)
        {
        }
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

    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
}
