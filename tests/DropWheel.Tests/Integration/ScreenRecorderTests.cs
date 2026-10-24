using System.Globalization;
using DropWheel.Capture;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;

namespace DropWheel.Tests.Integration;

/// <summary>
/// Запись экрана (D-RECORD) настоящим ffmpeg. gdigrab есть только в Windows, поэтому вместо экрана — генератор картинки
/// (lavfi testsrc в реальном времени); всё остальное — как в программе: «q» в stdin, MP4 на свободное имя,
/// GIF второй ступенью, временный файл и скрытая папка удаляются. Папка — «Мои фото» (кириллица и пробел в пути).
/// </summary>
public class ScreenRecorderTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly Converter _converter = new(Tools.Locator, ConversionSettings.Default);
    private static readonly DateTime Now = new(2026, 10, 24, 15, 30, 5);

    public void Dispose() => _dir.Dispose();

    private static IReadOnlyList<string> Fake(string output, TimeSpan? limit = null) => ScreenRecord.Arguments(
        ["-re", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=30"], output, limit);

    private static string Probe(string path, string entries) =>
        Tools.Run(Tools.Ffprobe, "-v", "error", "-show_entries", entries, "-of", "csv=p=0", path);

    private static double Duration(string path) => double.Parse(Probe(path, "format=duration"), CultureInfo.InvariantCulture);

    private void AssertNoTemp() =>
        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path, ".dropwheel-*", SearchOption.AllDirectories));

    private ScreenRecorder Start(TimeSpan? limit = null) =>
        ScreenRecorder.Start(Tools.Locator, _dir.Path, temp => Fake(temp, limit));

    [SkippableFact]
    public async Task Stop_with_q_finalizes_a_playable_mp4()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        using var recorder = Start();
        Assert.StartsWith(Path.Combine(_dir.Path, Converter.TempDirName), recorder.TempPath);
        await Task.Delay(1500);
        Assert.False(recorder.HasExited);
        await recorder.StopAsync(TimeSpan.FromSeconds(15));

        var path = await recorder.SaveAsync(RecordFormat.Mp4, _dir.Path, ScreenRecord.FileStem(Now), _converter);

        Assert.Equal(Path.Combine(_dir.Path, "Запись 2026-10-24 15-30-05.mp4"), path);
        Assert.Equal("h264,320,240,yuv420p", Probe(path, "stream=codec_name,width,height,pix_fmt"));
        Assert.InRange(Duration(path), 0.5, 5);
        AssertNoTemp();
    }

    [SkippableFact]
    public async Task Second_recording_in_the_same_second_gets_free_name()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        File.WriteAllText(Path.Combine(_dir.Path, "Запись 2026-10-24 15-30-05.mp4"), "занято");
        using var recorder = Start(TimeSpan.FromSeconds(1));
        await recorder.Exited; // предел длины — ffmpeg остановился сам

        var path = await recorder.SaveAsync(RecordFormat.Mp4, _dir.Path, ScreenRecord.FileStem(Now), _converter);

        Assert.Equal(Path.Combine(_dir.Path, "Запись 2026-10-24 15-30-05 (1).mp4"), path);
        Assert.Equal("занято", File.ReadAllText(Path.Combine(_dir.Path, "Запись 2026-10-24 15-30-05.mp4")));
        Assert.InRange(Duration(path), 0.8, 1.3);
        AssertNoTemp();
    }

    [SkippableFact]
    public async Task Gif_is_made_from_the_recording_and_temp_mp4_is_deleted()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        using var recorder = Start();
        await Task.Delay(1200);
        await recorder.StopAsync(TimeSpan.FromSeconds(15));

        var path = await recorder.SaveAsync(RecordFormat.Gif, _dir.Path, ScreenRecord.FileStem(Now), _converter);

        Assert.Equal(Path.Combine(_dir.Path, "Запись 2026-10-24 15-30-05.gif"), path);
        var head = new byte[6];
        using (var f = File.OpenRead(path)) f.ReadExactly(head);
        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(head));
        Assert.Equal("gif,320,240", Probe(path, "stream=codec_name,width,height"));
        Assert.False(File.Exists(recorder.TempPath));
        Assert.Single(Directory.GetFiles(_dir.Path)); // только GIF, MP4 не остался
        AssertNoTemp();
    }

    [SkippableFact]
    public void Synchronous_stop_on_exit_saves_mp4()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        using var recorder = Start();
        Thread.Sleep(1200);
        recorder.Stop(TimeSpan.FromSeconds(15));

        var path = recorder.SaveMp4Now(_dir.Path, ScreenRecord.FileStem(Now));

        Assert.Equal(Path.Combine(_dir.Path, "Запись 2026-10-24 15-30-05.mp4"), path);
        Assert.Equal("h264", Probe(path!, "stream=codec_name"));
        AssertNoTemp();
    }

    [SkippableFact]
    public async Task Failed_ffmpeg_gives_error_and_leaves_nothing()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        using var recorder = ScreenRecorder.Start(Tools.Locator, _dir.Path,
            temp => ScreenRecord.Arguments(["-f", "lavfi", "-i", "no_such_source"], temp));
        await recorder.Exited;

        await Assert.ThrowsAsync<ConversionException>(() =>
            recorder.SaveAsync(RecordFormat.Mp4, _dir.Path, ScreenRecord.FileStem(Now), _converter));
        Assert.Empty(Directory.GetFiles(_dir.Path));
        AssertNoTemp();
    }

    [SkippableFact]
    public void Dispose_without_saving_kills_ffmpeg_and_cleans_up()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var recorder = Start();
        Thread.Sleep(500);
        recorder.Dispose();
        Assert.True(recorder.HasExited);
        AssertNoTemp();
    }

    [Fact]
    public void Missing_ffmpeg_is_a_conversion_error()
    {
        var tools = new ToolLocator { FfmpegPath = Path.Combine(_dir.Path, "нет ffmpeg.exe") };
        Assert.Throws<ConversionException>(() => ScreenRecorder.Start(tools, _dir.Path, temp => Fake(temp)));
        AssertNoTemp();
    }
}
