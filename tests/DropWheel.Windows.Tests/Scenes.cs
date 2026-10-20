using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using DropWheel.Ui;
using DropWheel.Wheel;

namespace DropWheel.Windows.Tests;

internal static class TestSetup
{
    /// <summary>Скриншоты — конечные кадры: анимации выключены на всю сборку тестов.</summary>
    [ModuleInitializer]
    internal static void DisableAnimations() => Motion.ForceDisabled = true;
}

/// <summary>
/// Фон «рабочего стола» для скриншотов: колесо и окна показываются поверх чего-то пёстрого,
/// чтобы было видно прозрачность. Системное размытие (акрил) имитируется размытой копией фона под диском.
/// </summary>
internal static class Scenes
{
    public static Brush Plain(bool light) =>
        new SolidColorBrush(light ? Color.FromRgb(0xE9, 0xEE, 0xF5) : Color.FromRgb(0x1B, 0x1F, 0x27));

    /// <summary>Картинка-обои: градиент, мягкие пятна и несколько «значков» с подписями.</summary>
    public static FrameworkElement Wallpaper(double width, double height, bool light)
    {
        var canvas = new Canvas { Width = width, Height = height, ClipToBounds = true };
        canvas.Background = new LinearGradientBrush(
            light ? Color.FromRgb(0xB9, 0xD4, 0xF5) : Color.FromRgb(0x10, 0x2A, 0x4F),
            light ? Color.FromRgb(0xF3, 0xC9, 0xD9) : Color.FromRgb(0x4A, 0x1E, 0x4D), 35);

        void Blob(double x, double y, double r, Color c)
        {
            var e = new Ellipse
            {
                Width = 2 * r, Height = 2 * r,
                Fill = new RadialGradientBrush(c, Color.FromArgb(0, c.R, c.G, c.B)),
            };
            Canvas.SetLeft(e, x - r);
            Canvas.SetTop(e, y - r);
            canvas.Children.Add(e);
        }

        Blob(width * 0.2, height * 0.25, width * 0.35, light ? Color.FromArgb(0xC0, 0xFF, 0xE0, 0x8A) : Color.FromArgb(0xB0, 0x1F, 0x8E, 0xD8));
        Blob(width * 0.85, height * 0.7, width * 0.4, light ? Color.FromArgb(0xB0, 0x7C, 0xB8, 0xFF) : Color.FromArgb(0xB0, 0xC2, 0x3A, 0x8E));
        Blob(width * 0.6, height * 0.05, width * 0.25, light ? Color.FromArgb(0x90, 0xFF, 0x9E, 0xB5) : Color.FromArgb(0x90, 0xF5, 0x9E, 0x2D));

        // «Значки на рабочем столе» — чтобы размытие было заметно.
        var names = new[] { "Отпуск", "IMG_2041.HEIC", "Договор.pdf", "Видео", "song.flac", "Скриншот" };
        for (var i = 0; i < names.Length; i++)
        {
            var x = 14 + (i % 2) * (width - 92);
            var y = 14 + (i / 2) * (height / 3.2);
            var icon = new Border
            {
                Width = 46, Height = 38, CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(i % 3 == 0 ? Color.FromRgb(0xF6, 0xC3, 0x4A)
                    : i % 3 == 1 ? Color.FromRgb(0x5B, 0xA8, 0xF5) : Color.FromRgb(0xE8, 0x5D, 0x5D)),
            };
            Canvas.SetLeft(icon, x + 10);
            Canvas.SetTop(icon, y);
            canvas.Children.Add(icon);
            var label = new TextBlock
            {
                Text = names[i], Width = 66, TextAlignment = TextAlignment.Center, FontSize = 11,
                Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"),
                Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.7 },
            };
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, y + 42);
            canvas.Children.Add(label);
        }
        return canvas;
    }

    /// <summary>
    /// Колесо поверх обоев. Translucent — под диском размытая копия обоев (так выглядит системный акрил),
    /// Solid — просто обои (так колесо выглядит без эффектов прозрачности).
    /// </summary>
    public static FrameworkElement Wheel(WheelControl wheel, bool light, double size = 300)
    {
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(Wallpaper(size, size, light));
        if (wheel.Backdrop == WheelBackdrop.Translucent)
        {
            var blurred = Wallpaper(size, size, light);
            blurred.Effect = new BlurEffect { Radius = 28, KernelType = KernelType.Gaussian };
            var holder = new Grid
            {
                Width = size, Height = size,
                Clip = new EllipseGeometry(new Point(size / 2, size / 2), WheelControl.DiscRadius, WheelControl.DiscRadius),
            };
            holder.Children.Add(blurred);
            grid.Children.Add(holder);
        }
        wheel.HorizontalAlignment = HorizontalAlignment.Center;
        wheel.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(wheel);
        return grid;
    }
}
