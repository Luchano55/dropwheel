using DropWheel.Conversions;
using DropWheel.Settings;

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
