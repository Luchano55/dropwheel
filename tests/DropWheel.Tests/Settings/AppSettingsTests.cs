using DropWheel.Conversions;
using DropWheel.Settings;
using DropWheel.Wheel;

namespace DropWheel.Tests.Settings;

public class AppSettingsTests : IDisposable
{
    private readonly TempDir _dir = new();
    private string SettingsPath => Path.Combine(_dir.Path, "настройки", "settings.json");

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var s = AppSettings.Load(SettingsPath);
        Assert.Equal(new AppSettings(), s);
        Assert.Equal("Space", s.Hotkey);
        Assert.True(s.ContextMenuEnabled);
        Assert.True(s.ClipboardHotkey);
        Assert.False(s.AutoStart);
        Assert.Equal(90, s.Conversion.JpegQuality);
    }

    [Fact]
    public void Round_trip_keeps_values_and_writes_literal_cyrillic()
    {
        var folder = Path.Combine(_dir.Path, "Загрузки", "Мои результаты");
        var original = new AppSettings
        {
            Hotkey = "F8",
            ContextMenuEnabled = false,
            AutoStart = true,
            FfmpegPath = @"C:\Программы\ffmpeg.exe",
            Conversion = new ConversionSettings { JpegQuality = 75, VideoCrf = 20, OutputFolder = folder },
        };

        original.Save(SettingsPath);
        var loaded = AppSettings.Load(SettingsPath);

        Assert.Equal(original, loaded);
        Assert.Equal(folder, loaded.Conversion.OutputFolder);
        Assert.Equal(75, loaded.Conversion.JpegQuality);

        var json = File.ReadAllText(SettingsPath);
        Assert.Contains("Мои результаты", json);
        Assert.Contains("Программы", json);
        Assert.DoesNotContain(@"\u04", json);
        Assert.Contains("\n", json); // с отступами
        // Временных файлов не остаётся.
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!));
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("ru", "ru")]
    [InlineData("auto", "auto")]
    public void Language_round_trips(string language, string expected)
    {
        new AppSettings { Language = language }.Save(SettingsPath);
        Assert.Contains($"\"language\": \"{expected}\"", File.ReadAllText(SettingsPath));
        Assert.Equal(expected, AppSettings.Load(SettingsPath).Language);
    }

    [Theory]
    [InlineData("{ }", "auto")]                         // старый settings.json без языка
    [InlineData("{ \"language\": null }", "auto")]
    [InlineData("{ \"language\": \"de\" }", "auto")]   // неизвестный язык — как в Windows
    [InlineData("{ \"language\": \"EN\" }", "en")]
    [InlineData("{ \"Language\": \"ru\" }", "ru")]
    public void Language_is_sanitized_on_load(string json, string expected)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, json);
        Assert.Equal(expected, AppSettings.Load(SettingsPath).Language);
    }

    [Fact]
    public void Language_defaults_to_auto() => Assert.Equal("auto", new AppSettings().Language);

    [Fact]
    public void Save_overwrites_existing_file()
    {
        new AppSettings { Hotkey = "F1" }.Save(SettingsPath);
        new AppSettings { Hotkey = "F2" }.Save(SettingsPath);
        Assert.Equal("F2", AppSettings.Load(SettingsPath).Hotkey);
    }

    [Fact]
    public void Corrupt_file_is_moved_to_bad_and_defaults_returned()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath + ".bad", "старый битый");
        File.WriteAllText(SettingsPath, "{ \"hotkey\": \"F8\", ");

        var s = AppSettings.Load(SettingsPath);

        Assert.Equal(new AppSettings(), s);
        Assert.False(File.Exists(SettingsPath));
        Assert.Equal("{ \"hotkey\": \"F8\", ", File.ReadAllText(SettingsPath + ".bad"));
    }

    [Fact]
    public void Wrong_types_count_as_corrupt()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ \"autoStart\": \"да\" }");
        Assert.Equal(new AppSettings(), AppSettings.Load(SettingsPath));
        Assert.True(File.Exists(SettingsPath + ".bad"));
    }

    [Fact]
    public void Partial_json_keeps_defaults_and_ignores_unknown()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """
            {
              "AutoStart": true,
              "somethingNew": [1, 2, 3],
              "conversion": { "mp3BitrateKbps": 320 }
            }
            """);

        var s = AppSettings.Load(SettingsPath);

        Assert.True(s.AutoStart);
        Assert.Equal("Space", s.Hotkey);
        Assert.True(s.ContextMenuEnabled);
        Assert.Equal(320, s.Conversion.Mp3BitrateKbps);
        Assert.Equal(90, s.Conversion.JpegQuality);
        Assert.Null(s.Conversion.OutputFolder);
        Assert.False(File.Exists(SettingsPath + ".bad"));
    }

    [Theory]
    [InlineData("vk:0x65", "vk:0x65")] // своя клавиша сохраняется (D-HOTKEY)
    [InlineData("F8", "F8")]
    [InlineData("Ctrl", "Space")]       // неизвестная или запрещённая — Пробел
    [InlineData("vk:0xA2", "Space")]
    [InlineData("vk:0x1B", "Space")]
    [InlineData("nonsense", "Space")]
    public void Hotkey_is_sanitized(string saved, string expected)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, $$"""{ "hotkey": "{{saved}}" }""");
        Assert.Equal(expected, AppSettings.Load(SettingsPath).Hotkey);
    }

    [Fact]
    public void Explicit_nulls_fall_back_to_defaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{ "hotkey": null, "conversion": null }""");

        var s = AppSettings.Load(SettingsPath);

        Assert.Equal("Space", s.Hotkey);
        Assert.NotNull(s.Conversion);
        Assert.Equal(90, s.Conversion.JpegQuality);
    }

    [Fact]
    public void Wheel_preferences_round_trip()
    {
        var original = new AppSettings
        {
            Wheel = new WheelPreferences { MruFirst = true }
                .WithLayout(WheelGroups.Photo, OperationMode.Convert, ["to-png", "to-jpg"], ["to-avif"])
                .WithLayout(WheelGroups.Video, OperationMode.Tool, [], ["video-mute"])
                .WithRecent(WheelGroups.Photo, OperationMode.Convert, "to-webp"),
        };

        original.Save(SettingsPath);
        var loaded = AppSettings.Load(SettingsPath);

        Assert.Equal(original, loaded);
        Assert.True(loaded.Wheel.MruFirst);
        Assert.Equal(["to-png", "to-jpg"], loaded.Wheel.For(WheelGroups.Photo, OperationMode.Convert)!.Order);
        Assert.Equal("to-webp", loaded.Wheel.For(WheelGroups.Photo, OperationMode.Convert)!.Recent);
        Assert.Equal(["video-mute"], loaded.Wheel.For(WheelGroups.Video, OperationMode.Tool)!.Hidden);
        Assert.Contains("\"photo\"", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Old_settings_without_wheel_load_with_defaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        // settings.json этапа 3 — без «wheel».
        File.WriteAllText(SettingsPath, """
            {
              "hotkey": "F8",
              "contextMenuEnabled": true,
              "autoStart": false,
              "ffmpegPath": null,
              "magickPath": null,
              "conversion": { "jpegQuality": 90, "webpQuality": 85, "videoCrf": 23, "compressCrf": 28, "mp3BitrateKbps": 192, "outputFolder": null },
              "checkForUpdates": true
            }
            """);

        var s = AppSettings.Load(SettingsPath);

        Assert.Equal("F8", s.Hotkey);
        Assert.Equal(new WheelPreferences(), s.Wheel);
        Assert.False(s.Wheel.MruFirst);
        Assert.False(File.Exists(SettingsPath + ".bad"));
        // Новые настройки, которых в старом файле нет, — по умолчанию.
        Assert.Equal(WheelFill.DefaultPercent, s.WheelOpacity);
        Assert.True(s.ClipboardHotkey);
        Assert.True(s.KeepClipboardImage);
        Assert.Null(s.ClipboardFolder);
        Assert.False(s.CopyResultToClipboard);
    }

    [Fact]
    public void Copy_result_setting_round_trips()
    {
        Assert.False(new AppSettings().CopyResultToClipboard);
        new AppSettings { CopyResultToClipboard = true }.Save(SettingsPath);
        Assert.Contains("\"copyResultToClipboard\": true", File.ReadAllText(SettingsPath));
        Assert.True(AppSettings.Load(SettingsPath).CopyResultToClipboard);
    }

    [Fact]
    public void Last_seen_version_is_null_in_old_files_and_round_trips()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{ "hotkey": "Space", "checkForUpdates": true }""");
        Assert.Null(AppSettings.Load(SettingsPath).LastSeenVersion);
        Assert.False(File.Exists(SettingsPath + ".bad"));

        new AppSettings { LastSeenVersion = "1.2.0-dev.57" }.Save(SettingsPath);
        Assert.Equal("1.2.0-dev.57", AppSettings.Load(SettingsPath).LastSeenVersion);
    }

    [Fact]
    public void Clipboard_image_settings_round_trip()
    {
        new AppSettings { KeepClipboardImage = false, ClipboardFolder = "/data/Мои фото/буфер 1" }.Save(SettingsPath);
        var loaded = AppSettings.Load(SettingsPath);
        Assert.False(loaded.KeepClipboardImage);
        Assert.Equal("/data/Мои фото/буфер 1", loaded.ClipboardFolder);
    }

    [Fact]
    public void Blank_clipboard_folder_means_default()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{ "clipboardFolder": "  " }""");
        Assert.Null(AppSettings.Load(SettingsPath).ClipboardFolder);
    }

    [Fact]
    public void Wheel_opacity_and_clipboard_hotkey_round_trip()
    {
        new AppSettings { WheelOpacity = 50, ClipboardHotkey = false }.Save(SettingsPath);
        var loaded = AppSettings.Load(SettingsPath);
        Assert.Equal(50, loaded.WheelOpacity);
        Assert.False(loaded.ClipboardHotkey);
    }

    [Theory]
    [InlineData(5, 40)]
    [InlineData(250, 100)]
    [InlineData(70, 70)]
    public void Wheel_opacity_out_of_range_is_clamped(int written, int expected)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, $$"""{ "wheelOpacity": {{written}} }""");
        Assert.Equal(expected, AppSettings.Load(SettingsPath).WheelOpacity);
    }

    [Fact]
    public void Wheel_nulls_are_sanitized()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """
            { "wheel": { "mruFirst": true, "groups": { "photo": { "convert": { "order": null, "hidden": ["to-png", null] } }, "video": null } } }
            """);

        var s = AppSettings.Load(SettingsPath);

        var photo = s.Wheel.For(WheelGroups.Photo, OperationMode.Convert)!;
        Assert.Empty(photo.Order);
        Assert.Equal(["to-png"], photo.Hidden);
        Assert.Null(s.Wheel.For(WheelGroups.Video, OperationMode.Convert));
        Assert.True(s.Wheel.MruFirst);

        File.WriteAllText(SettingsPath, """{ "wheel": null }""");
        Assert.Equal(new WheelPreferences(), AppSettings.Load(SettingsPath).Wheel);
    }

    [Fact]
    public async Task Saver_writes_only_the_latest_value_after_delay()
    {
        using var saver = new SettingsSaver(SettingsPath, TimeSpan.FromMilliseconds(80));
        saver.Schedule(new AppSettings { Hotkey = "F1" });
        saver.Schedule(new AppSettings { Hotkey = "F2" });
        Assert.False(File.Exists(SettingsPath));

        for (var i = 0; i < 100 && saver.HasPending; i++) await Task.Delay(20);

        Assert.False(saver.HasPending);
        Assert.Equal("F2", AppSettings.Load(SettingsPath).Hotkey);
    }

    [Fact]
    public void Saver_flush_and_dispose_write_immediately()
    {
        var saver = new SettingsSaver(SettingsPath, TimeSpan.FromHours(1));
        saver.Schedule(new AppSettings { Hotkey = "F3" });
        saver.Flush();
        Assert.Equal("F3", AppSettings.Load(SettingsPath).Hotkey);

        saver.Schedule(new AppSettings { Hotkey = "F4" });
        saver.Dispose();
        Assert.Equal("F4", AppSettings.Load(SettingsPath).Hotkey);
        saver.Flush(); // повторно — ничего не делает
    }

    [Fact]
    public void Data_dir_can_be_overridden_by_environment()
    {
        var old = Environment.GetEnvironmentVariable(AppPaths.DataDirVariable);
        try
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirVariable, _dir.Path);
            Assert.Equal(_dir.Path, AppPaths.DataDir);
            Assert.Equal(Path.Combine(_dir.Path, "settings.json"), AppPaths.SettingsFile);
            Assert.Equal(Path.Combine(_dir.Path, "logs"), AppPaths.LogsDir);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirVariable, old);
        }
    }
}
