using System.Globalization;
using DropWheel.Capture;
using DropWheel.Conversions;
using DropWheel.Jobs;
using DropWheel.Localization;
using DropWheel.Settings;

namespace DropWheel.Tests.Capture;

/// <summary>Запись экрана (D-RECORD) без окон: аргументы ffmpeg, чётный размер, имя и папка, время, рамка, состояния.</summary>
public class RecordTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    // ---------- аргументы ffmpeg ----------

    [Theory]
    [InlineData(0, 0, 1920, 1080, 1920, 1080)]
    [InlineData(10, 20, 641, 481, 640, 480)]
    [InlineData(5, 5, 1, 1, 2, 2)]
    [InlineData(-1280, -300, 1279, 1023, 1278, 1022)]
    public void Even_size_rounds_down_and_keeps_corner(int x, int y, int w, int h, int ew, int eh)
    {
        Assert.Equal(new PixelRect(x, y, ew, eh), ScreenRecord.EvenSize(new PixelRect(x, y, w, h)));
    }

    [Fact]
    public void Gdigrab_input_uses_virtual_screen_pixels_with_negative_offsets()
    {
        // Монитор левее и выше основного: координаты отрицательные — gdigrab понимает их как есть.
        var args = ScreenRecord.GdigrabInput(new PixelRect(-1280, -300, 1281, 1025));
        Assert.Equal(
            ["-f", "gdigrab", "-framerate", "30", "-draw_mouse", "1", "-offset_x", "-1280", "-offset_y", "-300",
             "-video_size", "1280x1024", "-i", "desktop"],
            args);
    }

    [Fact]
    public void Arguments_encode_h264_into_the_output_with_time_limit()
    {
        var output = Path.Combine(_dir.Path, ".dropwheel-tmp", "тест 1.mp4");
        var args = ScreenRecord.Arguments(new PixelRect(100, 50, 801, 601), output);

        Assert.Equal(output, args[^1]); // путь — одним аргументом, последним
        Assert.Equal(["-hide_banner", "-nostats", "-y", "-f", "gdigrab"], args.Take(5));
        // stdin нужен для «q» — «-nostdin» нельзя.
        Assert.DoesNotContain("-nostdin", args);
        var joined = string.Join(' ', args);
        Assert.Contains("-offset_x 100 -offset_y 50 -video_size 800x600 -i desktop", joined);
        Assert.Contains("-c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p -movflags +faststart -t 1800", joined);
        // Вход — до кодека.
        Assert.True(Array.IndexOf(args.ToArray(), "-i") < Array.IndexOf(args.ToArray(), "-c:v"));
    }

    [Fact]
    public void Arguments_do_not_depend_on_windows_language()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            var odd = (CultureInfo)CultureInfo.GetCultureInfo("ru-RU").Clone();
            odd.NumberFormat.NegativeSign = "\u2212"; // типографский минус вместо «-»
            CultureInfo.CurrentCulture = odd;
            var args = ScreenRecord.GdigrabInput(new PixelRect(-1920, 0, 1920, 1080));
            Assert.Contains("-1920", args);
            Assert.Contains("1920x1080", args);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Custom_input_and_limit_for_tests()
    {
        var args = ScreenRecord.Arguments(["-f", "lavfi", "-i", "testsrc"], "out.mp4", TimeSpan.FromSeconds(2));
        Assert.Equal("-f lavfi -i testsrc", string.Join(' ', args.Skip(3).Take(4)));
        Assert.Equal(["-t", "2", "out.mp4"], args.TakeLast(3));
    }

    [Fact]
    public void Gif_is_made_by_the_wheel_gif_operation()
    {
        var op = ScreenRecord.GifOperation;
        Assert.Equal("to-gif", op.Id);
        Assert.True(op.AppliesTo(".mp4"));
        Assert.Contains("palettegen", op.Args);
    }

    // ---------- имя, папка, время ----------

    [Fact]
    public void File_name_has_date_and_time_without_colons()
    {
        var now = new DateTime(2026, 10, 24, 15, 30, 5);
        Assert.Equal("Запись 2026-10-24 15-30-05", ScreenRecord.FileStem(now));
        Assert.Equal(Path.Combine(_dir.Path, "Запись 2026-10-24 15-30-05.mp4"), ScreenRecord.FilePath(_dir.Path, now, RecordFormat.Mp4));
        File.WriteAllText(Path.Combine(_dir.Path, "Запись 2026-10-24 15-30-05.gif"), "x");
        Assert.Equal(Path.Combine(_dir.Path, "Запись 2026-10-24 15-30-05 (1).gif"), ScreenRecord.FilePath(_dir.Path, now, RecordFormat.Gif));
    }

    [Fact]
    public void Folder_is_own_then_shared_then_videos()
    {
        var shared = new ConversionSettings { OutputFolder = @"D:\Готово" };
        Assert.Equal(@"D:\Мои снимки", ScreenRecord.Folder(@"D:\Мои снимки", shared));
        Assert.Equal(@"D:\Готово", ScreenRecord.Folder(null, shared));
        Assert.Equal(ScreenRecord.DefaultFolder, ScreenRecord.Folder(null, new ConversionSettings()));
        Assert.Equal("DropWheel", Path.GetFileName(ScreenRecord.DefaultFolder));
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(12.7, "00:12")]
    [InlineData(754, "12:34")]
    [InlineData(3599, "59:59")]
    [InlineData(3723, "1:02:03")]
    [InlineData(-5, "00:00")]
    public void Elapsed_time_on_the_pill(double seconds, string expected)
    {
        Assert.Equal(expected, ScreenRecord.FormatElapsed(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Frame_inside_when_hidden_from_capture_otherwise_outside()
    {
        var region = new PixelRect(-100, 20, 640, 480);
        Assert.Equal(region, ScreenRecord.FrameBounds(region, 2, excludedFromCapture: true));
        Assert.Equal(new PixelRect(-102, 18, 644, 484), ScreenRecord.FrameBounds(region, 2, excludedFromCapture: false));
    }

    // ---------- состояния ----------

    [Fact]
    public void Session_goes_idle_selecting_recording_finalizing_idle()
    {
        var s = new RecordSession();
        var t0 = new DateTime(2026, 10, 24, 15, 30, 0);
        Assert.Equal(RecordState.Idle, s.State);
        Assert.Equal(RecordCommand.Select, s.Toggle());
        Assert.False(s.Start(t0)); // без выделения не пишем
        Assert.True(s.BeginSelect());
        Assert.False(s.BeginSelect()); // второй оверлей не открываем
        Assert.Equal(RecordCommand.None, s.Toggle()); // сочетание во время выделения — ничего
        Assert.True(s.Start(t0));
        Assert.Equal(RecordState.Recording, s.State);
        Assert.Equal(TimeSpan.FromSeconds(12), s.Elapsed(t0.AddSeconds(12)));
        Assert.Equal(RecordCommand.Stop, s.Toggle());
        s.CancelSelect(); // поздняя «отмена» выделения не сбивает запись
        Assert.Equal(RecordState.Recording, s.State);
        Assert.True(s.Stop());
        Assert.False(s.Stop()); // второй «Стоп» (сочетание + пилюля) — ничего
        Assert.Equal(RecordState.Finalizing, s.State);
        Assert.Equal(RecordCommand.None, s.Toggle()); // файл дописывается — сочетание ничего не делает
        Assert.False(s.BeginSelect());
        s.Finished();
        Assert.Equal(RecordState.Idle, s.State);
        Assert.Null(s.StartedAt);
        Assert.Equal(TimeSpan.Zero, s.Elapsed(t0.AddSeconds(30)));
    }

    [Fact]
    public void Cancelled_selection_returns_to_idle()
    {
        var s = new RecordSession();
        s.BeginSelect();
        s.CancelSelect();
        Assert.Equal(RecordState.Idle, s.State);
        Assert.False(s.Stop());
    }

    // ---------- настройки ----------

    [Theory]
    [InlineData("""{ "recordKeys": "shift+ctrl+f9" }""", true, "Ctrl+Shift+F9", false)]
    [InlineData("""{ "recordKeys": "R", "recordHotkey": false, "recordGif": true }""", false, "Ctrl+Alt+R", true)]
    [InlineData("""{ "recordKeys": null }""", true, "Ctrl+Alt+R", false)]
    [InlineData("{}", true, "Ctrl+Alt+R", false)]
    public void Settings_record_hotkey(string json, bool on, string keys, bool gif)
    {
        var path = Path.Combine(_dir.Path, "settings.json");
        File.WriteAllText(path, json);
        var s = AppSettings.Load(path);
        Assert.Equal((on, keys, gif), (s.RecordHotkey, s.RecordKeys, s.RecordGif));
        s.Save(path);
        Assert.Equal(s, AppSettings.Load(path));
    }

    [Fact]
    public void Default_chord_differs_from_screenshot_and_clipboard()
    {
        Assert.Equal("Ctrl+Alt+R", ScreenRecord.DefaultChord.ToString());
        Assert.NotEqual(KeyChord.Default, ScreenRecord.DefaultChord);
        Assert.Equal(ScreenRecord.DefaultKeys, ScreenRecord.NormalizeKeys("garbage"));
    }
}

[Collection(LanguageCollection.Name)]
public class RecordEnglishTests
{
    [Fact]
    public void English_file_name()
    {
        using var _ = Loc.Use(UiLanguage.En);
        Assert.Equal("Recording 2026-10-24 15-30-05", ScreenRecord.FileStem(new DateTime(2026, 10, 24, 15, 30, 5)));
    }
}
