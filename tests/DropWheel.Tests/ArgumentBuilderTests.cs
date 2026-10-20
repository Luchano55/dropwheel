using DropWheel.Conversions;
using DropWheel.Engines;

namespace DropWheel.Tests;

public class ArgumentBuilderTests
{
    private const string In = "/data/Мои фото/тест 1.heic";
    private static readonly ConversionSettings S = ConversionSettings.Default;

    private static IReadOnlyList<string> Build(string opId, string input = In, string? output = null)
    {
        var op = OperationCatalog.Find(opId)!;
        output ??= "/data/Мои фото/out." + op.Output.Resolve(Path.GetExtension(input));
        return ArgumentBuilder.Build(op, input, output, S);
    }

    public static TheoryData<string> AllOperations()
    {
        var data = new TheoryData<string>();
        // Только внешние утилиты: встроенные движки (ZIP, Office, OCR, текст…) работают без аргументов процесса.
        foreach (var op in OperationCatalog.All.Where(o => o.Engine is EngineKind.FFmpeg or EngineKind.ImageMagick
                     or EngineKind.Qpdf or EngineKind.Pdftoppm or EngineKind.Pdftotext or EngineKind.SevenZip))
            data.Add(op.Id);
        return data;
    }

    private static string SampleInput(string opId) => OperationCatalog.Find(opId)!.Kind switch
    {
        MediaKind.Image => "/data/Мои фото/тест 1.heic",
        MediaKind.Video => "/data/Мои фото/тест 1.mkv",
        MediaKind.Pdf => "/data/Мои фото/тест 1.pdf",
        _ => "/data/Мои фото/тест 1.wav",
    };

    [Theory]
    [MemberData(nameof(AllOperations))]
    public void Every_operation_builds_without_leftover_placeholders(string opId)
    {
        var args = Build(opId, SampleInput(opId));
        Assert.DoesNotContain(args, a => a.Contains('{') || a.Contains('}'));
        Assert.DoesNotContain(args, string.IsNullOrWhiteSpace);
    }

    [Theory]
    [MemberData(nameof(AllOperations))]
    public void Paths_with_spaces_and_cyrillic_stay_single_arguments(string opId)
    {
        var input = SampleInput(opId);
        var args = Build(opId, input);
        Assert.Contains(args, a => a == input || a == input + "[0]");
        Assert.StartsWith("/data/Мои фото/out.", args[^1]);
    }

    [Fact]
    public void Magick_reads_first_frame_and_writes_output_last()
    {
        var args = Build("to-jpg");
        Assert.Equal(In + "[0]", args[0]);
        Assert.Equal("/data/Мои фото/out.jpg", args[^1]);
        Assert.Contains("-auto-orient", args);
        Assert.Equal(S.JpegQuality.ToString(), args[args.ToList().IndexOf("-quality") + 1]);
    }

    [Fact]
    public void Jpeg_output_gets_white_background_png_does_not()
    {
        Assert.Contains("remove", Build("to-jpg"));
        Assert.DoesNotContain("remove", Build("to-png"));
        Assert.DoesNotContain("remove", Build("to-webp"));
    }

    [Fact]
    public void Ffmpeg_has_input_progress_and_output()
    {
        var input = "/v/my clip.mov";
        var args = Build("to-mp4", input).ToList();
        Assert.Equal(input, args[args.IndexOf("-i") + 1]);
        Assert.Equal("pipe:1", args[args.IndexOf("-progress") + 1]);
        Assert.Contains("libx264", args);
        Assert.Equal(S.VideoCrf.ToString(), args[args.IndexOf("-crf") + 1]);
        Assert.EndsWith(".mp4", args[^1]);
    }

    [Fact]
    public void Video_compress_uses_crf_28()
    {
        var args = Build("video-compress", "/v/a.mov").ToList();
        Assert.Equal("28", args[args.IndexOf("-crf") + 1]);
    }

    [Fact]
    public void Settings_are_applied()
    {
        var op = OperationCatalog.Find("to-mp3")!;
        var args = ArgumentBuilder.Build(op, "/a/x.wav", "/a/x.mp3", S with { Mp3BitrateKbps = 320 }).ToList();
        Assert.Equal("320k", args[args.IndexOf("-b:a") + 1]);
    }

