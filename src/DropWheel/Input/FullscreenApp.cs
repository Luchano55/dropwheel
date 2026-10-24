using System.Runtime.InteropServices;
using System.Text;

namespace DropWheel.Input;

/// <summary>Развёрнуто ли сейчас на весь экран приложение под фокусом (см. <see cref="FullscreenRule"/>). Быстро: пара вызовов user32.</summary>
internal static class FullscreenApp
{
    public static bool IsActive()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect)) return false;
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return false;
            var name = new StringBuilder(64);
            var cls = GetClassName(hwnd, name, name.Capacity) > 0 ? name.ToString() : null;
            var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
            return FullscreenRule.IsFullscreenApp(cls, style, (rect.Left, rect.Top, rect.Right, rect.Bottom),
                (info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom));
        }
        catch
        {
            return false;
        }
    }

    private const uint MonitorDefaultToNearest = 2;
    private const int GwlStyle = -16;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
}
