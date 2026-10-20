using DropWheel.Conversions;
using DropWheel.Ui;
using DropWheel.Wheel;

namespace DropWheel.Windows.Tests;

/// <summary>
/// Колесо в разных состояниях и темах → PNG (артефакт CI «screenshots»).
/// Тест падает, только если рисование бросило исключение или картинка пустая.
/// </summary>
public class WheelScreenshots
{
    private static IReadOnlyList<WheelItem> ItemsFor(string file, OperationMode mode) =>
        WheelItems.For(OperationCatalog.ForFiles([file]), mode);

    private static WheelControl Make(bool light, bool translucent = true) => new()
    {
        Palette = light ? WheelPalette.Light() : WheelPalette.Dark(),
        Backdrop = translucent ? WheelBackdrop.Translucent : WheelBackdrop.Solid,
        IsPending = false,
    };

    private static void Save(WheelControl wheel, bool light, string name)
    {
        var path = Screenshot.Save(Scenes.Wheel(wheel, light), 300, 300, name);
        Assert.True(new FileInfo(path).Length > 1000);
    }

    [Theory]
    [InlineData("photo.heic", OperationMode.Convert, 2, false, "wheel-dark-photo-convert-hover")]
    [InlineData("photo.heic", OperationMode.Convert, 2, true, "wheel-light-photo-convert-hover")]
    [InlineData("photo.heic", OperationMode.Tool, 0, false, "wheel-dark-photo-tools")]
    [InlineData("photo.heic", OperationMode.Tool, 3, true, "wheel-light-photo-tools")]
    [InlineData("clip.mov", OperationMode.Convert, null, false, "wheel-dark-video-convert")]
    [InlineData("clip.mov", OperationMode.Tool, 1, true, "wheel-light-video-tools")]
    [InlineData("song.flac", OperationMode.Convert, 1, true, "wheel-light-audio-convert")]
    [InlineData("song.flac", OperationMode.Tool, 0, false, "wheel-dark-audio-tools")]
    [InlineData("report.pdf", OperationMode.Convert, null, false, "wheel-dark-pdf-convert")]
    [InlineData("report.pdf", OperationMode.Tool, 1, true, "wheel-light-pdf-tools")]
    [InlineData("archive.zip", OperationMode.Tool, 0, false, "wheel-dark-zip-tools")]
    public void Wheel(string file, OperationMode mode, int? hover, bool light, string name)
    {
        Sta.Run(() =>
        {
            var wheel = Make(light);
            var convert = ItemsFor(file, OperationMode.Convert);
            var tools = ItemsFor(file, OperationMode.Tool);
            wheel.Items = mode == OperationMode.Convert ? convert : tools;
            wheel.Mode = mode;
            wheel.ShowModeDots = convert.Count > 0 && tools.Count > 0;
            wheel.HoverIndex = hover;
            Save(wheel, light, name);
        });
    }

    [Theory]
    [InlineData(false, "wheel-dark-many")]
    [InlineData(true, "wheel-light-many")]
    public void Wheel_eight_sectors(bool light, string name)
    {
        Sta.Run(() =>
        {
            var wheel = Make(light);
            wheel.Items = OperationCatalog.All.Where(o => o.Kind == MediaKind.Image).Take(8)
                .Select(o => new WheelItem(o.Id, o.Label)).ToList();
            wheel.ShowModeDots = true;
            wheel.HoverIndex = 5;
            Save(wheel, light, name);
        });
    }

    [Theory]
    [InlineData(false, "wheel-dark-two")]
    [InlineData(true, "wheel-light-one")]
    public void Wheel_few_sectors(bool light, string name)
    {
        Sta.Run(() =>
        {
            var wheel = Make(light);
            wheel.Items = light
                ? [new WheelItem("zip-pack", "В ZIP")]
                : [new WheelItem("audio-normalize", "Нормализовать"), new WheelItem("zip-pack", "В ZIP")];
            wheel.Mode = OperationMode.Tool;
            wheel.HoverIndex = 0;
            Save(wheel, light, name);
        });
    }

    [Theory]
    [InlineData(false, "wheel-dark-solid")]
    [InlineData(true, "wheel-light-solid")]
    public void Wheel_without_blur(bool light, string name)
    {
        Sta.Run(() =>
        {
            var wheel = Make(light, translucent: false);
            wheel.Items = ItemsFor("photo.jpg", OperationMode.Convert);
            wheel.ShowModeDots = true;
            wheel.HoverIndex = 1;
            Save(wheel, light, name);
        });
    }

    [Theory]
    [InlineData(false, "wheel-dark-center-hover")]
    [InlineData(true, "wheel-light-center-hover")]
    public void Wheel_center_hovered(bool light, string name)
    {
        Sta.Run(() =>
        {
            var wheel = Make(light);
            wheel.Items = ItemsFor("clip.mp4", OperationMode.Convert);
            wheel.ShowModeDots = true;
            wheel.IsCenterHovered = true;
            Save(wheel, light, name);
        });
    }

    [Theory]
    [InlineData(false, "wheel-dark-empty")]
    [InlineData(true, "wheel-light-empty")]
    public void Wheel_nothing_to_offer(bool light, string name)
    {
        Sta.Run(() =>
        {
            var wheel = Make(light);
            wheel.EmptyHint = "Отпустите — ничего не произойдёт";
            Save(wheel, light, name);
        });
    }

    [Theory]
    [InlineData(false, "wheel-dark-pending")]
    [InlineData(true, "wheel-light-pending")]
    public void Wheel_waiting_for_files(bool light, string name)
    {
        Sta.Run(() =>
        {
            var wheel = Make(light);
            wheel.IsPending = true;
            Save(wheel, light, name);
        });
    }

    /// <summary>Меню проводника «Конвертировать…» — то же колесо в режиме клика: стрелками выбран сектор, точки режима видны.</summary>
    [Theory]
    [InlineData(false, "chooser-dark")]
    [InlineData(true, "chooser-light")]
    public void Chooser(bool light, string name)
    {
        Sta.Run(() =>
        {
            var wheel = Make(light);
            wheel.Items = ItemsFor("IMG_2041.HEIC", OperationMode.Convert);
            wheel.ShowModeDots = true;
            wheel.HoverIndex = 0;
            wheel.IsDotsHovered = true;
            Save(wheel, light, name);
        });
    }

    [Fact]
    public void Every_catalog_operation_has_a_glyph()
    {
        foreach (var op in OperationCatalog.All)
            Assert.False(string.IsNullOrEmpty(OperationGlyphs.For(op)), op.Id);
    }
}
