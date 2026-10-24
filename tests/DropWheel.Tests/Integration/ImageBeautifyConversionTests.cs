using DropWheel.Cli;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;

namespace DropWheel.Tests.Integration;

/// <summary>«Оформить…» настоящим ImageMagick (D-BEAUTIFY): «Мои фото/тест 1.*».</summary>
public class ImageBeautifyConversionTests : IDisposable
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

    private async Task<string> Beautify(string input, BeautifyStyle style)
    {
        var hash = Tools.Hash(input);
        var size = Tools.ImageSize(input);
        var output = await _converter.ConvertAsync(input, ImageBeautify.Bind(style, size.W, size.H));
        Assert.Equal(hash, Tools.Hash(input)); // исходник не тронут
        Assert.Empty(Directory.GetFileSystemEntries(Path.GetDirectoryName(input)!, ".dropwheel-*"));
        return output;
    }

    /// <summary>Цвет точки как «r,g,b,a» (a — 0…255), независимо от того, как ImageMagick назвал формат.</summary>
    private static int[] Rgba(string path, int x, int y) =>
        Tools.Run(Tools.Magick, path + "[0]", "-alpha", "set", "-depth", "8", "-format",
                $"%[fx:int(255*p{{{x},{y}}}.r+0.5)],%[fx:int(255*p{{{x},{y}}}.g+0.5)],%[fx:int(255*p{{{x},{y}}}.b+0.5)],%[fx:int(255*p{{{x},{y}}}.a+0.5)]", "info:")
            .Split(',').Select(int.Parse).ToArray();

    [SkippableFact]
    public async Task Png_result_is_bigger_by_two_paddings_next_to_the_source_and_repeats_get_a_number()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var input = Tools.MakeImage(Path.Combine(Folder(), "тест 1.jpg"), 320, 200);
        var output = await Beautify(input, BeautifyStyle.Default);
        Assert.Equal(Path.Combine(Folder(), "тест 1 (оформлено).png"), output);
        // 8 % от 200 = 16 с каждой стороны.
        Assert.Equal((352, 232), Tools.ImageSize(output));
        Assert.Equal("352x232+0+0", Tools.Run(Tools.Magick, output, "-format", "%g", "info:"));
        // Угол — градиент «Закат» (#ff9a8b), непрозрачный.
        var corner = Rgba(output, 0, 0);
        Assert.Equal(255, corner[3]);
        Assert.InRange(corner[0], 240, 255);

        var second = await Beautify(input, BeautifyStyle.Default);
        Assert.Equal(Path.Combine(Folder(), "тест 1 (оформлено) (1).png"), second);
    }

    [SkippableFact]
    public async Task Transparent_background_leaves_corners_transparent_and_the_middle_opaque()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var input = Path.Combine(Folder(), "тест 1.png");
        Tools.Run(Tools.Magick, "-size", "200x100", "xc:blue", input);
        var output = await Beautify(input, new BeautifyStyle("none", 10, 20, Shadow: false));
        Assert.Equal((220, 120), Tools.ImageSize(output));
        Assert.Equal(0, Rgba(output, 0, 0)[3]);     // поле вокруг
        Assert.Equal(0, Rgba(output, 10, 10)[3]);   // скруглённый угол картинки
        Assert.Equal(0, Rgba(output, 211, 111)[3]); // и нижний правый
        Assert.Equal([0, 0, 255, 255], Rgba(output, 110, 60));
        Assert.Equal(255, Rgba(output, 30, 10)[3]); // край картинки вне скругления
    }

    [SkippableFact]
    public async Task Shadow_darkens_the_padding_under_the_picture()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var input = Tools.MakeImage(Path.Combine(Folder(), "тест 1.png"), 200, 200);
        var with = await Beautify(input, new BeautifyStyle("white", 20, 0, Shadow: true));
        var without = await Beautify(input, new BeautifyStyle("white", 20, 0, Shadow: false));
        // Под картинкой (отступ 40 px), чуть ниже края — тень темнее белого фона.
        Assert.True(Rgba(with, 140, 244)[0] < 250, string.Join(",", Rgba(with, 140, 244)));
        Assert.Equal([255, 255, 255, 255], Rgba(without, 140, 244));
    }

    [SkippableFact]
    public async Task Preview_renders_the_same_template_on_a_small_copy()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var input = Tools.MakeImage(Path.Combine(Folder(), "тест 1.png"), 2000, 1000);
        var copy = Path.Combine(_dir.Path, "копия.png");
        Assert.Equal((2000, 1000), await ImageProbe.PreviewAsync(Tools.Locator, input, copy, side: ImageBeautify.PreviewSide));
        Assert.Equal((1000, 500), Tools.ImageSize(copy));
        Assert.Equal((1000, 500), ImageProbe.PngSize(File.ReadAllBytes(copy)));
        Assert.Null(ImageProbe.PngSize(new byte[30]));

        var style = ImageBeautify.ForPreview(BeautifyStyle.Default, 2000, 1000, 1000, 500);
        var render = Path.Combine(_dir.Path, "предпросмотр.png");
        var args = ArgumentBuilder.Build(ImageBeautify.Bind(style, 1000, 500), copy, render, ConversionSettings.Default);
        Assert.True(await ImageProbe.RenderAsync(Tools.Locator, args, render));
        Assert.Equal((1080, 580), Tools.ImageSize(render));
    }

    [SkippableFact]
    public async Task Cli_beautifies_with_style_and_with_defaults()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var input = Tools.MakeImage(Path.Combine(Folder(), "тест 1.png"), 120, 80);
        var code = await Program.RunAsync(["convert", input, "img-beautify", "--style", "bg=dark;pad=25;radius=8;shadow=0"],
            new StringWriter(), new StringWriter());
        Assert.Equal(0, code);
        Assert.Equal((160, 120), Tools.ImageSize(Path.Combine(Folder(), "тест 1 (оформлено).png")));

        Assert.Equal(0, await Program.RunAsync(["convert", input, "img-beautify"], new StringWriter(), new StringWriter()));
        Assert.Equal((132, 92), Tools.ImageSize(Path.Combine(Folder(), "тест 1 (оформлено) (1).png")));

        var error = new StringWriter();
        Assert.Equal(1, await Program.RunAsync(["convert", Path.Combine(Folder(), "нет такого.png"), "img-beautify"], new StringWriter(), error));
        Assert.Contains("нет такого.png", error.ToString());
    }

    [Fact]
    public async Task Cli_rejects_bad_style_options()
    {
        await Assert.ThrowsAsync<UsageException>(() =>
            Program.RunAsync(["convert", "a.jpg", "img-beautify", "--style", "bg=pink"], new StringWriter(), new StringWriter()));
        await Assert.ThrowsAsync<UsageException>(() =>
            Program.RunAsync(["convert", "a.jpg", "to-png", "--style", "pad=5"], new StringWriter(), new StringWriter()));
        await Assert.ThrowsAsync<UsageException>(() =>
            Program.RunAsync(["convert", "a.jpg", "img-beautify", "--style"], new StringWriter(), new StringWriter()));
    }
}
