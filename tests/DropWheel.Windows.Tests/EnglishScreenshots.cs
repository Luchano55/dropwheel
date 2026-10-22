using System.Windows.Media;
using DropWheel.App;
using DropWheel.Conversions;
using DropWheel.Jobs;
using DropWheel.Localization;
using DropWheel.Ui;

namespace DropWheel.Windows.Tests;

/// <summary>
/// Язык интерфейса (Loc.Current) общий для процесса: тесты, которые его меняют, — в этой коллекции,
/// xUnit запускает её после остальных, не параллельно с русскими снимками.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LanguageCollection
{
    public const string Name = "Язык интерфейса";
}

/// <summary>
/// Английский интерфейс → PNG (решение D-LANG): колесо фото (конвертация и инструменты), окно задач,
/// настройки «Основное». Язык задаётся явно — снимки не зависят от языка Windows на машине CI.
/// </summary>
[Collection(LanguageCollection.Name)]
public class EnglishScreenshots
{
    [Theory]
    [InlineData(true, OperationMode.Convert, 1, "wheel-en-light-photo-convert")]
    [InlineData(false, OperationMode.Convert, null, "wheel-en-dark-photo-convert")]
    [InlineData(true, OperationMode.Tool, 2, "wheel-en-light-photo-tools")]
    [InlineData(false, OperationMode.Tool, 0, "wheel-en-dark-photo-tools")]
    public void Wheel_photo(bool light, OperationMode mode, int? hover, string name)
    {
        using var _ = Loc.Use(UiLanguage.En);
        Sta.Run(() =>
        {
            var wheel = WheelScreenshots.Make(light);
            var other = mode == OperationMode.Convert ? OperationMode.Tool : OperationMode.Convert;
            wheel.Items = WheelScreenshots.ItemsFor("photo.heic", mode);
            wheel.Mode = mode;
            wheel.ShowModeDots = WheelScreenshots.ItemsFor("photo.heic", other).Count > 0;
            wheel.IdleText = Loc.FileSize(5_452_595);
            wheel.HoverIndex = hover;
            Assert.Equal("5.2 MB", wheel.IdleText);
            WheelScreenshots.Save(wheel, light, name);
        });
    }

    [Theory]
    [InlineData(false, "wheel-en-dark-pdf-convert")]
    public void Wheel_pdf_with_notes(bool light, string name)
    {
        using var _ = Loc.Use(UiLanguage.En);
        Sta.Run(() =>
        {
            var wheel = WheelScreenshots.Make(light);
            wheel.Items = WheelScreenshots.ItemsFor("report.pdf", OperationMode.Convert);
            wheel.ShowModeDots = true;
            wheel.IdleText = Loc.FileSize(3_565_158);
            wheel.HoverIndex = 0;
            WheelScreenshots.Save(wheel, light, name);
        });
    }

    [Theory]
    [InlineData(false, "wheel-en-dark-empty")]
    public void Wheel_nothing_to_offer(bool light, string name)
    {
        using var _ = Loc.Use(UiLanguage.En);
        Sta.Run(() =>
        {
            var wheel = WheelScreenshots.Make(light);
            wheel.EmptyHint = Loc.T.WheelEmptyDrag;
            WheelScreenshots.Save(wheel, light, name);
        });
    }

    private static string Op(string id)
    {
        var op = OperationCatalog.Find(id)!;
        return op.Mode == OperationMode.Convert ? "→ " + Loc.Label(op) : Loc.Label(op);
    }

    private static IReadOnlyList<JobRowModel> Rows() =>
    [
        new("1", "IMG_2041.HEIC", Op("to-jpg"), JobState.Running)
            { Glyph = Glyphs.Photo, Thumbnail = JobsScreenshots.FakePhoto(Color.FromRgb(0x6F, 0xA8, 0xE8), Color.FromRgb(0x3E, 0x7C, 0x4A)) },
        new("2", "Summer trip 2024 — sea.mov", Op("to-mp4"), JobState.Running)
            { Progress = 0.46, Glyph = Glyphs.Video, Thumbnail = JobsScreenshots.FakePhoto(Color.FromRgb(0xF2, 0x9B, 0x6B), Color.FromRgb(0x2D, 0x5F, 0x8A)) },
        new("3", "song.flac", Op("to-mp3"), JobState.Queued) { Glyph = Glyphs.Audio },
        new("4", "IMG_1999.HEIC", Op("to-png"), JobState.Done)
            { ResultName = "IMG_1999.png", Glyph = Glyphs.Photo, Thumbnail = JobsScreenshots.FakePhoto(Color.FromRgb(0x9C, 0xC7, 0xF0), Color.FromRgb(0x5A, 0x8C, 0x3C)) },
        new("5", "Lease agreement.pdf", Op("pdf-optimize"), JobState.Done)
            { ResultName = "Lease agreement" + Loc.Suffix(OperationCatalog.Find("pdf-optimize")!) + ".pdf", Glyph = Glyphs.Document },
        new("6", "broken.jpg", Op("to-png"), JobState.Failed)
            { Error = Loc.T.ErrCorruptOrUnknown, Glyph = Glyphs.Photo },
        new("7", "clip.mkv", Op("video-mute"), JobState.Canceled) { Glyph = Glyphs.Video },
    ];

