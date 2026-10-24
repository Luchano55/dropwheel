using System.Globalization;
using DropWheel.Cli;
using DropWheel.Conversions;
using DropWheel.Jobs;

namespace DropWheel.Tests.Integration;

/// <summary>Кусок видео → GIF настоящим ffmpeg: «Мои фото/тест 1.mkv», 3 с → 1…2 с.</summary>
public class VideoTrimGifConversionTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly Converter _converter = new(Tools.Locator, ConversionSettings.Default);

    public void Dispose() => _dir.Dispose();

    private static double Duration(string path) =>
        double.Parse(Tools.Run(Tools.Ffprobe, "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path),
            CultureInfo.InvariantCulture);

    private static void AssertGif(string path, double seconds)
    {
        var head = new byte[6];
        using (var f = File.OpenRead(path)) f.ReadExactly(head);
        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(head));
        Assert.Equal("gif", Tools.Run(Tools.Ffprobe, "-v", "error", "-show_entries", "stream=codec_name", "-of", "csv=p=0", path));
        Assert.InRange(Duration(path), seconds - 0.2, seconds + 0.2);
    }

    [SkippableFact]
    public async Task Piece_becomes_a_gif_of_one_second()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var input = Tools.MakeVideo(Path.Combine(_dir.Path, "тест 1.mkv"), 800, 450, seconds: 3);
        var hash = Tools.Hash(input);

        var output = await _converter.ConvertAsync(input, VideoTrim.BindGif(1, 2));

        Assert.Equal(Path.Combine(_dir.Path, "тест 1 (кусок).gif"), output);
        AssertGif(output, 1);
        Assert.Equal("640x360", Tools.Run(Tools.Ffprobe, "-v", "error", "-show_entries", "stream=width,height", "-of", "csv=s=x:p=0", output));
        Assert.Equal(hash, Tools.Hash(input)); // исходник не тронут
        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path, ".dropwheel-*")); // временных не осталось
    }

    [SkippableFact]
    public async Task Cli_makes_gif_with_flag_or_operation_id()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var input = Tools.MakeVideo(Path.Combine(_dir.Path, "тест 1.mkv"), 160, 120, seconds: 3);

        var code = await Program.RunAsync(["convert", input, "video-trim", "--start", "1", "--end", "2", "--gif"],
            new StringWriter(), new StringWriter());
        Assert.Equal(0, code);
        AssertGif(Path.Combine(_dir.Path, "тест 1 (кусок).gif"), 1);

        code = await Program.RunAsync(["convert", input, VideoTrim.GifId, "--start", "0:01", "--end", "0:02"],
            new StringWriter(), new StringWriter());
        Assert.Equal(0, code);
        AssertGif(Path.Combine(_dir.Path, "тест 1 (кусок) (1).gif"), 1);
    }

    [Fact]
    public async Task Cli_rejects_exact_together_with_gif()
    {
        await Assert.ThrowsAsync<UsageException>(() =>
            Program.RunAsync(["convert", "a.mp4", "video-trim", "--gif", "--exact"], new StringWriter(), new StringWriter()));
        await Assert.ThrowsAsync<UsageException>(() =>
            Program.RunAsync(["convert", "a.mp4", "to-webm", "--gif"], new StringWriter(), new StringWriter()));
    }
}
