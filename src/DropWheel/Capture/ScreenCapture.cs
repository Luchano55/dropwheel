using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DropWheel.Capture;

/// <summary>
/// Снимок экрана в физических пикселях (решение D-CAPTURE). Приложение — Per-Monitor V2 (app.manifest), поэтому
/// координаты мониторов, окон и BitBlt — настоящие пиксели каждого монитора, при любом масштабе.
/// Весь виртуальный экран копируется одним BitBlt в DIB и сразу переносится в замороженный BitmapSource;
/// DIB освобождается — в памяти остаётся одна копия, до закрытия оверлея.
/// </summary>
internal static class ScreenCapture
{
    /// <summary>Снимок: картинка виртуального экрана, его границы, мониторы с DPI и рамки окон сверху вниз.</summary>
    public sealed record Frame(BitmapSource Image, PixelRect Bounds, IReadOnlyList<CaptureMonitor> Monitors, IReadOnlyList<PixelRect> Windows);

    /// <summary>Мониторы, затем окна, затем пиксели — до того, как появится оверлей (стоп-кадр).</summary>
    public static Frame Take()
    {
        var monitors = Monitors();
        if (monitors.Count == 0) throw new InvalidOperationException("Мониторы не найдены");
        var bounds = monitors.Aggregate(default(PixelRect), (a, m) => a.Union(m.Bounds));
        var windows = SafeWindows();
        var image = Grab(bounds);
        return new Frame(image, bounds, monitors, windows);
    }

    /// <summary>
    /// Часть снимка (координаты экрана) — отдельная замороженная копия пикселей для буфера или PNG:
    /// она не держит в памяти снимок всего экрана.
    /// </summary>
    public static BitmapSource Crop(Frame frame, PixelRect rect)
    {
        var r = rect.Intersect(frame.Bounds);
        if (r.IsEmpty) throw new ArgumentException("Область за пределами снимка", nameof(rect));
        var stride = r.Width * 4;
        var pixels = new byte[(long)stride * r.Height];
        frame.Image.CopyPixels(new Int32Rect(r.X - frame.Bounds.X, r.Y - frame.Bounds.Y, r.Width, r.Height), pixels, stride, 0);
        var image = BitmapSource.Create(r.Width, r.Height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
        image.Freeze();
        return image;
    }

    /// <summary>PNG — в новый файл (CreateNew: существующий не перезаписывается; путь уже свободный).</summary>
    public static void SavePng(BitmapSource image, string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(file);
        }
        catch
        {
            try { File.Delete(path); }
            catch (Exception) { }
            throw;
        }
    }

    // ---------- пиксели ----------

    private static BitmapSource Grab(PixelRect r)
    {
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) throw new InvalidOperationException("GetDC не удался");
        IntPtr memory = IntPtr.Zero, dib = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            memory = CreateCompatibleDC(screen);
            var info = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = r.Width,
                Height = -r.Height, // сверху вниз
                Planes = 1,
                BitCount = 32,
            };
            dib = CreateDIBSection(screen, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero) throw new InvalidOperationException("Не хватило памяти для снимка экрана");
            old = SelectObject(memory, dib);
            // CAPTUREBLT — вместе со слоистыми (полупрозрачными) окнами.
            if (!BitBlt(memory, 0, 0, r.Width, r.Height, screen, r.X, r.Y, SrcCopy | CaptureBlt))
                throw new InvalidOperationException("BitBlt не удался: " + Marshal.GetLastWin32Error());
            GdiFlush();
            var stride = r.Width * 4;
            var image = BitmapSource.Create(r.Width, r.Height, 96, 96, PixelFormats.Bgr32, null, bits, stride * r.Height, stride);
            image.Freeze();
            return image;
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(memory, old);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    // ---------- мониторы ----------

    public static IReadOnlyList<CaptureMonitor> Monitors()
    {
        var list = new List<CaptureMonitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (mon, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(mon, ref info))
            {
                var dpi = GetDpiForMonitor(mon, 0, out var dx, out _) == 0 && dx > 0 ? dx : 96u;
                var b = info.Monitor;
                list.Add(new CaptureMonitor(PixelRect.FromEdges(b.Left, b.Top, b.Right, b.Bottom), dpi));
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    // ---------- окна (для щелчка по окну) ----------

    private static IReadOnlyList<PixelRect> SafeWindows()
    {
        try { return Windows(); }
        catch (Exception) { return []; } // без окон — щелчок выберет монитор
    }

    /// <summary>
    /// Видимые окна верхнего уровня сверху вниз: не свёрнутые, не «спрятанные» DWM (другие рабочие столы, приостановленные
    /// приложения Store), без рабочего стола (Progman/WorkerW) и окон этой программы. Рамка — без тени (EXTENDED_FRAME_BOUNDS).
    /// </summary>
    private static List<PixelRect> Windows()
    {
        var result = new List<PixelRect>();
        var pid = (uint)Environment.ProcessId;
        var name = new StringBuilder(64);
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner == pid) return true;
            if (DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            name.Clear();
            GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() is "Progman" or "WorkerW") return true;
            var rect = DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out NativeRect frame, Marshal.SizeOf<NativeRect>()) == 0
                ? frame
                : GetWindowRect(hwnd, out var plain) ? plain : default;
            var r = PixelRect.FromEdges(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (r.Width >= 8 && r.Height >= 8) result.Add(r);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    // ---------- Win32 ----------

    private const uint SrcCopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000;
    private const int DwmwaExtendedFrameBounds = 9;
    private const int DwmwaCloaked = 14;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);
    private delegate bool WindowEnumProc(IntPtr hwnd, IntPtr data);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GdiFlush();

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(WindowEnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out NativeRect value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
