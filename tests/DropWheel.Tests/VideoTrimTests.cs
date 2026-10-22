using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;
using DropWheel.Localization;
using DropWheel.Wheel;

namespace DropWheel.Tests;

/// <summary>Обрезка видео (D-TRIM): каталог, параметры задачи, аргументы ffmpeg, разбор и показ времени, ручки.</summary>
public class VideoTrimTests
{
    private const string In = "/data/Мои видео/тест 1.mov";
    private static readonly ConversionSettings S = ConversionSettings.Default;

    private static List<string> Build(Operation op, string output = "/data/Мои видео/.dropwheel-abc.mov") =>
        ArgumentBuilder.Build(op, In, output, S).ToList();

    // ---------- каталог и колесо ----------

    [Fact]
    public void Trim_is_a_video_tool_for_one_file_only()
    {
        Assert.Contains(VideoTrim.Id, OperationCatalog.ForFile("clip.mp4").Where(o => o.Mode == OperationMode.Tool).Select(o => o.Id));
        Assert.Contains(VideoTrim.Id, OperationCatalog.ForFiles(["Мои видео/тест 1.mkv"]).Select(o => o.Id));
        Assert.DoesNotContain(VideoTrim.Id, OperationCatalog.ForFiles(["a.mp4", "b.mp4"]).Select(o => o.Id));
        Assert.DoesNotContain(VideoTrim.Id, OperationCatalog.ForFile("song.mp3").Select(o => o.Id));
    }

    [Fact]
    public void Exact_variant_is_not_on_the_wheel_but_can_be_found()
    {
        Assert.NotNull(OperationCatalog.Find(VideoTrim.ExactId));
        Assert.DoesNotContain(VideoTrim.ExactId, OperationCatalog.ForFile("clip.mp4").Select(o => o.Id));
        Assert.DoesNotContain(VideoTrim.ExactId, WheelGroups.Operations(WheelGroups.Video).Select(o => o.Id));
        Assert.Contains(VideoTrim.Id, WheelGroups.Operations(WheelGroups.Video).Select(o => o.Id));
    }

    [Fact]
    public void Default_video_tools_wheel_shows_trim_and_drops_7z()
    {
        var ops = OperationCatalog.ForFiles(["clip.mp4"]);
        var (_, tools) = WheelArrangement.Arrange(ops, WheelGroups.Video, new WheelPreferences());
        var ids = tools.Select(t => t.Id).ToList();
        Assert.Equal(8, ids.Count);
        Assert.Contains(VideoTrim.Id, ids);
        Assert.Contains("zip-pack", ids);
        Assert.DoesNotContain("pack-7z", ids);
    }

    [Fact]
    public void Trim_needs_values_until_bound()
    {
        var op = OperationCatalog.Find(VideoTrim.Id)!;
        Assert.True(op.NeedsValues);
        Assert.Equal(["start", "end"], op.MissingValues());
        var bound = VideoTrim.Bind(false, 5, 12);
        Assert.False(bound.NeedsValues);
        Assert.Empty(bound.MissingValues());
        Assert.Equal(VideoTrim.Id, bound.Id);
        Assert.Null(op.Values); // каталог не меняется
        Assert.False(OperationCatalog.Find("to-mp4")!.NeedsValues);
    }

    [Fact]
    public void Missing_values_are_a_conversion_error()
    {
        var op = OperationCatalog.Find(VideoTrim.Id)!;
        var ex = Assert.Throws<ConversionException>(() => Build(op));
        Assert.Contains("start, end", ex.Message);
        Assert.Throws<ConversionException>(() => Build(op.WithValues(new Dictionary<string, string> { ["start"] = "1" })));
    }

    [Fact]
    public async Task Converter_refuses_unbound_trim_before_running_anything()
    {
        using var dir = new TempDir();
        var input = Path.Combine(dir.Path, "тест 1.mp4");
        await File.WriteAllBytesAsync(input, [1, 2, 3]);
        var converter = new Converter(new ToolLocator { FfmpegPath = "/nonexistent/ffmpeg" }, S);
        var ex = await Assert.ThrowsAsync<ConversionException>(() => converter.ConvertAsync(input, OperationCatalog.Find(VideoTrim.Id)!));
        Assert.Contains("Обрезать…", ex.Message);
        Assert.Equal(["тест 1.mp4"], Directory.GetFileSystemEntries(dir.Path).Select(Path.GetFileName));
    }

    // ---------- аргументы ffmpeg ----------