    [Theory]
    [InlineData(true, "jobs-en-light")]
    [InlineData(false, "jobs-en-dark")]
    public void Jobs_flyout(bool light, string name)
    {
        using var _ = Loc.Use(UiLanguage.En);
        Sta.Run(() =>
        {
            var view = new JobsFlyoutView(light ? FlyoutPalette.Light() : FlyoutPalette.Dark(), DropWheel.Input.Hotkeys.DisplayName("Space"));
            view.SetRows(Rows());
            JobsScreenshots.Save(view, light, backdrop: false, name);
        });
    }

    [Fact]
    public void Jobs_flyout_empty()
    {
        using var _ = Loc.Use(UiLanguage.En);
        Sta.Run(() =>
        {
            var view = new JobsFlyoutView(FlyoutPalette.Light(), DropWheel.Input.Hotkeys.DisplayName("Space"));
            view.SetRows([]);
            JobsScreenshots.Save(view, light: true, backdrop: false, "jobs-en-empty-light");
        });
    }

    [Theory]
    [InlineData(true, SettingsPage.General, "settings-en-light-general")]
    [InlineData(false, SettingsPage.General, "settings-en-dark-general")]
    [InlineData(true, SettingsPage.Wheel, "settings-en-light-wheel")]
    public void Settings(bool light, SettingsPage page, string name)
    {
        using var _ = Loc.Use(UiLanguage.En);
        Sta.Run(() =>
        {
            var settings = SettingsScreenshots.Sample() with { Language = Loc.English };
            SettingsScreenshots.Save(SettingsScreenshots.Make(light, page, settings), light, name);
        });
    }

    /// <summary>Своя горячая клавиша (D-HOTKEY): назначена и режим «Press a key…».</summary>
    [Theory]
    [InlineData(false, "settings-en-light-general-custom-key")]
    [InlineData(true, "settings-en-light-general-key-capture")]
    public void Settings_custom_hotkey(bool capture, string name)
    {
        using var _ = Loc.Use(UiLanguage.En);
        SettingsScreenshots.CustomHotkey(capture, name);
    }

    /// <summary>Колесо для папки «Vacation» из меню проводника (D-FOLDERS).</summary>
    [Theory]
    [InlineData(true, OperationMode.Convert, 0, "wheel-en-light-folder-convert")]
    [InlineData(true, OperationMode.Tool, null, "wheel-en-light-folder-tools")]
    public void Wheel_folder(bool light, OperationMode mode, int? hover, string name)
    {
        using var _ = Loc.Use(UiLanguage.En);
        FeatureScreenshots.FolderWheel(light, mode, hover, "Vacation", name);
    }

    /// <summary>Колесо для картинки WebP, перетащенной из браузера (D-BROWSER).</summary>
    [Theory]
    [InlineData(true, 1, "wheel-en-light-browser-webp")]
    public void Wheel_browser_image(bool light, int? hover, string name)
    {
        using var _ = Loc.Use(UiLanguage.En);
        FeatureScreenshots.BrowserWheel(light, hover, name);
    }

    /// <summary>Нижняя часть «General»: картинки из буфера и браузера, автозапуск, канал обновлений.</summary>
    [Theory]
    [InlineData(true, "settings-en-light-general-lower")]
    [InlineData(false, "settings-en-dark-general-lower")]
    public void Settings_general_lower(bool light, string name)
    {
        using var _ = Loc.Use(UiLanguage.En);
        FeatureScreenshots.SettingsLower(light, @"C:\Users\Roman\Pictures\From browser", name);
    }

    /// <summary>Смена языка на лету: окно настроек перестраивается и говорит на новом языке.</summary>
    [Fact]
    public void Settings_view_relocalizes()
    {
        using var _ = Loc.Use(UiLanguage.Ru);
        Sta.Run(() =>
        {
            var view = SettingsScreenshots.Make(true, SettingsPage.General);
            Assert.Equal("Настройки DropWheel", Strings.Settings.WindowTitle);
            Loc.Current = UiLanguage.En;
            view.Relocalize();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { },
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.Equal("DropWheel Settings", Strings.Settings.WindowTitle);
            SettingsScreenshots.Save(view, light: true, "settings-en-light-general-relocalized");
        });
    }
}
