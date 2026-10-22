using DropWheel.Conversions;
using DropWheel.Jobs;

namespace DropWheel.Tests.Integration;

/// <summary>Видео: повернуть, кадры, обложка — настоящим ffmpeg, в папке «Мои фото», исходник «тест 1.mkv».</summary>
public class VideoToolsConversionTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly Converter _converter = new(Tools.Locator, ConversionSettings.Default);

    public void Dispose() => _dir.Dispose();

    private async Task<string> Convert(string input, string opId)
    {
        var hash = Tools.Hash(input);
        var output = await _converter.ConvertAsync(input, OperationCatalog.Find(opId)!);
        Assert.Equal(hash, Tools.Hash(input)); // исходник не тронут
        var dir = Path.GetDirectoryName(input)!;
        Assert.Empty(Directory.GetFileSystemEntries(dir, ".dropwheel-*")); // временных не осталось
        return output;
    }

    [SkippableFact]
    public async Task Rotate_swaps_width_and_height()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var input = Tools.MakeVideo(Path.Combine(_dir.Path, "тест 1.mkv"), 640, 360);
        var output = await Convert(input, "video-rotate-cw");
        Assert.Equal(Path.Combine(_dir.Path, "тест 1 (повёрнуто).mp4"), output);
        var streams = Tools.Streams(output);
        Assert.Contains("h264x360x640", streams);
        Assert.Contains("aac", streams);
    }

    [SkippableFact]
    public async Task Rotate_odd_size_is_made_even()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var input = Tools.MakeVideo(Path.Combine(_dir.Path, "тест 1.mkv"), audio: false, seconds: 1); // 1281×721
        var output = await Convert(input, "video-rotate-cw");
        Assert.Equal(["h264x720x1280"], Tools.Streams(output));
    }

    [SkippableFact]
    public async Task Frames_one_per_second_into_folder()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var input = Tools.MakeVideo(Path.Combine(_dir.Path, "тест 1.mkv"), 320, 240, seconds: 3);
        var output = await Convert(input, "video-frames");
        Assert.Equal(Path.Combine(_dir.Path, "тест 1 (кадры)"), output);
        var files = Directory.GetFiles(output).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["кадр-001.jpg", "кадр-002.jpg", "кадр-003.jpg"], files);
        if (Tools.HasMagick) Assert.Equal((320, 240), Tools.ImageSize(Path.Combine(output, "кадр-001.jpg")));
    }

    [SkippableFact]
    public async Task Frames_into_folder_with_percent_in_name()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var dir = Path.Combine(_dir.Path, "100% отпуск");
        Directory.CreateDirectory(dir);
        var input = Tools.MakeVideo(Path.Combine(dir, "тест 1.mkv"), 320, 240, audio: false, seconds: 2);
        var output = await Convert(input, "video-frames");
        Assert.Equal(Path.Combine(dir, "тест 1 (кадры)"), output);
        Assert.Equal(2, Directory.GetFiles(output, "*.jpg").Length);
    }

    [SkippableFact]
    public async Task Poster_is_a_jpg_frame()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var input = Tools.MakeVideo(Path.Combine(_dir.Path, "тест 1.mkv")); // 1281×721, 2 с
        var output = await Convert(input, "video-poster");
        Assert.Equal(Path.Combine(_dir.Path, "тест 1 (кадр).jpg"), output);
        Assert.True(new FileInfo(output).Length > 0);
        if (Tools.HasMagick) Assert.Equal((1281, 721), Tools.ImageSize(output));
    }

    [SkippableFact]
    public async Task Poster_of_clip_shorter_than_a_second()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var input = Tools.MakeVideo(Path.Combine(_dir.Path, "тест 1.mkv"), 320, 240, audio: false, seconds: 0.4);
        var output = await Convert(input, "video-poster");
        Assert.True(new FileInfo(output).Length > 0);
        if (Tools.HasMagick) Assert.Equal((320, 240), Tools.ImageSize(output));
    }
}