    [Theory]
    [InlineData("mp3", "libmp3lame")]
    [InlineData("m4a", "aac")]
    [InlineData("wav", "pcm_s16le")]
    [InlineData("flac", "flac")]
    [InlineData("ogg", "libvorbis")]
    [InlineData("opus", "libopus")]
    public void Normalize_keeps_format_with_matching_codec(string ext, string codec)
    {
        var args = Build("audio-normalize", "/a/song." + ext).ToList();
        Assert.Equal(codec, args[args.IndexOf("-c:a") + 1]);
        Assert.EndsWith("." + ext, args[^1]);
    }

    // --- PDF и несколько файлов ---

    private const string Pdf1 = "/data/Мои документы/тест 1.pdf";
    private const string Pdf2 = "/data/Мои документы/второй файл.pdf";
    private const string OutDir = "/data/Мои документы/.dropwheel-abc";

    private static IReadOnlyList<string> BuildMany(string opId, IReadOnlyList<string> inputs, string output) =>
        ArgumentBuilder.Build(OperationCatalog.Find(opId)!, inputs, output, S);

    [Fact]
    public void Pdf_to_png_writes_pages_into_folder()
    {
        Assert.Equal(["-r", "150", "-png", Pdf1, Path.Combine(OutDir, "стр")], BuildMany("pdf-to-png", [Pdf1], OutDir));
    }

    [Fact]
    public void Pdf_to_jpg_writes_pages_into_folder()
    {
        Assert.Equal(["-r", "150", "-jpeg", "-jpegopt", "quality=90", Pdf1, Path.Combine(OutDir, "стр")],
            BuildMany("pdf-to-jpg", [Pdf1], OutDir));
    }

    [Fact]
    public void Pdf_merge_expands_all_inputs_one_argument_each()
    {
        const string output = "/data/Мои документы/.dropwheel-abc.pdf";
        Assert.Equal(["--empty", "--pages", Pdf1, Pdf2, Pdf1, "--", output],
            BuildMany("pdf-merge", [Pdf1, Pdf2, Pdf1], output));
    }

    [Fact]
    public void Pdf_split_writes_pages_into_folder()
    {
        Assert.Equal(["--split-pages", Pdf1, Path.Combine(OutDir, "стр.pdf")], BuildMany("pdf-split", [Pdf1], OutDir));
    }

    [Fact]
    public void Pdf_optimize_is_lossless_qpdf()
    {
        const string output = "/data/Мои документы/.dropwheel-abc.pdf";
        Assert.Equal(
            ["--object-streams=generate", "--compress-streams=y", "--recompress-flate", "--compression-level=9", Pdf1, output],
            BuildMany("pdf-optimize", [Pdf1], output));
    }

    [Fact]
    public void Images_to_pdf_reads_first_frame_of_every_input()
    {
        string[] inputs = ["/data/Мои фото/тест 1.heic", "/data/Мои фото/второе фото.png", "/data/Мои фото/3.jpg"];
        const string output = "/data/Мои фото/.dropwheel-abc.pdf";
        var args = BuildMany("images-to-pdf", inputs, output);
        Assert.Equal(inputs.Select(i => i + "[0]"), args.Take(3));
        Assert.Equal(["-auto-orient", "-compress", "jpeg", "-quality", S.JpegQuality.ToString()], args.Skip(3).Take(5));
        Assert.Contains("remove", args); // прозрачность — на белом
        Assert.Equal(output, args[^1]);
    }

    [Fact]
    public void Per_file_magick_reads_only_first_input()
    {
        // Обычный to-pdf (PerFile) не меняется: один вход, [0], белый фон.
        var args = BuildMany("to-pdf", ["/a/x 1.png", "/a/лишний.png"], "/a/out.pdf");
        Assert.Equal("/a/x 1.png[0]", args[0]);
        Assert.DoesNotContain(args, a => a.Contains("лишний"));
        Assert.Contains("remove", args);
    }

    [Theory]
    [InlineData(EngineKind.Qpdf, 0, true)]
    [InlineData(EngineKind.Qpdf, 3, true)]   // предупреждения, файл записан
    [InlineData(EngineKind.Qpdf, 2, false)]
    [InlineData(EngineKind.Pdftoppm, 3, false)]
    [InlineData(EngineKind.FFmpeg, 3, false)]
    [InlineData(EngineKind.ImageMagick, 1, false)]
    public void Exit_codes(EngineKind engine, int code, bool success)
    {
        Assert.Equal(success, EngineRunner.Succeeded(engine, code));
    }
}
