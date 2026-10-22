using DropWheel.Conversions;
using DropWheel.Jobs;
using DropWheel.Shell;

namespace DropWheel.Tests.Integration;

/// <summary>Картинки из браузера (D-BROWSER) на настоящих файлах: формат по байтам, CF_DIB → BMP, сохранение и конвертация.</summary>
public class BrowserImageTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private byte[] Make(string ext)
    {
        var path = Path.Combine(_dir.Path, "исходник." + ext);
        Tools.Run(Tools.Magick, "-size", "40x30", "gradient:red-blue", path);
        return File.ReadAllBytes(path);
    }

    [SkippableTheory]
    [InlineData("jpg")]
    [InlineData("png")]
    [InlineData("webp")]
    [InlineData("gif")]
    [InlineData("bmp")]
    [InlineData("tif")]
    [InlineData("avif")]
    public void Real_files_are_recognised(string ext)
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        Skip.If(ext is "avif" or "webp" && !Tools.CanWrite(ext), $"эта сборка ImageMagick не пишет {ext}");
        Assert.Equal(ext, BrowserDrag.SniffExtension(Make(ext)));
    }

    [Fact]
    public void Heic_fixture_is_recognised() =>
        Assert.Equal("heic", BrowserDrag.SniffExtension(File.ReadAllBytes(Tools.Fixture("sample.heic"))));

    [SkippableFact]
    public void Dib_from_clipboard_becomes_readable_bmp()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        // BMP3 — классический BITMAPINFOHEADER (40 байт), как CF_DIB; без 14 байт заголовка файла это и есть DIB.
        var bmpPath = Path.Combine(_dir.Path, "исходник.bmp");
        Tools.Run(Tools.Magick, "-size", "40x30", "gradient:red-blue", "-type", "TrueColor", "BMP3:" + bmpPath);
        var original = File.ReadAllBytes(bmpPath);

        var restored = BrowserDrag.DibToBmp(original.AsSpan(14))!;
        Assert.Equal(original[..14], restored[..14]);

        var again = Path.Combine(_dir.Path, "из буфера.bmp");
        File.WriteAllBytes(again, restored);
        Assert.Equal((40, 30), Tools.ImageSize(again));
    }

    [SkippableFact]
    public async Task Webp_named_jpg_is_saved_as_webp_and_converts()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        Skip.IfNot(Tools.CanWrite("webp"), "эта сборка ImageMagick не пишет webp");
        // Сайт отдал WebP, а Chrome назвал файл «фото с сайта.jpg».
        var content = BrowserDrag.Resolve(new DragSource
        {
            LooksLikeBrowser = true,
            VirtualFile = () => ("фото с сайта.jpg", Make("webp")),
        });
        var image = content.Image!;
        var folder = Path.Combine(_dir.Path, "Картинки");
        Directory.CreateDirectory(folder);
        var path = BrowserDrag.ImagePath(folder, image, DateTime.Now);
        Assert.Equal(Path.Combine(folder, "фото с сайта.webp"), path);
        await File.WriteAllBytesAsync(path, image.Bytes);

        // Колесо для этой картинки — как для файла .webp: есть «→ JPG», нет «→ WebP».
        var ops = OperationCatalog.ForFiles(["image." + image.Extension]);
        Assert.Contains(ops, o => o.Id == "to-jpg");
        Assert.DoesNotContain(ops, o => o.Id == "to-webp");

        var output = await new Converter(Tools.Locator, ConversionSettings.Default).ConvertAsync(path, OperationCatalog.Find("to-jpg")!);
        Assert.Equal(Path.Combine(folder, "фото с сайта.jpg"), output);
        Assert.Equal((40, 30), Tools.ImageSize(output));

        // Второй раз то же имя — «(1)».
        Assert.Equal(Path.Combine(folder, "фото с сайта (1).webp"), BrowserDrag.ImagePath(folder, image, DateTime.Now));
    }
}