    [Fact]
    public void Copy_mode_seeks_on_input_and_copies_all_streams()
    {
        var args = Build(VideoTrim.Bind(false, 5, 12.25));
        var i = args.IndexOf("-i");
        Assert.Equal(In, args[i + 1]);
        Assert.Single(args, a => a == In);
        Assert.Equal("5", args[args.IndexOf("-ss") + 1]);
        Assert.Equal("12.25", args[args.IndexOf("-to") + 1]);
        Assert.True(args.IndexOf("-ss") < i && args.IndexOf("-to") < i); // поиск по входу — до -i
        Assert.Equal(["-map", "0", "-c", "copy", "-avoid_negative_ts", "make_zero"], args[(i + 2)..^1]);
        Assert.Equal("/data/Мои видео/.dropwheel-abc.mov", args[^1]);
        Assert.Equal("pipe:1", args[args.IndexOf("-progress") + 1]); // общие ключи — на месте
    }

    [Fact]
    public void Exact_mode_reencodes_h264_with_crf_from_settings()
    {
        var args = ArgumentBuilder.Build(VideoTrim.Bind(true, 1, 2), In, "/data/out.mp4", S with { VideoCrf = 21 }).ToList();
        var i = args.IndexOf("-i");
        Assert.True(args.IndexOf("-ss") < i && args.IndexOf("-to") < i);
        Assert.Contains("libx264", args);
        Assert.Contains("aac", args);
        Assert.Equal("21", args[args.IndexOf("-crf") + 1]);
        Assert.Contains("trunc(iw/2)*2", args[args.IndexOf("-vf") + 1]);
        Assert.DoesNotContain("copy", args);
    }

    [Theory]
    [InlineData("clip.mp4", "mp4")]
    [InlineData("clip.MOV", "mov")]
    [InlineData("clip.mkv", "mkv")]
    [InlineData("clip.webm", "webm")]
    [InlineData("clip.avi", "avi")]
    [InlineData("clip.m4v", "m4v")]
    public void Copy_keeps_container_exact_makes_mp4(string file, string ext)
    {
        Assert.Equal(ext, OperationCatalog.Find(VideoTrim.Id)!.Output.Resolve(Path.GetExtension(file)));
        Assert.Equal("mp4", OperationCatalog.Find(VideoTrim.ExactId)!.Output.Resolve(Path.GetExtension(file)));
    }

    [Fact]
    public void Value_with_spaces_stays_one_argument()
    {
        var op = OperationCatalog.Find(VideoTrim.Id)!.WithValues(new Dictionary<string, string> { ["start"] = "0 1", ["end"] = "2" });
        var args = Build(op);
        Assert.Equal("0 1", args[args.IndexOf("-ss") + 1]);
    }

    [Fact]
    public void Other_ffmpeg_operations_still_put_input_first()
    {
        var args = ArgumentBuilder.Build(OperationCatalog.Find("to-mp4")!, In, "/data/out.mp4", S).ToList();
        Assert.Equal(["-hide_banner", "-nostdin", "-y", "-nostats", "-progress", "pipe:1", "-i", In], args[..8]);
    }

    [Fact]
    public void Bind_rejects_bad_ranges()
    {
        Assert.Throws<ArgumentException>(() => VideoTrim.Bind(false, 5, 5));
        Assert.Throws<ArgumentException>(() => VideoTrim.Bind(false, 5, 2));
        Assert.Throws<ArgumentException>(() => VideoTrim.Bind(false, -1, 2));
        Assert.Throws<ArgumentException>(() => VideoTrim.Bind(false, double.NaN, 2));
    }

    // ---------- время ----------

