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
        // Светлая тема — тёплые оранжевые обои (как в образце Tangerine), тёмная — сине-фиолетовые.
        canvas.Background = new LinearGradientBrush(
            light ? Color.FromRgb(0xFF, 0x7E, 0x45) : Color.FromRgb(0x10, 0x2A, 0x4F),
            light ? Color.FromRgb(0xFF, 0xC8, 0x7A) : Color.FromRgb(0x4A, 0x1E, 0x4D), 35);

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
        Blob(width * 0.85, height * 0.7, width * 0.4, light ? Color.FromArgb(0xB0, 0xE8, 0x4A, 0x2A) : Color.FromArgb(0xB0, 0xC2, 0x3A, 0x8E));
        Blob(width * 0.6, height * 0.05, width * 0.25, light ? Color.FromArgb(0x90, 0xFF, 0xF0, 0xC0) : Color.FromArgb(0x90, 0xF5, 0x9E, 0x2D));

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
    /// Окно задач у трея поверх обоев: скруглённые углы и тень, как у всплывающих окон Windows 11.
    /// overBackdrop — под окном размытые обои (системная акриловая подложка Windows 11 22H2+).
    /// </summary>
    public static (FrameworkElement Scene, double Width, double Height) Flyout(FrameworkElement view, bool light, bool overBackdrop)
    {
        const double margin = 24;
        view.Measure(new Size(view.Width, double.PositiveInfinity));
        var w = view.Width;
        var h = Math.Ceiling(view.DesiredSize.Height);
        var sceneW = w + 2 * margin;
        var sceneH = h + 2 * margin;

        var grid = new Grid { Width = sceneW, Height = sceneH };
        grid.Children.Add(Wallpaper(sceneW, sceneH, light));

        var frame = new Grid
        {
            Width = w, Height = h,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Clip = new RectangleGeometry(new Rect(0, 0, w, h), 8, 8),
        };
        if (overBackdrop)
        {
            var blurred = Wallpaper(sceneW, sceneH, light);
            blurred.Effect = new BlurEffect { Radius = 40, KernelType = KernelType.Gaussian };
            blurred.Margin = new Thickness(-margin, -margin, 0, 0);
            blurred.HorizontalAlignment = HorizontalAlignment.Left;
            blurred.VerticalAlignment = VerticalAlignment.Top;
            frame.Children.Add(blurred);
            // Подкраска самой подложки Windows (акрил: свой полупрозрачный тон).
            frame.Children.Add(new Border { Background = new SolidColorBrush(light ? Color.FromArgb(0x99, 0xF3, 0xF3, 0xF3) : Color.FromArgb(0xAA, 0x20, 0x20, 0x20)) });
        }
        frame.Children.Add(view);

        // Тень и тонкая рамка Windows 11.
        var shadow = new Border
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(8),
            Background = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.35 },
        };
        var border = new Border
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(light ? Color.FromArgb(0x1F, 0, 0, 0) : Color.FromArgb(0x33, 0x75, 0x75, 0x75)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        grid.Children.Add(shadow);
        grid.Children.Add(frame);
        grid.Children.Add(border);
        return (grid, sceneW, sceneH);
    }

    /// <summary>
    /// Колесо поверх обоев. Translucent — под диском размытая копия обоев (так выглядит системный акрил),
    /// Solid — просто обои (так колесо выглядит без эффектов прозрачности).
    /// </summary>
    public static FrameworkElement Wheel(WheelControl wheel, bool light, double size = WheelWindow.DipSize)
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
