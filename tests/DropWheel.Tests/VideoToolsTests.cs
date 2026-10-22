using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Localization;

namespace DropWheel.Tests;

/// <summary>Видео: «Повернуть ↻», «Кадры», «JPG (кадр)» — каталог и аргументы ffmpeg.</summary>
public class VideoToolsTests
{
    private static readonly ConversionSettings S = ConversionSettings.Default;
    private const string In = "/data/Мои видео/тест 1.mov";

    private static IReadOnlyList<string> Build(string opId, string output) =>
        ArgumentBuilder.Build(OperationCatalog.Find(opId)!, In, output, S);

    [Fact]
    public void Video_wheel_has_new_tools_and_poster()
    {
        var ops = OperationCatalog.ForFile("clip.mp4");
        var tools = ops.Where(o => o.Mode == OperationMode.Tool).Select(o => o.Id).ToList();
        var convert = ops.Where(o => o.Mode == OperationMode.Convert).Select(o => o.Id).ToList();
        Assert.Contains("video-rotate-cw", tools);
        Assert.Contains("video-frames", tools);
        Assert.Contains("video-poster", convert);
        // Девятый — «В 7Z»: по умолчанию не виден, колесо берёт первые 8 (D-TRIM).
        Assert.Equal(9, tools.Count);
        Assert.Equal("pack-7z", tools[^1]);
        Assert.True(convert.Count <= 8, string.Join(", ", convert));
    }

    [Fact]
    public void Rotate_transposes_and_reencodes_to_h264_with_even_size()
    {
        var args = Build("video-rotate-cw", "/data/Мои видео/.dropwheel-abc.mp4").ToList();
        var vf = args[args.IndexOf("-vf") + 1];
        Assert.StartsWith("transpose=1,", vf);
        Assert.Contains("trunc(iw/2)*2", vf);
        Assert.Contains("libx264", args);
        Assert.Equal(S.VideoCrf.ToString(), args[args.IndexOf("-crf") + 1]);
        Assert.Equal("/data/Мои видео/.dropwheel-abc.mp4", args[^1]);
    }

    [Fact]
    public void Frames_go_into_folder_as_numbered_jpgs_and_output_is_not_appended_again()
    {
        const string dir = "/data/Мои видео/.dropwheel-abc";
        var args = Build("video-frames", dir).ToList();
        Assert.Equal(Path.Combine(dir, "кадр-%03d.jpg"), args[^1]);
        Assert.Single(args, a => a.Contains(".dropwheel-abc"));
        Assert.Equal("fps=1", args[args.IndexOf("-vf") + 1]);
        Assert.Equal("300", args[args.IndexOf("-frames:v") + 1]);
    }

    [Fact]
    public void Percent_in_folder_name_is_escaped_for_ffmpeg_pattern()
    {
        var args = Build("video-frames", "/data/100% фото/.dropwheel-abc");
        Assert.Equal(Path.Combine("/data/100%% фото/.dropwheel-abc", "кадр-%03d.jpg"), args[^1]);
        // Обычный файл-результат — как есть.
        Assert.Equal("/data/100% фото/x.mp4", Build("video-rotate-cw", "/data/100% фото/x.mp4")[^1]);
    }

    [Fact]
    public void Poster_takes_one_frame_at_one_second()
    {
        var args = Build("video-poster", "/data/Мои видео/.dropwheel-abc.jpg").ToList();
        Assert.Equal("1", args[args.IndexOf("-ss") + 1]);
        Assert.Equal("1", args[args.IndexOf("-frames:v") + 1]);
        Assert.Equal("/data/Мои видео/.dropwheel-abc.jpg", args[^1]);
        // После входа: tpad успевает дорисовать короткий ролик до первой секунды.
        Assert.True(args.IndexOf("-ss") > args.IndexOf("-i"));
    }

    [Fact]
    public void Russian_names()
    {
        Assert.Equal("Повернуть ↻", Loc.Label(OperationCatalog.Find("video-rotate-cw")!, UiLanguage.Ru));
        Assert.Equal(" (кадры)", Loc.Suffix(OperationCatalog.Find("video-frames")!, UiLanguage.Ru));
        Assert.Equal(" (кадр)", Loc.Suffix(OperationCatalog.Find("video-poster")!, UiLanguage.Ru));
    }

    [Theory]
    [InlineData("video-rotate-cw", "Rotate ↻", " (rotated)")]
    [InlineData("video-frames", "Frames", " (frames)")]
    [InlineData("video-poster", "JPG (frame)", " (frame)")]
    public void English_names(string id, string label, string suffix)
    {
        var op = OperationCatalog.Find(id)!;
        Assert.Equal((label, suffix), (Loc.Label(op, UiLanguage.En), Loc.Suffix(op, UiLanguage.En)));
    }
}

/// <summary>Имя кадров зависит от языка — в непараллельной коллекции.</summary>
[Collection(LanguageCollection.Name)]
public class VideoFramesEnglishTests
{
    [Fact]
    public void Frame_files_are_named_in_interface_language()
    {
        using var _ = Loc.Use(UiLanguage.En);
        var args = ArgumentBuilder.Build(OperationCatalog.Find("video-frames")!, "/v/a.mp4", "/v/.dropwheel-x", ConversionSettings.Default);
        Assert.Equal(Path.Combine("/v/.dropwheel-x", "frame-%03d.jpg"), args[^1]);
    }
}
