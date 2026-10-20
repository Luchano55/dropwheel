using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;

namespace DropWheel.Tests.Integration;

/// <summary>Дополнения: ICO, SVG/PSD/RAW на входе, GIF → видео, субтитры.</summary>
public class ExtrasConversionTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly Converter _converter = new(Tools.Locator, ConversionSettings.Default);

    public void Dispose() => _dir.Dispose();

    private async Task<string> Convert(string input, string opId)
    {
        var hash = Tools.Hash(input);
        var output = await _converter.ConvertAsync(input, OperationCatalog.Find(opId)!);
        Assert.True(new FileInfo(output).Length > 0);
        Assert.Equal(hash, Tools.Hash(input));
        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path, ".dropwheel-*"));
        return output;
    }

    /// <summary>По строке на кадр: «ширина высота альфа-в-углу».</summary>
    private static string[] Frames(string path) =>
        Tools.Run(Tools.Magick, path, "-format", "%w %h %[fx:p{0,0}.a]\n", "info:")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool CanRead(string file)
    {
        try
        {
            Tools.Run(Tools.Magick, file + "[0]", "-format", "%w", "info:");
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // --- ICO ---

    [SkippableTheory]
    [InlineData("тест 1.png")]
    [InlineData("тест 1.jpg")]
    public async Task Ico_has_all_sizes_and_transparent_margins(string name)
    {
        Skip.IfNot(Tools.HasMagick && Tools.HasFfmpeg, "нет ImageMagick или ffmpeg");
        var src = Tools.MakeImage(Path.Combine(_dir.Path, name), 300, 200); // не квадрат: сверху и снизу поля

        var output = await Convert(src, "img-ico");

        Assert.Equal(Path.Combine(_dir.Path, "тест 1.ico"), output);
        Assert.Equal(["256 256 0", "128 128 0", "64 64 0", "48 48 0", "32 32 0", "16 16 0"], Frames(output));
    }

    [SkippableFact]
    public async Task Ico_keeps_transparency_of_png()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var src = Path.Combine(_dir.Path, "круг.png");
        Tools.Run(Tools.Magick, "-size", "64x64", "xc:none", "-fill", "red", "-draw", "circle 32,32 32,2", src);

        var frames = Frames(await Convert(src, "img-ico"));

        Assert.Equal("256 256 0", frames[0]); // угол прозрачный — квадрат не залит фоном
    }

    // --- SVG, PSD, RAW на входе ---

    [SkippableFact]
    public async Task Svg_is_rasterized()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var src = _dir.File("логотип.svg",
            """<svg xmlns="http://www.w3.org/2000/svg" width="120" height="80"><rect x="10" y="10" width="100" height="60" fill="red"/></svg>""");
        Skip.IfNot(CanRead(src), "эта сборка ImageMagick не читает SVG (на Linux нужен rsvg-convert)");

        // Размер — «свой» (120×80 px); если сборка рисует SVG с другой плотностью — пропорции те же.
        var (w, h) = Tools.ImageSize(await Convert(src, "to-png"));
        Assert.True(w >= 120 && w * 2 == h * 3, $"{w}×{h}");
        // Инструменты «того же формата» для SVG дают JPG.
        Assert.Equal(Path.Combine(_dir.Path, "логотип (50%).jpg"), await Convert(src, "img-half"));
        Assert.Equal("256 256 0", Frames(await Convert(src, "img-ico"))[0]);
    }

    [SkippableFact]
    public async Task Psd_uses_flattened_image()
    {
        Skip.IfNot(Tools.HasMagick && Tools.HasFfmpeg, "нет ImageMagick или ffmpeg");
        var png = Tools.MakeImage(Path.Combine(_dir.Path, "исходник.png"), 320, 240);
        var src = Path.Combine(_dir.Path, "макет.psd");
        Tools.Run(Tools.Magick, png, src);

        Assert.Equal((320, 240), Tools.ImageSize(await Convert(src, "to-png")));
        var half = await Convert(src, "img-half");
        Assert.Equal(Path.Combine(_dir.Path, "макет (50%).jpg"), half);
        Assert.Equal((160, 120), Tools.ImageSize(half));
    }

    [SkippableTheory]
    [InlineData(1, 64, 48)]
    [InlineData(6, 48, 64)] // «повернуть на 90°» из тега Orientation — -auto-orient поворачивает ровно один раз
    public async Task Dng_raw_is_developed(int orientation, int w, int h)
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var src = Path.Combine(_dir.Path, "снимок.dng");
        File.WriteAllBytes(src, Dng.LinearRaw(64, 48, orientation));
        Skip.IfNot(CanRead(src), "эта сборка ImageMagick не читает RAW (нет LibRaw)");

        var jpg = await Convert(src, "to-jpg");
        Assert.Equal(Path.Combine(_dir.Path, "снимок.jpg"), jpg);
        Assert.Equal((w, h), Tools.ImageSize(jpg));
        Assert.Equal(Path.Combine(_dir.Path, "снимок (без метаданных).jpg"), await Convert(src, "img-strip"));
    }

    // --- GIF → видео ---

    private string AnimatedGif(string name)
    {
        var path = Path.Combine(_dir.Path, name);
        Tools.Run(Tools.Ffmpeg, "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc=size=161x91:rate=10:duration=1.5",
            "-vf", "split[a][b];[a]palettegen[p];[b][p]paletteuse", "-loop", "0", path);
        return path;
    }

    private static int FrameCount(string path) =>
        int.Parse(Tools.Run(Tools.Ffprobe, "-v", "error", "-count_frames", "-select_streams", "v:0",
            "-show_entries", "stream=nb_read_frames", "-of", "csv=p=0", path));

    [SkippableTheory]
    [InlineData("gif-to-mp4", "тест 1.mp4", "h264x160x90")]  // нечётные 161×91 → чётные
    [InlineData("gif-to-webm", "тест 1.webm", "vp9x160x90")]
    public async Task Animated_gif_becomes_video(string opId, string expectedName, string stream)
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var src = AnimatedGif("тест 1.gif");

        var output = await Convert(src, opId);

        Assert.Equal(Path.Combine(_dir.Path, expectedName), output);
        Assert.Equal([stream], Tools.Streams(output)); // одна дорожка — без звука
        Assert.Equal(15, FrameCount(output));
        Assert.Contains("yuv420p", Tools.Run(Tools.Ffprobe, "-v", "error", "-show_entries", "stream=pix_fmt", "-of", "csv=p=0", output));
    }

    // --- Субтитры ---

    [SkippableFact]
    public async Task Subtitles_are_extracted_to_srt_next_to_video()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var srt = _dir.File("исходные.srt",
            "1\n00:00:00,000 --> 00:00:01,000\nПривет, мир\n\n2\n00:00:01,000 --> 00:00:02,000\nВторая строка\n");
        var src = Path.Combine(_dir.Path, "тест 1.mkv");
        Tools.Run(Tools.Ffmpeg, "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc=size=160x90:rate=25:duration=2",
            "-i", srt, "-map", "0:v", "-map", "1:s", "-c:v", "ffv1", "-c:s", "srt", src);

        var output = await Convert(src, "video-subs");

        Assert.Equal(Path.Combine(_dir.Path, "тест 1.srt"), output); // без пометки: плееры подхватят сами
        var text = File.ReadAllText(output);
        Assert.Contains("00:00:01,000 --> 00:00:02,000", text);
        Assert.Contains("Привет, мир", text);
        Assert.Contains("Вторая строка", text);
    }

    [SkippableFact]
    public async Task Video_without_subtitles_fails_clearly()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var src = Tools.MakeVideo(Path.Combine(_dir.Path, "без субтитров.mkv"), 160, 90, seconds: 1);

        var ex = await Assert.ThrowsAsync<ConversionException>(() =>
            _converter.ConvertAsync(src, OperationCatalog.Find("video-subs")!));

        Assert.Contains("дорожки", ex.Message);
        Assert.Single(Directory.GetFiles(_dir.Path));
    }

    [Fact]
    public void Bitmap_subtitles_error_is_translated()
    {
        const string stderr = "Subtitle encoding currently only possible from text to text or bitmap to bitmap";
        Assert.Contains("картинками", ErrorTranslator.Translate(EngineKind.FFmpeg, stderr));
    }
}

