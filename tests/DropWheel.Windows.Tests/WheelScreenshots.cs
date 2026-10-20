using DropWheel.Conversions;
using DropWheel.Ui;
using DropWheel.Wheel;

namespace DropWheel.Windows.Tests;

/// <summary>
/// Колесо в разных состояниях и темах → PNG (артефакт CI «screenshots»).
/// Светлая тема — на тёплых оранжевых обоях, тёмная — на сине-фиолетовых; размытие под диском имитируется.
/// Тест падает, только если рисование бросило исключение или картинка пустая.
/// </summary>
public class WheelScreenshots
{
    private const double Size = WheelWindow.DipSize;

    /// <summary>8 лепестков-форматов (картинка): часть — из каталога, остальные — чтобы заполнить колесо.</summary>
    private static readonly WheelItem[] EightFormats =
    [
        new("to-jpg", "JPG"), new("to-png", "PNG"), new("to-webp", "WebP"), new("to-avif", "AVIF"),
        new("to-pdf", "PDF"), new("to-heic", "HEIC"), new("to-bmp", "BMP"), new("to-tiff", "TIFF"),
    ];

    /// <summary>4 лепестка (PDF): сверху, справа, снизу, слева.</summary>
    private static readonly WheelItem[] PdfFormats =
    [
        new("pdf-to-png", "PNG (страницы)"), new("pdf-to-jpg", "JPG (страницы)"),
        new("pdf-to-text", "TXT"), new("pdf-to-docx", "Word (DOCX)"),
    ];

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
        var path = Screenshot.Save(Scenes.Wheel(wheel, light), Size, Size, name);
        Assert.True(new FileInfo(path).Length > 1000);
    }

    /// <summary>Конвертация, 8 лепестков, ни на что не наведено — в пилюле размер файла.</summary>
    [Theory]
    [InlineData(false, "wheel-dark-convert-8-idle-size")]
    [InlineData(true, "wheel-light-convert-8-idle-size")]
    public void Convert_eight_petals_idle(bool light, string name)
    {
        Sta.Run(() =>
        {
            var wheel = Make(light);
            wheel.Items = EightFormats;
            wheel.ShowModeDots = true;
            wheel.IdleText = "1,1\u00A0МБ";
            Save(wheel, light, name);
        });
    }

    /// <summary>Конвертация, наведено на лепесток — он в цвете акцента, в пилюле имя формата.</summary>
    [Theory]
    [InlineData(false, "wheel-dark-convert-8-hover")]
    [InlineData(true, "wheel-light-convert-8-hover")]
    public void Convert_eight_petals_hovered(bool light, string name)
    {
        Sta.Run(() =>
        {
            var wheel = Make(light);
            wheel.Items = EightFormats;
            wheel.ShowModeDots = true;
            wheel.IdleText = "1,1\u00A0МБ";
            wheel.HoverIndex = 2;
            Save(wheel, light, name);
        });
    }

    /// <summary>PDF: 4 лепестка (верх/право/низ/лево), уточнение формата мелко под названием.</summary>
    [Theory]
    [InlineData(false, null, "wheel-dark-convert-4-pdf")]
    [InlineData(true, null, "wheel-light-convert-4-pdf")]
    [InlineData(false, 1, "wheel-dark-convert-4-pdf-hover")]
    [InlineData(true, 3, "wheel-light-convert-4-pdf-hover")]
    public void Convert_four_petals_pdf(bool light, int? hover, string name)
    {
        Sta.Run(() =>
        {
            var wheel = Make(light);
            wheel.Items = PdfFormats;
            wheel.ShowModeDots = true;
            wheel.IdleText = "3,4\u00A0МБ";
            wheel.HoverIndex = hover;
            Save(wheel, light, name);
        });
    }

    /// <summary>Инструменты: значок над подписью, подписи как есть.</summary>
    [Theory]
    [InlineData("photo.heic", null, false, "wheel-dark-tools-photo")]
    [InlineData("photo.heic", 3, true, "wheel-light-tools-photo-hover")]
    [InlineData("clip.mov", 1, false, "wheel-dark-tools-video-hover")]
    [InlineData("clip.mov", null, true, "wheel-light-tools-video")]
    [InlineData("song.flac", 0, false, "wheel-dark-tools-audio-hover")]
    [InlineData("report.pdf", null, true, "wheel-light-tools-pdf")]
    [InlineData("archive.zip", 0, false, "wheel-dark-tools-zip-hover")]
    public void Tools(string file, int? hover, bool light, string name)
    {
        Sta.Run(() =>
        {
            var wheel = Make(light);
            var convert = ItemsFor(file, OperationMode.Convert);
            wheel.Items = ItemsFor(file, OperationMode.Tool);
            wheel.Mode = OperationMode.Tool;
            wheel.ShowModeDots = convert.Count > 0;
            wheel.IdleText = "24\u00A0МБ";
            wheel.HoverIndex = hover;
            Save(wheel, light, name);
        });
    }

    /// <summary>Наборы из настоящего каталога операций (что увидит пользователь на этой сборке).</summary>
    [Theory]
    [InlineData("photo.heic", 1, false, "wheel-dark-catalog-photo")]
    [InlineData("clip.mov", null, true, "wheel-light-catalog-video")]
    [InlineData("song.flac", 2, true, "wheel-light-catalog-audio")]
    [InlineData("report.pdf", 0, false, "wheel-dark-catalog-pdf")]
    public void Catalog_convert(string file, int? hover, bool light, string name)
    {
        Sta.Run(() =>
        {
            var wheel = Make(light);
            wheel.Items = ItemsFor(file, OperationMode.Convert);
            wheel.ShowModeDots = ItemsFor(file, OperationMode.Tool).Count > 0;
            wheel.IdleText = "5,2\u00A0МБ";
            wheel.HoverIndex = hover;
            Save(wheel, light, name);
        });
    }

    [Theory]
    [InlineData(false, "wheel-dark-two")]
    [InlineData(true, "wheel-light-one")]
    public void Few_petals(bool light, string name)
    {
        Sta.Run(() =>
        {
            var wheel = Make(light);
            wheel.Items = light
                ? [new WheelItem("zip-pack", "В ZIP")]
                : [new WheelItem("audio-normalize", "Нормализовать"), new WheelItem("zip-pack", "В ZIP")];
            wheel.Mode = OperationMode.Tool;
            wheel.IdleText = "640\u00A0КБ";
            wheel.HoverIndex = 0;
            Save(wheel, light, name);
        });
    }

    /// <summary>Без «Эффектов прозрачности» Windows: плотный диск с тенью.</summary>
    [Theory]
    [InlineData(false, "wheel-dark-solid")]
    [InlineData(true, "wheel-light-solid")]
    public void Without_blur(bool light, string name)
    {
        Sta.Run(() =>
        {
            var wheel = Make(light, translucent: false);
            wheel.Items = ItemsFor("photo.jpg", OperationMode.Convert);
            wheel.ShowModeDots = true;
            wheel.IdleText = "2,7\u00A0МБ";
            wheel.HoverIndex = 1;
            Save(wheel, light, name);
        });
    }

    /// <summary>Курсор в центре — в пилюле «Отмена».</summary>
    [Theory]
    [InlineData(false, "wheel-dark-center-cancel")]
    [InlineData(true, "wheel-light-center-cancel")]
    public void Center_hovered(bool light, string name)
    {
        Sta.Run(() =>
        {
            var wheel = Make(light);
            wheel.Items = ItemsFor("clip.mp4", OperationMode.Convert);
            wheel.ShowModeDots = true;
            wheel.IdleText = "148\u00A0МБ";
            wheel.IsCenterHovered = true;
            Save(wheel, light, name);
        });
    }

    [Theory]
    [InlineData(false, "wheel-dark-empty")]
    [InlineData(true, "wheel-light-empty")]
    public void Nothing_to_offer(bool light, string name)
    {
        Sta.Run(() =>
        {
            var wheel = Make(light);
            wheel.EmptyHint = "Отпустите — ничего не произойдёт";
            Save(wheel, light, name);
        });
    }

    /// <summary>Колесо уже показано, файлы ещё не «въехали» (DragEnter не пришёл).</summary>
    [Theory]
    [InlineData(false, "wheel-dark-pending")]
    [InlineData(true, "wheel-light-pending")]
    public void Waiting_for_files(bool light, string name)
    {
        Sta.Run(() =>
        {
            var wheel = Make(light);
            wheel.IsPending = true;
            Save(wheel, light, name);
        });
    }

    /// <summary>Меню проводника «Конвертировать…» — то же колесо в режиме клика: стрелками выбран лепесток, точки режима под курсором.</summary>
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
            wheel.IdleText = "3,1\u00A0МБ";
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

    [Theory]
    [InlineData("PNG (страницы)", "PNG", "страницы")]
    [InlineData("WebP", "WEBP", null)]
    [InlineData("Word (DOCX)", "WORD", "DOCX")]
    public void Format_labels_are_uppercase_with_note(string label, string main, string? sub)
    {
        var (m, s) = WheelControl.SplitLabel(label);
        Assert.Equal(main, m);
        Assert.Equal(sub, s);
    }
}
