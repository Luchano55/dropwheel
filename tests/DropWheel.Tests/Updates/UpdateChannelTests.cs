using System.Reflection;
using DropWheel.Settings;
using DropWheel.Updates;

namespace DropWheel.Tests.Updates;

public class UpdateChannelTests
{
    private static UpdateManifest Manifest(string? version) =>
        new(57, "deadbeef", "abc", "DropWheel-app-win-x64.zip", "11", "DropWheel-tools-win-x64.zip", "22", version, "stable");

    [Theory]
    [InlineData(null, "auto")]
    [InlineData("", "auto")]
    [InlineData("что-то", "auto")]
    [InlineData("AUTO", "auto")]
    [InlineData(" Stable ", "stable")]
    [InlineData("release", "stable")]
    [InlineData("dev", "dev")]
    [InlineData("Beta", "dev")]
    public void Setting_is_normalized(string? setting, string expected)
    {
        Assert.Equal(expected, UpdateChannels.Normalize(setting));
    }

    [Theory]
    [InlineData("stable", UpdateChannel.Stable)]
    [InlineData("STABLE", UpdateChannel.Stable)]
    [InlineData("dev", UpdateChannel.Dev)]
    [InlineData(null, UpdateChannel.Dev)]   // сборки до 0.3.0 и локальные — без метаданных: тестовый канал, как раньше
    [InlineData("", UpdateChannel.Dev)]
    [InlineData("nightly", UpdateChannel.Dev)]
    public void Embedded_channel_from_metadata(string? metadata, UpdateChannel expected)
    {
        Assert.Equal(expected, UpdateChannels.FromMetadata(metadata));
    }

    [Theory]
    [InlineData("auto", UpdateChannel.Stable, UpdateChannel.Stable)]
    [InlineData("auto", UpdateChannel.Dev, UpdateChannel.Dev)]
    [InlineData(null, UpdateChannel.Stable, UpdateChannel.Stable)]
    [InlineData("stable", UpdateChannel.Dev, UpdateChannel.Stable)]
    [InlineData("dev", UpdateChannel.Stable, UpdateChannel.Dev)]
    [InlineData("мусор", UpdateChannel.Dev, UpdateChannel.Dev)]
    public void Setting_overrides_embedded_channel(string? setting, UpdateChannel embedded, UpdateChannel expected)
    {
        Assert.Equal(expected, UpdateChannels.Resolve(setting, embedded));
    }

    [Fact]
    public void Manifest_urls()
    {
        Assert.Equal("https://github.com/Luchano55/dropwheel/releases/download/dev-build/version.json",
            UpdateChannels.ManifestUrl(UpdateChannel.Dev));
        Assert.Equal("https://github.com/Luchano55/dropwheel/releases/latest/download/version.json",
            UpdateChannels.ManifestUrl(UpdateChannel.Stable));
    }

    [Fact]
    public void Dev_assets_come_from_dev_build()
    {
        Assert.Equal("https://github.com/Luchano55/dropwheel/releases/download/dev-build/DropWheel-app-win-x64.zip",
            UpdateChannels.AssetUrl(UpdateChannel.Dev, Manifest("0.3.0-dev.57"), "DropWheel-app-win-x64.zip"));
    }

    [Fact]
    public void Stable_assets_come_from_the_tagged_release()
    {
        Assert.Equal("https://github.com/Luchano55/dropwheel/releases/download/v0.3.0/DropWheel-app-win-x64.zip",
            UpdateChannels.AssetUrl(UpdateChannel.Stable, Manifest("0.3.0"), "DropWheel-app-win-x64.zip"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../evil")]
    [InlineData("v0.3.0")]
    [InlineData("0.3.0/../x")]
    public void Stable_assets_without_a_usable_version_come_from_latest(string? version)
    {
        Assert.Equal("https://github.com/Luchano55/dropwheel/releases/latest/download/DropWheel-tools-win-x64.zip",
            UpdateChannels.AssetUrl(UpdateChannel.Stable, Manifest(version), "DropWheel-tools-win-x64.zip"));
    }

    [Fact]
    public void Embedded_value_is_read_from_assembly_metadata()
    {
        // У тестовой сборки метаданных канала нет (CI передаёт UpdateChannel только при publish).
        Assert.Null(UpdateChannels.EmbeddedValue(Assembly.GetExecutingAssembly()));
        Assert.Null(UpdateChannels.EmbeddedValue(null));
    }

    [Fact]
    public void Old_manifest_without_version_still_parses()
    {
        const string old = """
            {"build":41,"commit":"abc","toolsVersion":"t","appZip":"a.zip","appSha256":"1","toolsZip":"b.zip","toolsSha256":"2"}
            """;
        var m = UpdateManifest.Parse(old);
        Assert.NotNull(m);
        Assert.Equal(41, m.Build);
        Assert.Null(m.Version);
        Assert.Null(m.Channel);
    }

    [Fact]
    public void New_manifest_with_version_and_channel_parses()
    {
        const string json = """
            {"build":57,"version":"0.3.0","channel":"stable","commit":"abc","toolsVersion":"t",
             "appZip":"a.zip","appSha256":"1","toolsZip":"b.zip","toolsSha256":"2"}
            """;
        var m = UpdateManifest.Parse(json);
        Assert.Equal("0.3.0", m?.Version);
        Assert.Equal("stable", m?.Channel);
        Assert.Equal(m, UpdateManifest.Parse(m!.ToJson()));
    }

    [Theory]
    [InlineData("0.3.0+abcdef0123", "0.3.0")]
    [InlineData("0.3.0-dev.57+abc", "0.3.0-dev.57")]
    [InlineData("0.3.0", "0.3.0")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("+abc", null)]
    public void Display_version_drops_commit(string? informational, string? expected)
    {
        Assert.Equal(expected, AppVersion.DisplayFrom(informational));
    }

    [Fact]
    public void Settings_keep_and_normalize_the_channel()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        Assert.Equal("auto", new AppSettings().UpdateChannel);

        File.WriteAllText(path, """{ "updateChannel": "Stable" }""");
        Assert.Equal("stable", AppSettings.Load(path).UpdateChannel);

        File.WriteAllText(path, """{ "updateChannel": null }""");
        Assert.Equal("auto", AppSettings.Load(path).UpdateChannel);

        new AppSettings { UpdateChannel = "dev" }.Save(path);
        Assert.Equal("dev", AppSettings.Load(path).UpdateChannel);
    }

    [Fact]
    public void Version_file_is_a_plain_semantic_version()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "version.txt"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        var version = File.ReadAllText(Path.Combine(root, "version.txt")).Trim();
        Assert.Matches(@"^\d+\.\d+\.\d+$", version);
    }
}