    [Theory]
    [InlineData("12", 12)]
    [InlineData("12.5", 12.5)]
    [InlineData("12,5", 12.5)]
    [InlineData("0:05", 5)]
    [InlineData(" 1:02.5 ", 62.5)]
    [InlineData("1:00:00", 3600)]
    [InlineData("01:02:03.4", 3723.4)]
    [InlineData("0", 0)]
    public void Parses_human_times(string text, double seconds)
    {
        Assert.True(VideoTrim.TryParse(text, out var value));
        Assert.Equal(seconds, value, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1:60")]
    [InlineData("1.5:00")]
    [InlineData("1:2:3:4")]
    [InlineData("1::2")]
    [InlineData(null)]
    public void Rejects_bad_times(string? text) => Assert.False(VideoTrim.TryParse(text, out _));

    [Theory]
    [InlineData(0, "00:00.0")]
    [InlineData(5.24, "00:05.2")]
    [InlineData(59.96, "01:00.0")]
    [InlineData(62.5, "01:02.5")]
    [InlineData(3725.5, "1:02:05.5")]
    [InlineData(-3, "00:00.0")]
    public void Formats_as_minutes_seconds_tenths(double seconds, string text) =>
        Assert.Equal(text, VideoTrim.Format(seconds));

    [Theory]
    [InlineData(5, "5")]
    [InlineData(12.25, "12.25")]
    [InlineData(1.23456, "1.235")]
    [InlineData(0, "0")]
    public void Ffmpeg_time_is_invariant_seconds(double seconds, string text)
    {
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ru-RU"); // запятая в дробях
            Assert.Equal(text, VideoTrim.Arg(seconds));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }
    }

    // ---------- ручки ----------

    [Fact]
    public void Range_starts_as_whole_video()
    {
        var r = new TrimRange(10);
        Assert.Equal((0.0, 10.0), (r.Start, r.End));
        Assert.True(r.IsWhole);
        Assert.False(r.WithStart(1).IsWhole);
    }

    [Fact]
    public void Handles_stay_inside_and_do_not_cross()
    {
        var r = new TrimRange(10).WithStart(3).WithEnd(7);
        Assert.Equal(4, r.Length, 6);
        Assert.Equal(7 - VideoTrim.MinLength, r.WithStart(9).Start, 6); // упирается в конец
        Assert.Equal(3 + VideoTrim.MinLength, r.WithEnd(1).End, 6);     // упирается в начало
        Assert.Equal(0, r.WithStart(-5).Start);
        Assert.Equal(10, r.WithEnd(50).End);
    }

    [Fact]
    public void Range_from_values_is_clamped()
    {
        var r = new TrimRange(10, start: 12, end: 20);
        Assert.Equal(10, r.End);
        Assert.Equal(10 - VideoTrim.MinLength, r.Start, 6);
        Assert.Equal(VideoTrim.MinLength, new TrimRange(0).Duration);
    }

    // ---------- длительность и прогресс ----------

    [Fact]
    public void Duration_from_ffmpeg_info()
    {
        const string info = """
            Input #0, matroska,webm, from 'тест 1.mkv':
              Metadata:
                ENCODER         : Lavf60.16.100
              Duration: 00:01:02.50, start: 0.000000, bitrate: 926 kb/s
              Stream #0:0: Video: ffv1
            """;
        Assert.Equal(62.5, VideoProbe.ParseDuration(info));
        Assert.Null(VideoProbe.ParseDuration("  Duration: N/A, start: 0.000000, bitrate: N/A"));
        Assert.Equal(2.96, VideoProbe.ParseLastTime("frame=1 time=00:00:01.00 bitrate\rframe=2 time=00:00:02.96 bitrate=N/A")!.Value, 6);
        Assert.Null(VideoProbe.ParseLastTime("nothing"));
    }

    [Fact]
    public void Frame_args_seek_on_input_and_scale()
    {
        var args = VideoProbe.FrameArgs("/data/Мои видео/тест 1.mkv", 3.5, "/tmp/кадр 1.png", 640).ToList();
        Assert.True(args.IndexOf("-ss") < args.IndexOf("-i"));
        Assert.Equal("3.5", args[args.IndexOf("-ss") + 1]);
        Assert.Equal("/data/Мои видео/тест 1.mkv", args[args.IndexOf("-i") + 1]);
        Assert.Equal("scale=640:-2", args[args.IndexOf("-vf") + 1]);
        Assert.Equal("1", args[args.IndexOf("-frames:v") + 1]);
        Assert.Equal("/tmp/кадр 1.png", args[^1]);
    }

    [Fact]
    public void Progress_of_a_trim_is_relative_to_the_piece()
    {
        var p = FfmpegProgress.For(["-hide_banner", "-ss", "10", "-to", "0:20", "-i", "in.mp4", "-c", "copy", "out.mp4"]);
        p.ReadStderr("  Duration: 00:01:40.00, start: 0.000000, bitrate: 1000 kb/s");
        Assert.Equal(0.5, p.ReadProgressLine("out_time_us=5000000")!.Value, 3);
    }

    [Fact]
    public void Progress_ignores_output_side_seek()
    {
        // «JPG (кадр)»: -ss после -i — это не кусок входа.
        var p = FfmpegProgress.For(["-i", "in.mp4", "-ss", "1", "-frames:v", "1", "out.jpg"]);
        p.ReadStderr("  Duration: 00:00:10.00, start: 0");
        Assert.Equal(0.5, p.ReadProgressLine("out_time_us=5000000")!.Value, 3);
        // -to за концом ролика — длина считается до конца.
        var q = FfmpegProgress.For(["-ss", "8", "-to", "360000", "-i", "in.mp4", "out.mp4"]);
        q.ReadStderr("  Duration: 00:00:10.00, start: 0");
        Assert.Equal(0.5, q.ReadProgressLine("out_time_us=1000000")!.Value, 3);
    }

    // ---------- подписи ----------

    [Fact]
    public void Labels_and_suffixes()
    {
        var op = OperationCatalog.Find(VideoTrim.Id)!;
        Assert.Equal("Обрезать…", Loc.Label(op, UiLanguage.Ru));
        Assert.Equal(" (обрезано)", Loc.Suffix(op, UiLanguage.Ru));
        Assert.Equal("Trim…", Loc.Label(op, UiLanguage.En));
        Assert.Equal(" (trimmed)", Loc.Suffix(OperationCatalog.Find(VideoTrim.ExactId)!, UiLanguage.En));
    }
}
