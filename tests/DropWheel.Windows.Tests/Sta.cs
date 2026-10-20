using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DropWheel.Windows.Tests;

/// <summary>WPF живёт только в STA-потоке: выполнить код там и дождаться.</summary>
internal static class Sta
{
    public static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AggregateException(error);
    }
}

/// <summary>
/// Снимок элемента в PNG (без показа на экране): для глаз разработчика и для README.
/// Папка — DROPWHEEL_SCREENSHOTS (CI выкладывает её артефактом), иначе bin/…/screenshots.
/// </summary>
internal static class Screenshot
{
    public static string Dir
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("DROPWHEEL_SCREENSHOTS") is { Length: > 0 } env
                ? env
                : Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <param name="scale">2 — как на экране 200%: чётче для README.</param>
    public static string Save(FrameworkElement element, double width, double height, string name,
        Brush? background = null, double scale = 2)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        if (background is not null)
        {
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen()) dc.DrawRectangle(background, null, new Rect(0, 0, width, height));
            bitmap.Render(bg);
        }
        bitmap.Render(element);

        var path = Path.Combine(Dir, name + ".png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }
}
