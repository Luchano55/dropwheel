using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;

namespace DropWheel.Tests;

/// <summary>Каталог дополнений: OCR, ICO, SVG/PSD/RAW, GIF → видео, субтитры, «В UTF-8».</summary>
public class ExtrasCatalogTests
{
    private static List<string> Ids(string file) => OperationCatalog.ForFile(file).Select(o => o.Id).ToList();

    [Fact]
    public void Every_extension_fits_the_wheel_and_labels_fit_a_sector()
    {
        foreach (var ext in Extensions.All.Concat(TextEngine.InputExtensions))
            foreach (var group in OperationCatalog.ForFile("file." + ext).GroupBy(o => o.Mode))
                Assert.True(group.Count() <= 8, $"{ext} {group.Key}: {string.Join(", ", group.Select(o => o.Id))}");
        Assert.All(OperationCatalog.All, o => Assert.True(o.Label.Length <= 16, o.Label));
    }

    [Theory]
    [InlineData("logo.svg")]
    [InlineData("макет.PSD")]
    [InlineData("IMG_0001.CR2")]
    [InlineData("IMG_0001.cr3")]
    [InlineData("DSC_0001.nef")]
    [InlineData("DSC01.arw")]
    [InlineData("photo.dng")]
    [InlineData("DSCF0001.raf")]
    [InlineData("P1010001.orf")]
    [InlineData("P1000001.rw2")]
    public void New_image_inputs_get_image_operations(string file)
    {
        Assert.Equal(MediaKind.Image, Extensions.KindOf(Path.GetExtension(file)));
        var ids = Ids(file);
        Assert.Equal(["to-jpg", "to-png", "to-webp", "to-avif", "to-pdf", "img-ico"],
            OperationCatalog.ForFile(file).Where(o => o.Mode == OperationMode.Convert).Select(o => o.Id));
        Assert.Contains("img-compress", ids);
        // Свой формат они не пишут — инструменты дают JPG.
        foreach (var tool in new[] { "img-compress", "img-half", "img-1920", "img-strip" })
            Assert.Equal("jpg", OperationCatalog.Find(tool)!.Output.Resolve(Path.GetExtension(file)));
    }

    [Fact]
    public void Ico_is_a_conversion_with_transparent_square()
    {
        var ico = OperationCatalog.Find("img-ico")!;
        Assert.Equal((OperationMode.Convert, MediaKind.Image, EngineKind.ImageMagick, "ico"),
            (ico.Mode, ico.Kind, ico.Engine, ico.Output.Resolve(".png")));
        var args = ArgumentBuilder.Build(ico, "/a/лого 1.png", "/a/out.ico", ConversionSettings.Default);
        Assert.Equal("/a/лого 1.png[0]", args[0]);
        Assert.Equal("none", args[args.ToList().IndexOf("-background") + 1]);
        Assert.Contains("icon:auto-resize=256,128,64,48,32,16", args);
        Assert.DoesNotContain("remove", args); // прозрачность не заливается белым, как у JPG
        Assert.Equal("/a/out.ico", args[^1]);
    }

    [Theory]
    [InlineData("скан.jpg", true)]
    [InlineData("скан.JPEG", true)]
    [InlineData("скриншот.png", true)]
    [InlineData("скан.bmp", true)]
    [InlineData("скан.tif", true)]
    [InlineData("скан.tiff", true)]
    [InlineData("фото.heic", false)]
    [InlineData("фото.webp", false)]
    [InlineData("анимация.gif", false)]
    [InlineData("лого.svg", false)]
    [InlineData("снимок.cr2", false)]
    public void Ocr_only_for_formats_windows_decodes(string file, bool offered)
    {
        Assert.Equal(offered, Ids(file).Contains(OperationCatalog.OcrId));
    }

    [Fact]
    public void Ocr_shape_and_hidden_without_engine()
    {
        var ocr = OperationCatalog.Find(OperationCatalog.OcrId)!;
        Assert.Equal((OperationMode.Convert, EngineKind.Ocr, "txt", ""), (ocr.Mode, ocr.Engine, ocr.Output.Resolve(".png"), ocr.Suffix));
        // На Linux (CLI) движка OCR нет — операция не предлагается.
        var converter = new Converter(new ToolLocator(), ConversionSettings.Default, [new ZipEngine(), new TextEngine()]);
        Assert.False(converter.CanRun(ocr, "png"));
        Assert.DoesNotContain(OperationCatalog.OcrId,
            OperationCatalog.ForFiles(["скан.png"], converter.CanRun).Select(o => o.Id));
    }

    [Fact]
    public void Gif_gets_video_conversions_others_do_not()
    {
        var gif = Ids("анимация.GIF");
        Assert.Contains("gif-to-mp4", gif);
        Assert.Contains("gif-to-webm", gif);
        Assert.DoesNotContain("gif-to-mp4", Ids("фото.png"));
        Assert.DoesNotContain("gif-to-webm", Ids("клип.mkv"));
        Assert.DoesNotContain("to-mp4", gif); // видео-операции не путаются с GIF-ными
    }

    [Theory]
    [InlineData("gif-to-mp4", "libx264", "mp4")]
    [InlineData("gif-to-webm", "libvpx-vp9", "webm")]
    public void Gif_to_video_args(string opId, string codec, string ext)
    {
        var op = OperationCatalog.Find(opId)!;
        var args = ArgumentBuilder.Build(op, "/a/анимация 1.gif", "/a/out." + ext, ConversionSettings.Default with { VideoCrf = 20 }).ToList();
        Assert.Equal("/a/анимация 1.gif", args[args.IndexOf("-i") + 1]);
        Assert.Contains("-an", args);
        Assert.Equal(codec, args[args.IndexOf("-c:v") + 1]);
        Assert.Equal("yuv420p", args[args.IndexOf("-pix_fmt") + 1]);
        Assert.Equal("scale=trunc(iw/2)*2:trunc(ih/2)*2", args[args.IndexOf("-vf") + 1]);
        if (ext == "mp4")
        {
            Assert.Equal("20", args[args.IndexOf("-crf") + 1]); // CRF из настроек
            Assert.Equal("+faststart", args[args.IndexOf("-movflags") + 1]);
        }
    }

    [Fact]
    public void Subtitles_tool_for_video()
    {
        var subs = OperationCatalog.Find("video-subs")!;
        Assert.Equal((OperationMode.Tool, MediaKind.Video, "srt", ""), (subs.Mode, subs.Kind, subs.Output.Resolve(".mkv"), subs.Suffix));
        Assert.Contains("video-subs", Ids("фильм.mkv"));
        Assert.Contains("video-subs", Ids("фильм.mp4"));
        var args = ArgumentBuilder.Build(subs, "/v/фильм 1.mkv", "/v/out.srt", ConversionSettings.Default);
        Assert.Equal(["-map", "0:s:0", "-c:s", "srt", "/v/out.srt"], args.TakeLast(5));
    }
}
