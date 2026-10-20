using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DropWheel.Shell;

/// <summary>
/// Миниатюры файлов, как в проводнике (IShellItemImageFactory): превью фото/видео или значок типа.
/// Всё — в отдельном STA-потоке (расширения оболочки бывают медленными и требуют STA), интерфейс не ждёт.
/// Любая ошибка → null: вызывающий рисует свой значок.
/// </summary>
internal static class ShellThumbnails
{
    private const int SiigbfResizeToFit = 0x0;
    private const int SiigbfThumbnailOnly = 0x8;

    private static readonly object Gate = new();
    private static Dispatcher? _worker;

    /// <summary>Миниатюра (или значок типа файла) размером не больше sizePx; готовая (Frozen) картинка.</summary>
    public static Task<BitmapSource?> GetAsync(string path, int sizePx, bool thumbnailOnly = false) =>
        Run(() => Load(path, sizePx, thumbnailOnly));

    /// <summary>
    /// Картинка для уведомления: превью файла на подложке 2:1 (как «героическая» картинка Windows),
    /// сохранённое в JPEG во временной папке. null — у файла нет превью (не картинка/видео) или ошибка.
    /// </summary>
    public static Task<string?> SaveToastPreviewAsync(string path) => Run(() =>
    {
        if (!File.Exists(path)) return null;
        var thumb = Load(path, 720, thumbnailOnly: true);
        return thumb is null ? null : SavePreview(thumb);
    });

    private static Task<T?> Run<T>(Func<T?> work)
    {
        var tcs = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Worker().BeginInvoke(DispatcherPriority.Background, () =>
            {
                try { tcs.TrySetResult(work()); }
                catch (Exception) { tcs.TrySetResult(default); }
            });
        }
        catch (Exception)
        {
            tcs.TrySetResult(default);
        }
        return tcs.Task;
    }

    private static Dispatcher Worker()
    {
        lock (Gate)
        {
            if (_worker is not null) return _worker;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                _worker = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "DropWheel: миниатюры",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();
            return _worker!;
        }
    }

    /// <summary>Вызывать только в STA-потоке.</summary>
    private static BitmapSource? Load(string path, int sizePx, bool thumbnailOnly)
    {
        if (string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path))) return null;
        IShellItemImageFactory? factory = null;
        var hbitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory) != 0 || factory is null) return null;
            var flags = SiigbfResizeToFit | (thumbnailOnly ? SiigbfThumbnailOnly : 0);
            if (factory.GetImage(new NativeSize { Width = sizePx, Height = sizePx }, flags, out hbitmap) != 0
                || hbitmap == IntPtr.Zero)
                return null;
            return FromHBitmap(hbitmap);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (hbitmap != IntPtr.Zero) DeleteObject(hbitmap);
            if (factory is not null) Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>
    /// HBITMAP → BitmapSource с прозрачностью (стандартный CreateBitmapSourceFromHBitmap её теряет).
    /// Пиксели забираются сверху вниз в 32 бита; если альфа везде 0 — картинка без прозрачности.
    /// </summary>
    private static BitmapSource? FromHBitmap(IntPtr hbitmap)
    {
        if (GetObject(hbitmap, Marshal.SizeOf<NativeBitmap>(), out var bm) == 0 || bm.Width <= 0 || bm.Height <= 0) return null;
        var width = bm.Width;
        var height = bm.Height;
        var pixels = new byte[width * height * 4];
        var info = new BitmapInfoHeader
        {
            Size = 40, // sizeof(BITMAPINFOHEADER), без таблицы цветов
            Width = width,
            Height = -height, // сверху вниз
            Planes = 1,
            BitCount = 32,
        };
        var dc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, hbitmap, 0, (uint)height, pixels, ref info, 0) == 0) return null;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }

        var hasAlpha = false;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] == 0) continue;
            hasAlpha = true;
            break;
        }
        var format = hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32;
        var source = BitmapSource.Create(width, height, 96, 96, format, null, pixels, width * 4);
        source.Freeze();
        return source;
    }

    /// <summary>Превью на подложке 2:1: размытая увеличенная копия + картинка целиком по центру.</summary>
    private static string? SavePreview(BitmapSource thumb)
    {
        const int width = 728, height = 364;
        var grid = new Grid { Width = width, Height = height, Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)) };
        grid.Children.Add(new Image
        {
            Source = thumb,
            Stretch = Stretch.UniformToFill,
            Opacity = 0.55,
            Effect = new BlurEffect { Radius = 36 },
        });
        grid.Children.Add(new Image { Source = thumb, Stretch = Stretch.Uniform, Margin = new Thickness(0, 10, 0, 10) });
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        // JPEG, а не PNG: у картинок уведомлений есть предел размера файла (на старых Windows 10 — 200 КБ).
        var encoder = new JpegBitmapEncoder { QualityLevel = 82 };
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0)));

        var dir = Path.Combine(Path.GetTempPath(), "DropWheel", "previews");
        Directory.CreateDirectory(dir);
        CleanOld(dir);
        var file = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".jpg");
        using (var stream = File.Create(file)) encoder.Save(stream);
        return file;
    }

    /// <summary>Превью нужны уведомлениям в центре уведомлений, поэтому живут пару дней.</summary>
    private static void CleanOld(string dir)
    {
        try
        {
            var border = DateTime.UtcNow.AddDays(-2);
            foreach (var f in Directory.EnumerateFiles(dir, "*.jpg"))
                if (File.GetLastWriteTimeUtc(f) < border) File.Delete(f);
        }
        catch (Exception)
        {
        }
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
        // BITMAPINFO: место под таблицу цветов (для 32 бит BI_RGB не используется).
        public uint Colors0;
        public uint Colors1;
        public uint Colors2;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? item);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static extern int GetObject(IntPtr obj, int size, out NativeBitmap bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits,
        ref BitmapInfoHeader info, uint usage);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
}
