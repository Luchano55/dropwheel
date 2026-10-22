using DropWheel.App;
using DropWheel.Conversions;
using DropWheel.Localization;
using DropWheel.Ui;

namespace DropWheel.Windows.Tests;

/// <summary>
/// Окно «Обрезать картинку» (D-CROP) → PNG, светлая и тёмная тема, английский. Картинка — нарисованная
/// (небо, солнце, холмы, как кадры окна обрезки видео), размер «настоящей» — 4032 × 2688.
/// </summary>
public class CropScreenshots
{
    private const int W = 4032, H = 2688;

    internal static CropView Make(bool light, CropAspect aspect, CropRect? rect)
    {
        var view = new CropView(light ? SettingsPalette.Light() : SettingsPalette.Dark(), "Отпуск 2026 — море.jpg");
        view.SetImage(TrimScreenshots.FakeFrame(0.35, 1200, 800), W, H);
        view.Aspect = aspect;
        if (rect is { } r) view.Rect = r;
        return view;
    }

    internal static void Save(CropView view, bool light, string name) =>
        TrimScreenshots.SaveWindow(view, CropView.DefaultWidth, Loc.T.CropTitle, view.Palette, light, name);

    [Theory]
    [InlineData(true, "crop-light")]
    [InlineData(false, "crop-dark")]
    public void Crop_window(bool light, string name)
    {
        Sta.Run(() =>
        {
            var view = light
                ? Make(light, CropAspect.FourThree, new CropRect(1210, 480, 2400, 1800))
                : Make(light, CropAspect.Square, null);
            Assert.True(view.CanCrop);
            Save(view, light, name);
        });
    }

    [Fact]
    public void Crop_button_gives_rect_and_whole_picture_is_not_cropped()
    {
        Sta.Run(() =>
        {
            var view = Make(light: true, CropAspect.Free, new CropRect(100, 200, 300, 400));
            (CropRect Rect, int W, int H)? got = null;
            view.CropRequested += (r, w, h) => got = (r, w, h);
            view.RequestCrop();
            Assert.Equal((new CropRect(100, 200, 300, 400), W, H), got);

            got = null;
            view.Aspect = CropAspect.Original; // вся картинка
            Assert.False(view.CanCrop);
            view.RequestCrop();
            Assert.Null(got);

            var failed = new CropView(SettingsPalette.Dark(), "тест 1.heic");
            Assert.False(failed.CanCrop);
            failed.SetFailed(Loc.T.CropFailed);
            Assert.False(failed.CanCrop);
        });
    }
}

/// <summary>Окно обрезки картинки по-английски (язык меняется — в коллекции языка).</summary>
[Collection(LanguageCollection.Name)]
public class CropEnglishScreenshots
{
    [Fact]
    public void Crop_window_in_english()
    {
        Sta.Run(() =>
        {
            using var _ = Loc.Use(UiLanguage.En);
            CropScreenshots.Save(CropScreenshots.Make(light: true, CropAspect.Free, new CropRect(640, 300, 2900, 1700)), light: true, "crop-light-en");
        });
    }
}
