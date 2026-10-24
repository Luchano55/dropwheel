using DropWheel.Engines;

namespace DropWheel.Tests.Integration;

/// <summary>«Пометки» (D-ANNOTATE): окно открывает картинку настоящим ImageMagick — в полном размере и повёрнутой по EXIF.</summary>
public class ImageAnnotateLoadTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string Folder()
    {
        var folder = Path.Combine(_dir.Path, "Мои фото");
        Directory.CreateDirectory(folder);
        return folder;
    }

    [SkippableFact]
    public async Task Big_picture_opens_without_downscaling()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        // Больше копии для «Обрезать…» (1600 px): пометки рисуются на полной картинке.
        var input = Tools.MakeImage(Path.Combine(Folder(), "тест 1.jpg"), 2400, 1800);
        var hash = Tools.Hash(input);
        var png = Path.Combine(_dir.Path, "полная.png");
        Assert.Equal((2400, 1800), await ImageProbe.FullAsync(Tools.Locator, input, png));
        Assert.Equal((2400, 1800), Tools.ImageSize(png));
        Assert.Equal(hash, Tools.Hash(input)); // исходник не тронут
    }

    [SkippableFact]
    public async Task Exif_rotated_photo_opens_as_shown_and_16_bit_becomes_8_bit()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var plain = Path.Combine(_dir.Path, "plain.jpg");
        Tools.Run(Tools.Magick, "-size", "64x48", "xc:red", plain);
        var input = Path.Combine(Folder(), "тест 1.jpg");
        // EXIF «повернуть на 90° по часовой» → на экране 48×64.
        File.WriteAllBytes(input, ImageCropConversionTests.WithExifOrientation(File.ReadAllBytes(plain), 6));
        var png = Path.Combine(_dir.Path, "a.png");
        Assert.Equal((48, 64), await ImageProbe.FullAsync(Tools.Locator, input, png));
        Assert.Equal((48, 64), Tools.ImageSize(png));

        var deep = Path.Combine(Folder(), "тест 2.png");
        Tools.Run(Tools.Magick, "-size", "20x10", "gradient:", "-depth", "16", deep);
        var png8 = Path.Combine(_dir.Path, "b.png");
        Assert.Equal((20, 10), await ImageProbe.FullAsync(Tools.Locator, deep, png8));
        Assert.Equal("8", Tools.Run(Tools.Magick, png8, "-format", "%z", "info:").Trim());
    }

    [SkippableFact]
    public async Task Not_a_picture_gives_null()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var input = Path.Combine(Folder(), "тест 1.png");
        File.WriteAllText(input, "не картинка");
        Assert.Null(await ImageProbe.FullAsync(Tools.Locator, input, Path.Combine(_dir.Path, "c.png")));
    }
}
