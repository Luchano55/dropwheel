using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using DropWheel.App;
using DropWheel.Conversions;
using DropWheel.Localization;
using DropWheel.Ui;

namespace DropWheel.Windows.Tests;

/// <summary>
/// Окно «Обрезать видео» → PNG, светлая и тёмная тема. Кадры — нарисованные картинки (не ffmpeg),
/// длительность и выбранный кусок — фиксированные.
/// </summary>
public class TrimScreenshots
{
    private const double Duration = 83.4;

    /// <summary>«Кадр из ролика»: небо, солнце, холмы; t (0…1) сдвигает солнце и цвет неба — миниатюры различаются.</summary>
    internal static ImageSource FakeFrame(double t, int width = 640, int height = 360)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var sky = new LinearGradientBrush(
                Color.FromRgb((byte)(40 + 180 * t), (byte)(90 + 60 * t), (byte)(200 - 90 * t)),
                Color.FromRgb(255, (byte)(190 - 60 * t), (byte)(120 - 40 * t)), 90);
            dc.DrawRectangle(sky, null, new Rect(0, 0, width, height));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 236, 170)), null,
                new Point(width * (0.15 + 0.7 * t), height * (0.25 + 0.3 * t)), height * 0.09, height * 0.09);
            var hills = new StreamGeometry();
            using (var g = hills.Open())
            {
                g.BeginFigure(new Point(0, height * 0.72), true, true);
                g.BezierTo(new Point(width * 0.25, height * 0.55), new Point(width * 0.45, height * 0.85), new Point(width * 0.7, height * 0.66), true, false);
                g.BezierTo(new Point(width * 0.85, height * 0.58), new Point(width * 0.95, height * 0.7), new Point(width, height * 0.64), true, false);
                g.LineTo(new Point(width, height), true, false);
                g.LineTo(new Point(0, height), true, false);
            }
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x2E, 0x5E, 0x3A)), null, hills);
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    internal static TrimView Make(bool light, bool exact = false)
    {
        var view = new TrimView(light ? SettingsPalette.Light() : SettingsPalette.Dark(), "Отпуск 2026 — море.mp4");
        view.SetDuration(Duration);
        view.Range = new TrimRange(Duration).WithStart(12.4).WithEnd(47.8);
        view.ActiveHandle = TrimHandle.Start;
        for (var i = 0; i < TrimView.ThumbnailCount; i++)
            view.SetThumbnail(i, FakeFrame(i / (double)(TrimView.ThumbnailCount - 1), 160, 90));
        view.SetFrame(FakeFrame(12.4 / Duration), 12.4);
        if (exact) view.Exact = true;
        return view;
    }

    /// <summary>Окно с заголовком Windows 11 поверх обоев (как у снимков окна настроек).</summary>
    internal static void Save(TrimView view, bool light, string name)
    {
        const double margin = 28, titleHeight = 32;
        view.Measure(new Size(TrimView.DefaultWidth, double.PositiveInfinity));
        var w = TrimView.DefaultWidth;
        var h = Math.Ceiling(view.DesiredSize.Height) + titleHeight;
        var sceneW = w + 2 * margin;
        var sceneH = h + 2 * margin;
        var palette = view.Palette;

        var grid = new Grid { Width = sceneW, Height = sceneH };
        grid.Children.Add(Scenes.Wallpaper(sceneW, sceneH, light));
        grid.Children.Add(new Border
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(8), Background = Brushes.Black,
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.35 },
        });
        var frame = new DockPanel
        {
            Width = w, Height = h, Background = new SolidColorBrush(palette.Solid),
            Clip = new RectangleGeometry(new Rect(0, 0, w, h), 8, 8),
        };
        var title = new Grid { Height = titleHeight };
        title.Children.Add(new TextBlock
        {
            Text = Loc.T.TrimTitle, FontFamily = UiFonts.Text, FontSize = 12, Foreground = palette.Text,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0),
        });
        title.Children.Add(new TextBlock
        {
            Text = "", FontFamily = UiFonts.Icons, FontSize = 10, Foreground = palette.Text, Width = 46,
            HorizontalAlignment = HorizontalAlignment.Right, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        DockPanel.SetDock(title, Dock.Top);
        frame.Children.Add(title);
        frame.Children.Add(view);
        grid.Children.Add(frame);
        grid.Children.Add(new Border
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(light ? Color.FromArgb(0x1F, 0, 0, 0) : Color.FromArgb(0x33, 0x75, 0x75, 0x75)),
            IsHitTestVisible = false,
        });
        var path = Screenshot.Save(grid, sceneW, sceneH, name, scale: 1.5);
        Assert.True(new FileInfo(path).Length > 5000);
    }

    [Theory]
    [InlineData(true, "trim-light")]
    [InlineData(false, "trim-dark")]
    public void Trim_window(bool light, string name)
    {
        Sta.Run(() => Save(Make(light), light, name));
    }

    /// <summary>Пока ffmpeg читает видео: «Читаю видео…», «Обрезать» недоступна; не прочиталось — причина.</summary>
    [Fact]
    public void Trim_window_loading_and_failed()
    {
        Sta.Run(() =>
        {
            var loading = new TrimView(SettingsPalette.Dark(), "тест 1.mkv");
            Assert.False(loading.CanTrim);
            Save(loading, light: false, "trim-dark-loading");

            var failed = new TrimView(SettingsPalette.Light(), "тест 1.mkv");
            failed.SetFailed(Loc.T.TrimFailed);
            Assert.False(failed.CanTrim);
            Save(failed, light: true, "trim-light-failed");
        });
    }

    [Fact]
    public void Trim_button_gives_range_and_exact_flag()
    {
        Sta.Run(() =>
        {
            var view = Make(light: true);
            (TrimRange Range, bool Exact)? got = null;
            view.TrimRequested += (r, e) => got = (r, e);
            view.RequestTrim();
            Assert.NotNull(got);
            Assert.Equal(12.4, got!.Value.Range.Start, 6);
            Assert.Equal(47.8, got.Value.Range.End, 6);
            Assert.False(got.Value.Exact);

            // Весь ролик — обрезать нечего, кнопка не срабатывает.
            view.Range = new TrimRange(Duration);
            got = null;
            view.RequestTrim();
            Assert.Null(got);
        });
    }
}

/// <summary>Окно обрезки по-английски, с галочкой «точно по кадру» (язык меняется — в коллекции языка).</summary>
[Collection(LanguageCollection.Name)]
public class TrimEnglishScreenshots
{
    [Fact]
    public void Trim_window_exact_in_english()
    {
        Sta.Run(() =>
        {
            using var _ = Loc.Use(UiLanguage.En);
            TrimScreenshots.Save(TrimScreenshots.Make(light: true, exact: true), light: true, "trim-light-en-exact");
        });
    }
}
