using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DropWheel.App;
using DropWheel.Conversions;
using DropWheel.Settings;
using DropWheel.Ui;
using DropWheel.Wheel;

namespace DropWheel.Windows.Tests;

/// <summary>
/// Окно настроек → PNG: разделы «Основное», «Качество», «Колесо» (со своим порядком, скрытым пунктом и мини-колесом),
/// светлая и тёмная тема. Настройки и номер сборки — фиксированные, чтобы картинки не зависели от компьютера.
/// </summary>
public class SettingsScreenshots
{
    private const int Build = 123;

    private static AppSettings Sample() => new()
    {
        Hotkey = "Space",
        AutoStart = true,
        ContextMenuEnabled = true,
        CheckForUpdates = true,
        Conversion = new ConversionSettings
        {
            JpegQuality = 88, WebpQuality = 80, VideoCrf = 23, Mp3BitrateKbps = 192,
            OutputFolder = @"C:\Users\Роман\Downloads\DropWheel",
        },
        Wheel = new WheelPreferences { MruFirst = true }
            .WithLayout(WheelGroups.Photo, OperationMode.Convert, ["to-png", "to-jpg", "to-webp"], ["to-avif"])
            .WithRecent(WheelGroups.Photo, OperationMode.Convert, "to-webp"),
    };

    /// <summary>Окно с заголовком Windows 11 поверх обоев: тень, скругление, заголовок с кнопками.</summary>
    private static (FrameworkElement Scene, double Width, double Height) Window(SettingsView view, bool light, double h)
    {
        const double margin = 28, titleHeight = 32;
        var w = SettingsView.DefaultWidth;
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
            Width = w, Height = h,
            Background = new SolidColorBrush(palette.Solid),
            Clip = new RectangleGeometry(new Rect(0, 0, w, h), 8, 8),
        };
        var title = new Grid { Height = titleHeight };
        title.Children.Add(new TextBlock
        {
            Text = Strings.Settings.WindowTitle, FontFamily = UiFonts.Text, FontSize = 12, Foreground = palette.Text,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0),
        });
        var caption = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var glyph in new[] { "\uE921", "\uE922", "\uE8BB" })
            caption.Children.Add(new TextBlock
            {
                Text = glyph, FontFamily = UiFonts.Icons, FontSize = 10, Foreground = palette.Text, Width = 46,
                TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
        title.Children.Add(caption);
        DockPanel.SetDock(title, Dock.Top);
        frame.Children.Add(title);
        view.Width = w;
        view.Height = h - titleHeight;
        frame.Children.Add(view);
        grid.Children.Add(frame);
        grid.Children.Add(new Border
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(light ? Color.FromArgb(0x1F, 0, 0, 0) : Color.FromArgb(0x33, 0x75, 0x75, 0x75)),
            IsHitTestVisible = false,
        });
        return (grid, sceneW, sceneH);
    }

    private static void Save(SettingsView view, bool light, string name)
    {
        // «Колесо» длиннее — окно на снимке повыше, чтобы список и мини-колесо были видны целиком.
        var height = view.Page == SettingsPage.Wheel ? 860 : SettingsView.DefaultHeight;
        var (scene, w, h) = Window(view, light, height);
        var path = Screenshot.Save(scene, w, h, name, scale: 1.5);
        Assert.True(new FileInfo(path).Length > 5000);
    }

    private static SettingsView Make(bool light, SettingsPage page, AppSettings? settings = null) =>
        new(light ? SettingsPalette.Light() : SettingsPalette.Dark(), settings ?? Sample(), AccentColors.Default, Build)
        {
            Page = page,
        };

    [Theory]
    [InlineData(true, SettingsPage.General, "settings-light-general")]
    [InlineData(false, SettingsPage.General, "settings-dark-general")]
    [InlineData(true, SettingsPage.Quality, "settings-light-quality")]
    [InlineData(false, SettingsPage.Quality, "settings-dark-quality")]
    [InlineData(true, SettingsPage.Wheel, "settings-light-wheel")]
    [InlineData(false, SettingsPage.Wheel, "settings-dark-wheel")]
    public void Pages(bool light, SettingsPage page, string name)
    {
        Sta.Run(() => Save(Make(light, page), light, name));
    }

    /// <summary>«Колесо» → Инструменты для видео; результат «рядом с исходником» — по умолчанию.</summary>
    [Fact]
    public void Wheel_tools_for_video()
    {
        Sta.Run(() =>
        {
            var view = Make(false, SettingsPage.Wheel, new AppSettings());
            view.WheelGroup = WheelGroups.Video;
            view.WheelMode = OperationMode.Tool;
            Save(view, light: false, "settings-dark-wheel-video-tools");
        });
    }

    /// <summary>Внешняя правка (трей, «последний выбор») перестраивает вид, но обратно в приложение не уходит.</summary>
    [Fact]
    public void External_settings_do_not_call_update()
    {
        Sta.Run(() =>
        {
            var applied = new List<AppSettings>();
            var current = Sample();
            var view = Make(true, SettingsPage.Wheel);
            view.Update = change =>
            {
                current = change(current);
                applied.Add(current);
                return current;
            };
            view.Settings = current with { ContextMenuEnabled = false };
            Assert.Empty(applied);
            Assert.False(view.Settings.ContextMenuEnabled);
        });
    }
}
