using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DropWheel.App;
using DropWheel.Capture;
using DropWheel.Localization;
using DropWheel.Settings;
using DropWheel.Ui;

namespace DropWheel.Windows.Tests;

/// <summary>
/// Снимок экрана (D-CAPTURE) → PNG: оверлей поверх нарисованного «рабочего стола» (обои, окно с текстом, панель задач)
/// с выделенной областью, подписью размера и панелью кнопок; светлая и тёмная тема, английский.
/// Подпись — в физических пикселях, как на экране 150 % (DIP × 1.5).
/// </summary>
public class CaptureScreenshots
{
    internal const double W = 1100, H = 640, Scale = 1.5;

    /// <summary>«Рабочий стол»: обои, окно «Заметки» с несколькими строками текста и панель задач.</summary>
    internal static BitmapSource FakeDesktop(bool light)
    {
        var grid = new Grid { Width = W, Height = H };
        grid.Children.Add(Scenes.Wallpaper(W, H, light));

        var text = light ? Brushes.Black : Brushes.White;
        var window = new Border
        {
            Width = 560, Height = 340, CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(light ? Color.FromRgb(0xFA, 0xFA, 0xFA) : Color.FromRgb(0x27, 0x27, 0x27)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(250, 120, 0, 0),
        };
        var body = new StackPanel { Margin = new Thickness(18, 10, 18, 10) };
        body.Children.Add(new TextBlock { Text = Loc.Current == UiLanguage.En ? "Notes.txt" : "Заметки.txt", FontFamily = UiFonts.Text, FontSize = 12, Foreground = text, Margin = new Thickness(0, 0, 0, 16) });
        var lines = Loc.Current == UiLanguage.En
            ? new[] { "Trip plan for October", "• Book the hotel by Friday", "• Photos from the sea → convert to JPG", "• Send the contract (PDF) to Anna", "• Buy a charger for the camera" }
            : new[] { "План поездки на октябрь", "• Забронировать гостиницу до пятницы", "• Фото с моря → перевести в JPG", "• Отправить договор (PDF) Анне", "• Купить зарядку для камеры" };
        foreach (var (line, i) in lines.Select((l, i) => (l, i)))
            body.Children.Add(new TextBlock
            {
                Text = line, FontFamily = UiFonts.Text, FontSize = i == 0 ? 20 : 15, Foreground = text,
                FontWeight = i == 0 ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 0, 0, 10),
            });
        window.Child = body;
        grid.Children.Add(window);

        grid.Children.Add(new Border
        {
            Height = 48, VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(light ? Color.FromArgb(0xE6, 0xEE, 0xEE, 0xEE) : Color.FromArgb(0xE6, 0x1C, 0x1C, 0x1C)),
        });

        grid.Measure(new Size(W, H));
        grid.Arrange(new Rect(0, 0, W, H));
        grid.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)(W * Scale), (int)(H * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        bitmap.Freeze();
        return bitmap;
    }

    internal static CaptureView Make(bool light, bool selected)
    {
        var view = new CaptureView(light ? SettingsPalette.Light() : SettingsPalette.Dark(), ocr: true)
        {
            Width = W, Height = H, Frame = FakeDesktop(light),
        };
        if (selected)
        {
            // Выделено: заголовок и список из окна «Заметки».
            var sel = new DipRect(262, 160, 420, 196);
            view.Show(sel, CaptureLayout.SizeText(new PixelRect(0, 0, (int)(sel.Width * Scale), (int)(sel.Height * Scale))), toolbar: true, hint: false);
        }
        else
        {
            // Курсор над окном — подсвечено оно целиком, вверху подсказка.
            view.Show(new DipRect(250, 120, 560, 340), CaptureLayout.SizeText(new PixelRect(0, 0, 840, 510)), toolbar: false, hint: true);
        }
        return view;
    }

    internal static void Save(CaptureView view, string name)
    {
        var path = Screenshot.Save(view, W, H, name, scale: Scale);
        Assert.True(new FileInfo(path).Length > 20000);
    }

    [Theory]
    [InlineData(true, "capture-light")]
    [InlineData(false, "capture-dark")]
    public void Capture_overlay(bool light, string name)
    {
        Sta.Run(() => Save(Make(light, selected: true), name));
    }

    [Fact]
    public void Capture_overlay_hover_with_hint()
    {
        Sta.Run(() => Save(Make(light: true, selected: false), "capture-light-hover"));
    }

    /// <summary>В настройках («Основное → Windows») есть переключатель «Ctrl+Alt+S — снимок экрана».</summary>
    [Fact]
    public void Settings_have_capture_toggle()
    {
        Sta.Run(() =>
        {
            var view = SettingsScreenshots.Make(true, SettingsPage.General, SettingsScreenshots.Sample() with { CaptureKeys = "ctrl+shift+prtsc" });
            view.Width = SettingsView.DefaultWidth;
            view.Measure(new Size(SettingsView.DefaultWidth, 2000));
            view.Arrange(new Rect(0, 0, SettingsView.DefaultWidth, 2000));
            view.UpdateLayout();
            Assert.Contains(Texts(view), t => t == "Ctrl+Shift+Print Screen — снимок экрана");
        });
    }

    private static IEnumerable<string> Texts(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is TextBlock { Text: { } text }) yield return text;
            foreach (var t in Texts(child)) yield return t;
        }
    }
}

/// <summary>Оверлей снимка по-английски (язык меняется — в коллекции языка).</summary>
[Collection(LanguageCollection.Name)]
public class CaptureEnglishScreenshots
{
    [Fact]
    public void Capture_overlay_in_english()
    {
        Sta.Run(() =>
        {
            using var _ = Loc.Use(UiLanguage.En);
            CaptureScreenshots.Save(CaptureScreenshots.Make(light: true, selected: true), "capture-light-en");
        });
    }
}
