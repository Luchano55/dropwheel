using DropWheel.Cli;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;

namespace DropWheel.Tests.Integration;

/// <summary>Обрезка картинки настоящим ImageMagick (D-CROP): «Мои фото/тест 1.*».</summary>
public class ImageCropConversionTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly Converter _converter = new(Tools.Locator, ConversionSettings.Default);

    public void Dispose() => _dir.Dispose();

    private string Folder()
    {
        var folder = Path.Combine(_dir.Path, "Мои фото");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private async Task<string> Crop(string input, CropRect rect)
    {
        var hash = Tools.Hash(input);
        var size = Tools.ImageSize(input);
        var output = await _converter.ConvertAsync(input, ImageCrop.Bind(rect, size.W, size.H));
        Assert.Equal(hash, Tools.Hash(input)); // исходник не тронут
        Assert.Empty(Directory.GetFileSystemEntries(Path.GetDirectoryName(input)!, ".dropwheel-*"));
        return output;
    }

    private static string Pixel(string path, int x, int y) =>
        Tools.Run(Tools.Magick, path + "[0]", "-format", $"%[pixel:p{{{x},{y}}}]", "info:");

    [SkippableFact]
    public async Task Jpg_is_cropped_to_the_exact_size_next_to_the_source()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var input = Tools.MakeImage(Path.Combine(Folder(), "тест 1.jpg"), 320, 240);
        var output = await Crop(input, new CropRect(10, 20, 100, 50));
        Assert.Equal(Path.Combine(Folder(), "тест 1 (обрезано).jpg"), output);
        Assert.Equal((100, 50), Tools.ImageSize(output));

        // Ещё раз — свободное имя.
        var second = await Crop(input, new CropRect(0, 0, 30, 30));
        Assert.Equal(Path.Combine(Folder(), "тест 1 (обрезано) (1).jpg"), second);
    }

    [SkippableFact]
    public async Task Png_keeps_transparency_and_no_page_offset()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var input = Path.Combine(Folder(), "тест 1.png");
        // 40×30: левая половина синяя, правая — прозрачная.
        Tools.Run(Tools.Magick, "-size", "40x30", "xc:none", "-fill", "blue", "-draw", "rectangle 0,0 19,29", input);
        var output = await Crop(input, new CropRect(10, 5, 20, 10));
        Assert.Equal(Path.Combine(Folder(), "тест 1 (обрезано).png"), output);
        Assert.Equal((20, 10), Tools.ImageSize(output));
        Assert.StartsWith("srgba(0,0,255,1", Pixel(output, 0, 0));
        Assert.EndsWith(",0)", Pixel(output, 19, 0)); // прозрачная часть осталась прозрачной
        Assert.Equal("20x10+0+0", Tools.Run(Tools.Magick, output, "-format", "%g", "info:")); // +repage
    }

    [SkippableFact]
    public async Task Exif_rotated_photo_is_cropped_as_shown()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        // 64×48, синий квадрат 10×10 в левом верхнем углу; EXIF «повернуть на 90° по часовой» → на экране 48×64,
        // квадрат — в правом верхнем углу.
        var plain = Path.Combine(_dir.Path, "plain.jpg");
        Tools.Run(Tools.Magick, "-size", "64x48", "xc:red", "-fill", "blue", "-draw", "rectangle 0,0 9,9", "-quality", "95", plain);
        var input = Path.Combine(Folder(), "тест 1.jpg");
        File.WriteAllBytes(input, WithExifOrientation(File.ReadAllBytes(plain), 6));

        var png = Path.Combine(_dir.Path, "показ.png");
        Assert.Equal((48, 64), await ImageProbe.PreviewAsync(Tools.Locator, input, png));
        Assert.Equal((48, 64), Tools.ImageSize(png));
        Assert.Equal((48, 64), await ImageProbe.SizeAsync(Tools.Locator, input));

        var output = await _converter.ConvertAsync(input, ImageCrop.Bind(new CropRect(38, 0, 10, 10), 48, 64));
        Assert.Equal((10, 10), Tools.ImageSize(output));
        var p = Pixel(output, 5, 5).Replace("srgb(", "").TrimEnd(')').Split(',').Select(int.Parse).ToArray();
        Assert.True(p[2] > 200 && p[0] < 60, string.Join(",", p)); // синий
    }

    [SkippableFact]
    public async Task Preview_shrinks_big_pictures()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var input = Tools.MakeImage(Path.Combine(Folder(), "тест 1.png"), 2000, 1000);
        var png = Path.Combine(_dir.Path, "показ.png");
        Assert.Equal((2000, 1000), await ImageProbe.PreviewAsync(Tools.Locator, input, png));
        Assert.Equal((1600, 800), Tools.ImageSize(png));
        Assert.Null(await ImageProbe.PreviewAsync(Tools.Locator, Path.Combine(_dir.Path, "нет такого.png"), png + "2.png"));
    }

    [SkippableFact]
    public async Task Cli_crops_with_geometry_and_clamps_to_the_picture()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var input = Tools.MakeImage(Path.Combine(Folder(), "тест 1.png"), 120, 80);
        var code = await Program.RunAsync(["convert", input, "img-crop", "--crop", "100x100+40+30"], new StringWriter(), new StringWriter());
        Assert.Equal(0, code);
        Assert.Equal((80, 50), Tools.ImageSize(Path.Combine(Folder(), "тест 1 (обрезано).png")));

        await Assert.ThrowsAsync<UsageException>(() =>
            Program.RunAsync(["convert", input, "img-crop", "--crop", "10x10+500+0"], new StringWriter(), new StringWriter()));
    }

    [Fact]
    public async Task Cli_rejects_bad_crop_options()
    {
        await Assert.ThrowsAsync<UsageException>(() =>
            Program.RunAsync(["convert", "a.jpg", "img-crop"], new StringWriter(), new StringWriter()));
        await Assert.ThrowsAsync<UsageException>(() =>
            Program.RunAsync(["convert", "a.jpg", "img-crop", "--crop", "abc"], new StringWriter(), new StringWriter()));
        await Assert.ThrowsAsync<UsageException>(() =>
            Program.RunAsync(["convert", "a.jpg", "to-png", "--crop", "10x10"], new StringWriter(), new StringWriter()));
        await Assert.ThrowsAsync<UsageException>(() =>
            Program.RunAsync(["convert", "a.jpg", "img-crop", "--crop", "10x10", "--start", "1"], new StringWriter(), new StringWriter()));
    }

    /// <summary>JPEG с EXIF, где только тег Orientation (вставляется сразу после SOI).</summary>
    internal static byte[] WithExifOrientation(byte[] jpeg, ushort orientation)
    {
        var tiff = new List<byte> { (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0, 1, 0 };
        tiff.AddRange([0x12, 0x01, 3, 0, 1, 0, 0, 0, (byte)orientation, (byte)(orientation >> 8), 0, 0, 0, 0, 0, 0]);
        var app = new List<byte>("Exif\0\0"u8.ToArray());
        app.AddRange(tiff);
        var length = app.Count + 2;
        return [jpeg[0], jpeg[1], 0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. app, .. jpeg[2..]];
    }
}
