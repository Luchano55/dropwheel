using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DropWheel.App;
using DropWheel.Conversions;
using DropWheel.Localization;
using DropWheel.Ui;

namespace DropWheel.Windows.Tests;

/// <summary>
/// Окно «Оформить картинку» (D-BEAUTIFY) → PNG, светлая и тёмная тема, английский. Предпросмотр — нарисованный
/// (кадр с небом и холмами, как у окон обрезки, со скруглёнными углами и тенью на фоне), размер «настоящей» — 1600 × 1000.
/// </summary>
public class BeautifyScreenshots
{
    private const int W = 1600, H = 1000;

    /// <summary>Похоже на результат ImageMagick: фон стиля, тень под картинкой, скруглённая картинка с отступами.</summary>
    internal static ImageSource FakeResult(BeautifyStyle style, int width = 800, int height = 500)
    {
        var l = ImageBeautify.Measure(style, width, height);
        var bg = ImageBeautify.FindBackground(style.Background)!;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var canvas = new Rect(0, 0, l.Width, l.Height);
            if (!bg.IsTransparent) dc.DrawRectangle(BeautifyView.SwatchBrush(bg), null, canvas);
            var picture = new Rect(l.Pad, l.Pad, width, height);
            if (style.Shadow)
                for (var i = l.Blur * 2; i > 0; i -= 2)
                {
                    var spread = new Rect(picture.X - i / 2.0, picture.Y - i / 2.0 + l.Offset, picture.Width + i, picture.Height + i);
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(10, 0, 0, 0)), null, spread, l.Radius + i / 2.0, l.Radius + i / 2.0);
                }
            dc.PushClip(new RectangleGeometry(picture, l.Radius, l.Radius));
            dc.DrawImage(TrimScreenshots.FakeFrame(0.55, width, height), picture);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(l.Width, l.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    internal static BeautifyView Make(bool light, BeautifyStyle style, string? status = null)
    {
        var view = new BeautifyView(light ? SettingsPalette.Light() : SettingsPalette.Dark(), "Снимок экрана 2026-10-24.png", style);
        view.SetImage(W, H);
        view.SetPreview(FakeResult(ImageBeautify.ForPreview(style, W, H, 800, 500)));
        if (status is not null) view.SetStatus(status);
        return view;
    }

    internal static void Save(BeautifyView view, bool light, string name) =>
        TrimScreenshots.SaveWindow(view, BeautifyView.DefaultWidth, Loc.T.BeautifyTitle, view.Palette, light, name);

    [Theory]
    [InlineData(true, "beautify-light")]
    [InlineData(false, "beautify-dark")]
    public void Beautify_window(bool light, string name)
    {
        Sta.Run(() =>
        {
            var view = light
                ? Make(light, BeautifyStyle.Default with { Radius = 24 })
                : Make(light, new BeautifyStyle("none", 6, 32, true), Loc.T.CopiedToClipboard);
            Assert.True(view.CanSave);
            Save(view, light, name);
        });
    }

    [Fact]
    public void Save_and_copy_give_the_style_and_nothing_before_the_picture_opens()
    {
        Sta.Run(() =>
        {
            var style = new BeautifyStyle("sky", 12, 0, false);
            var view = Make(light: true, style);
            BeautifyStyle? saved = null, copied = null;
            view.SaveRequested += s => saved = s;
            view.CopyRequested += s => copied = s;
            view.RequestSave();
            view.RequestCopy();
            Assert.Equal(style, saved);
            Assert.Equal(style, copied);

            var empty = new BeautifyView(SettingsPalette.Dark(), "тест 1.heic");
            Assert.False(empty.CanSave);
            empty.SaveRequested += _ => Assert.Fail("картинки ещё нет");
            empty.RequestSave();
            empty.SetFailed(Loc.T.CropFailed);
            Assert.False(empty.CanSave);
        });
    }
}

/// <summary>Окно «Оформить картинку» по-английски (язык меняется — в коллекции языка).</summary>
[Collection(LanguageCollection.Name)]
public class BeautifyEnglishScreenshots
{
    [Fact]
    public void Beautify_window_in_english()
    {
        Sta.Run(() =>
        {
            using var _ = Loc.Use(UiLanguage.En);
            BeautifyScreenshots.Save(BeautifyScreenshots.Make(light: true, new BeautifyStyle("mint", 10, 20, true)), light: true, "beautify-light-en");
        });
    }
}
