using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DropWheel.Capture;
using DropWheel.Localization;
using DropWheel.Ui;

namespace DropWheel.Windows.Tests;

/// <summary>
/// Запись экрана (D-RECORD) → PNG поверх нарисованного «рабочего стола» снимка (<see cref="CaptureScreenshots.FakeDesktop"/>):
/// оверлей выделения в режиме записи (панель «MP4 / GIF · Записать») и сама запись — красная рамка и пилюля «● 00:12 Стоп».
/// </summary>
public class RecordScreenshots
{
    private const double W = CaptureScreenshots.W, H = CaptureScreenshots.H, Scale = CaptureScreenshots.Scale;

    /// <summary>Заголовок и список из окна «Заметки».</summary>
    private static readonly DipRect Selection = new(262, 160, 420, 196);

    internal static CaptureView MakeOverlay(bool light, RecordFormat format)
    {
        var view = new CaptureView(light ? SettingsPalette.Light() : SettingsPalette.Dark(), ocr: false, record: format)
        {
            Width = W, Height = H, Frame = CaptureScreenshots.FakeDesktop(light),
        };
        var size = new PixelRect(0, 0, (int)(Selection.Width * Scale), (int)(Selection.Height * Scale));
        view.Show(Selection, CaptureLayout.SizeText(size), toolbar: true, hint: false);
        Assert.Equal(format, view.RecordMode);
        return view;
    }

    /// <summary>Идёт запись: живой «рабочий стол», рамка в 2 физических пикселя по краю области и пилюля под ней.</summary>
    internal static FrameworkElement MakeRecording(bool light)
    {
        var canvas = new Canvas { Width = W, Height = H };
        canvas.Children.Add(new Image { Source = CaptureScreenshots.FakeDesktop(light), Width = W, Height = H, Stretch = Stretch.Fill });

        var px = 1 / Scale;
        var frame = new Border
        {
            Width = Selection.Width, Height = Selection.Height,
            BorderBrush = new SolidColorBrush(RecordFrameWindow.FrameColor),
            BorderThickness = new Thickness(RecordFrameWindow.Thickness * px),
        };
        Canvas.SetLeft(frame, Selection.X);
        Canvas.SetTop(frame, Selection.Y);
        canvas.Children.Add(frame);

        var pill = new RecordPillView(light ? SettingsPalette.Light() : SettingsPalette.Dark())
        {
            Elapsed = TimeSpan.FromSeconds(12),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Direction = 270, Opacity = 0.3 },
        };
        pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var (x, y) = CaptureLayout.Toolbar(Selection, pill.DesiredSize.Width, pill.DesiredSize.Height, new DipRect(0, 0, W, H));
        Canvas.SetLeft(pill, Math.Round(x));
        Canvas.SetTop(pill, Math.Round(y));
        canvas.Children.Add(pill);
        return canvas;
    }

    internal static void Save(FrameworkElement view, string name)
    {
        var path = Screenshot.Save(view, W, H, name, scale: Scale);
        Assert.True(new FileInfo(path).Length > 20000);
    }

    [Fact]
    public void Record_overlay_with_toolbar()
    {
        Sta.Run(() => Save(MakeOverlay(light: true, RecordFormat.Mp4), "record-light"));
    }

    [Fact]
    public void Record_overlay_dark_gif()
    {
        Sta.Run(() => Save(MakeOverlay(light: false, RecordFormat.Gif), "record-dark-gif"));
    }

    [Fact]
    public void Recording_frame_and_pill()
    {
        Sta.Run(() => Save(MakeRecording(light: true), "recording-light"));
    }

    [Fact]
    public void Pill_shows_elapsed_time()
    {
        Sta.Run(() =>
        {
            var pill = new RecordPillView(SettingsPalette.Light()) { Elapsed = TimeSpan.FromSeconds(3723) };
            Assert.Contains(Texts(pill), t => t == "1:02:03");
            Assert.Contains(Texts(pill), t => t == "Стоп");
        });
    }

    internal static IEnumerable<string> Texts(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is TextBlock { Text: { } text }) yield return text;
            foreach (var t in Texts(child)) yield return t;
        }
    }
}

/// <summary>Оверлей записи по-английски (язык меняется — в коллекции языка).</summary>
[Collection(LanguageCollection.Name)]
public class RecordEnglishScreenshots
{
    [Fact]
    public void Record_overlay_in_english()
    {
        Sta.Run(() =>
        {
            using var _ = Loc.Use(UiLanguage.En);
            var view = RecordScreenshots.MakeOverlay(light: true, RecordFormat.Mp4);
            RecordScreenshots.Save(view, "record-light-en");
            Assert.Contains(RecordScreenshots.Texts(view), t => t.EndsWith("Record", StringComparison.Ordinal));
        });
    }
}
