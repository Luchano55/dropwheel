using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;

namespace DropWheel.Tests.Integration;

/// <summary>Настоящие конвертации в папке «Мои фото», исходник «тест 1.*».</summary>
public class ConversionTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly Converter _converter = new(Tools.Locator, ConversionSettings.Default);

    public void Dispose() => _dir.Dispose();

    private string Heic()
    {
        var p = Path.Combine(_dir.Path, "тест 1.heic");
        File.Copy(Tools.Fixture("sample.heic"), p);
        return p;
    }

    private async Task<string> Convert(string input, string opId)
    {
        var hash = Tools.Hash(input);
        var output = await _converter.ConvertAsync(input, OperationCatalog.Find(opId)!);
        Assert.True(new FileInfo(output).Length > 0);
        Assert.Equal(hash, Tools.Hash(input)); // исходник не тронут
        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path, ".dropwheel-*")); // временных файлов не осталось
        return output;
    }

    // --- Изображения ---

    [SkippableFact]
    public async Task Heic_to_jpg_lands_next_to_source()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var output = await Convert(Heic(), "to-jpg");
        Assert.Equal(Path.Combine(_dir.Path, "тест 1.jpg"), output);
        Assert.Equal((400, 300), Tools.ImageSize(output));
    }

    [SkippableTheory]
    [InlineData("to-png", "тест 1.png")]
    [InlineData("to-webp", "тест 1.webp")]
    [InlineData("to-avif", "тест 1.avif")]
    [InlineData("to-pdf", "тест 1.pdf")]
    [InlineData("img-compress", "тест 1 (сжато).jpg")]
    [InlineData("img-strip", "тест 1 (без метаданных).jpg")]
    public async Task Heic_conversions(string opId, string expectedName)
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var ext = Path.GetExtension(expectedName).TrimStart('.');
        Skip.If(ext is "avif" or "pdf" && !Tools.CanWrite(ext), $"эта сборка ImageMagick не пишет {ext}");
        var output = await Convert(Heic(), opId);
        Assert.Equal(Path.Combine(_dir.Path, expectedName), output);
    }

    [SkippableFact]
    public async Task Half_and_1920_resize()
    {
        Skip.IfNot(Tools.HasMagick && Tools.HasFfmpeg, "нет ImageMagick или ffmpeg");
        var big = Tools.MakeImage(Path.Combine(_dir.Path, "большое фото.png"), 2400, 1200);
        Assert.Equal((1200, 600), Tools.ImageSize(await Convert(big, "img-half")));
        Assert.Equal((1920, 960), Tools.ImageSize(await Convert(big, "img-1920")));

        var small = Tools.MakeImage(Path.Combine(_dir.Path, "маленькое.png"), 800, 600);
        Assert.Equal((800, 600), Tools.ImageSize(await Convert(small, "img-1920"))); // не увеличиваем
    }

    [SkippableFact]
    public async Task Second_conversion_gets_number()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var src = Heic();
        await Convert(src, "to-jpg");
        var second = await Convert(src, "to-jpg");
        Assert.Equal(Path.Combine(_dir.Path, "тест 1 (1).jpg"), second);
    }

    [SkippableFact]
    public async Task Shared_output_folder_is_created()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var shared = Path.Combine(_dir.Path, "Загрузки", "DropWheel");
        var converter = new Converter(Tools.Locator, ConversionSettings.Default with { OutputFolder = shared });
        var output = await converter.ConvertAsync(Heic(), OperationCatalog.Find("to-png")!);
        Assert.Equal(Path.Combine(shared, "тест 1.png"), output);
    }

    [SkippableFact]
    public async Task Broken_image_gives_short_error_and_no_leftovers()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var broken = _dir.File("сломанный.jpg", "это не картинка");
        var ex = await Assert.ThrowsAsync<ConversionException>(() =>
            _converter.ConvertAsync(broken, OperationCatalog.Find("to-png")!));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        Assert.NotNull(ex.Details);
        Assert.Single(Directory.GetFiles(_dir.Path)); // только исходник
    }

    // --- Видео ---

    [SkippableTheory]
    [InlineData("to-mp4", "тест 1.mp4", "h264x1280x720")]
    [InlineData("to-webm", "тест 1.webm", "vp9x1281x721")]
    [InlineData("to-gif", "тест 1.gif", "gifx640x360")]
    [InlineData("video-compress", "тест 1 (сжато).mp4", "h264x1280x720")]
    [InlineData("video-720p", "тест 1 (720p).mp4", "h264x1278x720")]
    [InlineData("video-mute", "тест 1 (без звука).mkv", "ffv1x1281x721")]
    [InlineData("video-to-mp3", "тест 1.mp3", "mp3")]
    public async Task Video_conversions(string opId, string expectedName, string firstStream)
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var src = Tools.MakeVideo(Path.Combine(_dir.Path, "тест 1.mkv"));
        var output = await Convert(src, opId);
        Assert.Equal(Path.Combine(_dir.Path, expectedName), output);
        var streams = Tools.Streams(output);
        Assert.Equal(firstStream, streams[0]);
        if (opId is "video-mute" or "to-gif") Assert.Single(streams);
        if (opId is "to-mp4" or "video-compress") Assert.Equal("aac", streams[1]);
    }

    [SkippableFact]
    public async Task Portrait_video_720p_keeps_orientation()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var src = Tools.MakeVideo(Path.Combine(_dir.Path, "вертикальное.mkv"), 1080, 1920);
        Assert.Equal("h264x720x1280", Tools.Streams(await Convert(src, "video-720p"))[0]);
    }

    [SkippableFact]
    public async Task Video_without_audio_to_mp3_fails_clearly()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var src = Tools.MakeVideo(Path.Combine(_dir.Path, "немое.mkv"), audio: false);
        var ex = await Assert.ThrowsAsync<ConversionException>(() =>
            _converter.ConvertAsync(src, OperationCatalog.Find("video-to-mp3")!));
        Assert.Contains("дорожки", ex.Message);
        Assert.Single(Directory.GetFiles(_dir.Path));
    }

    [SkippableFact]
    public async Task Video_progress_is_reported()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var src = Tools.MakeVideo(Path.Combine(_dir.Path, "тест 1.mkv"), seconds: 4);
        var values = new List<double>();
        await _converter.ConvertAsync(src, OperationCatalog.Find("to-mp4")!, new SyncProgress(values.Add));
        Assert.Contains(values, v => v > 0 && v < 1);
        Assert.Equal(1.0, values[^1]);
    }

    [SkippableFact]
    public async Task Cancel_kills_process_and_cleans_up()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var src = Tools.MakeVideo(Path.Combine(_dir.Path, "длинное.mkv"), 1920, 1080, seconds: 20);
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p => { if (p > 0) cts.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _converter.ConvertAsync(src, OperationCatalog.Find("to-webm")!, progress, ct: cts.Token));
        Assert.Single(Directory.GetFiles(_dir.Path));
    }

    // --- Аудио ---

    [SkippableTheory]
    [InlineData("to-mp3", "тест 1.mp3", "mp3")]
    [InlineData("to-m4a", "тест 1.m4a", "aac")]
    [InlineData("to-flac", "тест 1.flac", "flac")]
    [InlineData("to-ogg", "тест 1.ogg", "vorbis")]
    [InlineData("audio-normalize", "тест 1 (норм).wav", "pcm_s16le")]
    public async Task Audio_conversions(string opId, string expectedName, string codec)
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var src = Tools.MakeAudio(Path.Combine(_dir.Path, "тест 1.wav"));
        var output = await Convert(src, opId);
        Assert.Equal(Path.Combine(_dir.Path, expectedName), output);
        Assert.Equal(codec, Tools.Streams(output)[0]);
    }

    [SkippableFact]
    public async Task Wav_to_wav_is_rejected()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var src = Tools.MakeAudio(Path.Combine(_dir.Path, "тест 1.wav"));
        await Assert.ThrowsAsync<ConversionException>(() =>
            _converter.ConvertAsync(src, OperationCatalog.Find("to-wav")!));
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) { lock (this) report(value); }
    }
}