/// <summary>
/// Минимальный DNG (LinearRaw, 16 бит RGB без сжатия) — чтобы проверить путь RAW → JPG без настоящего снимка.
/// Настоящие CR2/NEF/ARW ImageMagick читает той же библиотекой (LibRaw).
/// </summary>
internal static class Dng
{
    public static byte[] LinearRaw(int width, int height, int orientation)
    {
        var pixels = new byte[width * height * 6];
        for (int y = 0, i = 0; y < height; y++)
            for (var x = 0; x < width; x++, i += 6)
            {
                BitConverter.TryWriteBytes(pixels.AsSpan(i), (ushort)(65535 * x / (width - 1)));
                BitConverter.TryWriteBytes(pixels.AsSpan(i + 2), (ushort)(65535 * y / (height - 1)));
                BitConverter.TryWriteBytes(pixels.AsSpan(i + 4), (ushort)20000);
            }

        byte[] model = "DropWheel Test\0"u8.ToArray();
        var tags = new List<(ushort Tag, ushort Type, int Count, byte[] Data)>
        {
            (254, 4, 1, U32(0)),                       // NewSubfileType: основное изображение
            (256, 4, 1, U32(width)),
            (257, 4, 1, U32(height)),
            (258, 3, 3, U16(16, 16, 16)),              // BitsPerSample
            (259, 3, 1, U16(1)),                       // без сжатия
            (262, 3, 1, U16(34892)),                   // LinearRaw
            (271, 2, 10, "DropWheel\0"u8.ToArray()),   // Make
            (272, 2, model.Length, model),             // Model
            (273, 4, 1, U32(0)),                       // StripOffsets — ниже
            (274, 3, 1, U16(orientation)),
            (277, 3, 1, U16(3)),                       // SamplesPerPixel
            (278, 4, 1, U32(height)),                  // RowsPerStrip
            (279, 4, 1, U32(pixels.Length)),           // StripByteCounts
            (284, 3, 1, U16(1)),                       // PlanarConfiguration
            (50706, 1, 4, [1, 4, 0, 0]),               // DNGVersion
            (50707, 1, 4, [1, 1, 0, 0]),               // DNGBackwardVersion
            (50708, 2, model.Length, model),           // UniqueCameraModel
            (50717, 4, 3, U32(65535, 65535, 65535)),   // WhiteLevel
            (50721, 10, 9, U32(1, 1, 0, 1, 0, 1, 0, 1, 1, 1, 0, 1, 0, 1, 0, 1, 1, 1)), // ColorMatrix1 = единичная
            (50728, 5, 3, U32(1, 1, 1, 1, 1, 1)),      // AsShotNeutral
            (50778, 3, 1, U16(21)),                    // CalibrationIlluminant1 = D65
        };

        const int ifdOffset = 8;
        var dataOffset = ifdOffset + 2 + 12 * tags.Count + 4;
        var extra = new List<byte>();
        var offsets = new int[tags.Count];
        for (var i = 0; i < tags.Count; i++)
        {
            if (tags[i].Data.Length <= 4) continue;
            offsets[i] = dataOffset + extra.Count;
            extra.AddRange(tags[i].Data);
            if (extra.Count % 2 == 1) extra.Add(0);
        }
        var pixelOffset = dataOffset + extra.Count;

        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("II*\0"u8);
        w.Write(ifdOffset);
        w.Write((ushort)tags.Count);
        for (var i = 0; i < tags.Count; i++)
        {
            var (tag, type, count, data) = tags[i];
            if (tag == 273) data = U32(pixelOffset);
            w.Write(tag);
            w.Write(type);
            w.Write(count);
            if (data.Length <= 4) w.Write(data.Concat(new byte[4 - data.Length]).ToArray());
            else w.Write(offsets[i]);
        }
        w.Write(0); // следующего IFD нет
        w.Write(extra.ToArray());
        w.Write(pixels);
        w.Flush();
        return stream.ToArray();
    }

    private static byte[] U16(params int[] values) => values.SelectMany(v => BitConverter.GetBytes((ushort)v)).ToArray();
    private static byte[] U32(params int[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();
}
