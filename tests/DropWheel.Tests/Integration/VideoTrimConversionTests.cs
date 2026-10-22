using System.Globalization;
using DropWheel.Cli;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;

namespace DropWheel.Tests.Integration;

/// <summary>Обрезка видео настоящим ffmpeg: «Мои фото/тест 1.mkv», 3 с → кусок 1…2 с.</summary>
public class VideoTrimConversionTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly Converter _converter = new(Tools.Locator, ConversionSettings.Default);

    public void Dispose() => _dir.Dispose();

    private static double Duration(string path) =>
        double.Parse(Tools.Run(Tools.Ffprobe, "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path),
            CultureInfo.InvariantCulture);

    private async Task<string> Trim(string input, bool exact, double start, double end)
    {
        var hash = Tools.Hash(input);
        var progress = new List<double>();
        var output = await _converter.ConvertAsync(input, VideoTrim.Bind(exact, start, end), new SyncProgress(progress.Add));
        Assert.Equal(hash, Tools.Hash(input)); // исходник не тронут
        Assert.Empty(Directory.GetFileSystemEntries(Path.GetDirectoryName(input)!, ".dropwheel-*")); // временных не осталось
        Assert.Equal(1.0, progress[^1]);
        return output;
    }

    [SkippableFact]
    public async Task Copy_mode_keeps_container_and_cuts_about_one_second()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var input = Tools.MakeVideo(Path.Combine(_dir.Path, "тест 1.mkv"), 320, 240, seconds: 3);
        var output = await Trim(input, exact: false, 1, 2);
        Assert.Equal(Path.Combine(_dir.Path, "тест 1 (обрезано).mkv"), output);
        Assert.InRange(Duration(output), 0.8, 1.3);
        // Без пересжатия: те же кодеки, обе дорожки.
        Assert.Equal(["ffv1x320x240", "flac"], Tools.Streams(output));
    }

    [SkippableFact]
    public async Task Exact_mode_reencodes_to_mp4_of_one_second()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var input = Tools.MakeVideo(Path.Combine(_dir.Path, "тест 1.mkv"), 321, 241, seconds: 3);
        var output = await Trim(input, exact: true, 1, 2);
        Assert.Equal(Path.Combine(_dir.Path, "тест 1 (обрезано).mp4"), output);
        Assert.InRange(Duration(output), 0.9, 1.1);
        var streams = Tools.Streams(output);
        Assert.Contains("h264x320x240", streams); // чётный размер
        Assert.Contains("aac", streams);
    }

    [SkippableFact]
    public async Task Second_trim_gets_a_free_name()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var input = Tools.MakeVideo(Path.Combine(_dir.Path, "тест 1.mkv"), 160, 120, audio: false, seconds: 3);
        await Trim(input, exact: false, 0, 1);
        var second = await Trim(input, exact: false, 1, 2);
        Assert.Equal(Path.Combine(_dir.Path, "тест 1 (обрезано) (1).mkv"), second);
    }

    [SkippableFact]
    public async Task Cli_trims_with_start_end_and_exact()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var input = Tools.MakeVideo(Path.Combine(_dir.Path, "тест 1.mkv"), 160, 120, seconds: 3);

        var code = await Program.RunAsync(["convert", input, "video-trim", "--start", "0:01", "--end", "2", "--exact"],
            new StringWriter(), new StringWriter());
        Assert.Equal(0, code);
        var output = Path.Combine(_dir.Path, "тест 1 (обрезано).mp4");
        Assert.InRange(Duration(output), 0.9, 1.1);

        // Без --end — до конца ролика.
        code = await Program.RunAsync(["convert", input, "video-trim", "--start", "2"], new StringWriter(), new StringWriter());
        Assert.Equal(0, code);
        Assert.InRange(Duration(Path.Combine(_dir.Path, "тест 1 (обрезано).mkv")), 0.8, 1.3);
    }

    [Fact]
    public async Task Cli_rejects_bad_trim_options()
    {
        await Assert.ThrowsAsync<UsageException>(() =>
            Program.RunAsync(["convert", "a.mp4", "video-trim", "--start", "abc"], new StringWriter(), new StringWriter()));
        await Assert.ThrowsAsync<UsageException>(() =>
            Program.RunAsync(["convert", "a.mp4", "video-trim", "--start", "5", "--end", "0:02"], new StringWriter(), new StringWriter()));
        await Assert.ThrowsAsync<UsageException>(() =>
            Program.RunAsync(["convert", "a.mp4", "to-webm", "--start", "5"], new StringWriter(), new StringWriter()));
    }

    [SkippableFact]
    public async Task Probe_reads_duration_and_frame()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var input = Tools.MakeVideo(Path.Combine(_dir.Path, "тест 1.mkv"), 320, 240, seconds: 3);
        Assert.Equal(3.0, (await VideoProbe.DurationAsync(Tools.Locator, input))!.Value, 1);

        var png = Path.Combine(_dir.Path, "кадр 1.png");
        Assert.True(await VideoProbe.FrameAsync(Tools.Locator, input, 1.5, png, 160));
        Assert.True(new FileInfo(png).Length > 0);
        if (Tools.HasMagick) Assert.Equal((160, 120), Tools.ImageSize(png));
        // У самого конца ролика кадр всё равно находится (берётся чуть раньше).
        Assert.True(await VideoProbe.FrameAsync(Tools.Locator, input, 3.0, png, 160));
    }

    [SkippableFact]
    public async Task Probe_without_duration_in_header_scans_the_file()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        // WebM «вживую» (как запись экрана из браузера) — без длительности в заголовке: «Duration: N/A».
        var src = Tools.MakeVideo(Path.Combine(_dir.Path, "тест 1.mkv"), 160, 120, audio: false, seconds: 2);
        var live = Path.Combine(_dir.Path, "запись.webm");
        Tools.Run(Tools.Ffmpeg, "-v", "error", "-y", "-i", src, "-c:v", "libvpx", "-b:v", "200k", "-deadline", "realtime", "-live", "1", live);
        var duration = await VideoProbe.DurationAsync(Tools.Locator, live);
        Assert.NotNull(duration);
        Assert.InRange(duration!.Value, 1.5, 2.5);
    }

    [SkippableFact]
    public async Task Probe_of_not_a_video_is_null()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var junk = _dir.File("мусор.mp4", "это не видео");
        Assert.Null(await VideoProbe.DurationAsync(Tools.Locator, junk));
        Assert.False(await VideoProbe.FrameAsync(Tools.Locator, junk, 1, Path.Combine(_dir.Path, "кадр.png"), 160));
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value)
        {
            lock (this) report(value);
        }
    }
}
