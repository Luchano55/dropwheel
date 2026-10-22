using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DropWheel.App;
using DropWheel.Jobs;
using DropWheel.Ui;

namespace DropWheel.Windows.Tests;

/// <summary>Окно задач у трея → PNG: все состояния строк, светлая/тёмная тема, сплошной фон и акрил, пустое окно.</summary>
public class JobsScreenshots
{
    /// <summary>«Фото» для миниатюры: небо, солнце, горы — вместо настоящего превью из проводника.</summary>
    internal static ImageSource FakePhoto(Color sky, Color ground)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(sky, Colors.White, 90), null, new Rect(0, 0, 96, 96));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x6B)), null, new Point(68, 30), 13, 13);
            var hills = new StreamGeometry();
            using (var g = hills.Open())
            {
                g.BeginFigure(new Point(0, 96), true, true);
                g.LineTo(new Point(0, 62), true, false);
                g.LineTo(new Point(30, 40), true, false);
                g.LineTo(new Point(55, 64), true, false);
                g.LineTo(new Point(78, 48), true, false);
                g.LineTo(new Point(96, 66), true, false);
                g.LineTo(new Point(96, 96), true, false);
            }
            dc.DrawGeometry(new SolidColorBrush(ground), null, hills);
        }
        var bitmap = new RenderTargetBitmap(96, 96, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static IReadOnlyList<JobRowModel> Rows() =>
    [
        new("1", "IMG_2041.HEIC", "→ JPG", JobState.Running)
            { Glyph = Glyphs.Photo, Thumbnail = FakePhoto(Color.FromRgb(0x6F, 0xA8, 0xE8), Color.FromRgb(0x3E, 0x7C, 0x4A)) },
        new("2", "Отпуск 2024 — море.mov", "→ MP4", JobState.Running)
            { Progress = 0.46, Glyph = Glyphs.Video, Thumbnail = FakePhoto(Color.FromRgb(0xF2, 0x9B, 0x6B), Color.FromRgb(0x2D, 0x5F, 0x8A)) },
        new("3", "song.flac", "→ MP3", JobState.Queued) { Glyph = Glyphs.Audio },
        new("4", "IMG_1999.HEIC", "→ PNG", JobState.Done)
            { ResultName = "IMG_1999.png", Glyph = Glyphs.Photo, Thumbnail = FakePhoto(Color.FromRgb(0x9C, 0xC7, 0xF0), Color.FromRgb(0x5A, 0x8C, 0x3C)) },
        new("5", "Договор аренды.pdf", "Оптимизировать", JobState.Done)
            { ResultName = "Договор аренды (оптим.).pdf", Glyph = Glyphs.Document },
        new("6", "сломанный.jpg", "→ PNG", JobState.Failed)
            { Error = "Файл повреждён или это не картинка", Glyph = Glyphs.Photo },
        new("7", "clip.mkv", "Без звука", JobState.Canceled) { Glyph = Glyphs.Video },
    ];

    internal static void Save(JobsFlyoutView view, bool light, bool backdrop, string name)
    {
        var (scene, w, h) = Scenes.Flyout(view, light, backdrop);
        var path = Screenshot.Save(scene, w, h, name);
        Assert.True(new FileInfo(path).Length > 1000);
    }

    [Theory]
    [InlineData(true, false, "jobs-light")]
    [InlineData(false, false, "jobs-dark")]
    [InlineData(true, true, "jobs-light-acrylic")]
    [InlineData(false, true, "jobs-dark-acrylic")]
    public void Flyout_with_all_states(bool light, bool backdrop, string name)
    {
        Sta.Run(() =>
        {
            var palette = light ? FlyoutPalette.Light(overBackdrop: backdrop) : FlyoutPalette.Dark(overBackdrop: backdrop);
            var view = new JobsFlyoutView(palette, "Пробел");
            view.SetRows(Rows());
            Save(view, light, backdrop, name);
        });
    }

    [Theory]
    [InlineData(true, "jobs-empty-light")]
    [InlineData(false, "jobs-empty-dark")]
    public void Flyout_empty(bool light, string name)
    {
        Sta.Run(() =>
        {
            var view = new JobsFlyoutView(light ? FlyoutPalette.Light() : FlyoutPalette.Dark(), "Пробел");
            view.SetRows([]);
            Save(view, light, backdrop: false, name);
        });
    }

    /// <summary>Много задач: список прокручивается, справа тонкий индикатор.</summary>
    [Fact]
    public void Flyout_many_jobs()
    {
        Sta.Run(() =>
        {
            var view = new JobsFlyoutView(FlyoutPalette.Dark(), "Пробел");
            view.SetRows(Rows().Concat(Rows().Select(r => r with { Key = r.Key + "b" })).ToList());
            Save(view, light: false, backdrop: false, "jobs-dark-many");
        });
    }

    /// <summary>Строки обновляются на месте: тот же ключ — та же строка, новое состояние.</summary>
    [Fact]
    public void Rows_update_in_place()
    {
        Sta.Run(() =>
        {
            var view = new JobsFlyoutView(FlyoutPalette.Light(), "Пробел");
            view.SetRows([new JobRowModel("a", "x.heic", "→ JPG", JobState.Running) { Progress = 0.2 }]);
            view.SetRows([new JobRowModel("a", "x.heic", "→ JPG", JobState.Done) { ResultName = "x.jpg" }]);
            Save(view, light: true, backdrop: false, "jobs-light-updated");
        });
    }
}
