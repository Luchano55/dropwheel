using DropWheel.Updates;

namespace DropWheel.Tests.Updates;

public class UpdateTests
{
    private static UpdateManifest Manifest(int build = 42, string tools = "abc") =>
        new(build, "deadbeef", tools, "DropWheel-app-win-x64.zip", "11", "DropWheel-tools-win-x64.zip", "22");

    [Fact]
    public void Manifest_round_trip()
    {
        var m = Manifest();
        Assert.Equal(m, UpdateManifest.Parse(m.ToJson()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("не json")]
    [InlineData("{}")]
    [InlineData("{\"build\":0,\"appZip\":\"a\",\"toolsZip\":\"b\"}")]
    [InlineData("[1,2]")]
    public void Bad_manifest_is_null(string json)
    {
        Assert.Null(UpdateManifest.Parse(json));
    }

    [Fact]
    public void Build_number_can_be_string()
    {
        Assert.Equal(7, UpdateManifest.Parse("{\"build\":\"7\",\"appZip\":\"a\",\"toolsZip\":\"b\"}")?.Build);
    }

    [Theory]
    [InlineData(41, "abc", true, false)]   // новая программа, утилиты те же
    [InlineData(41, "old", true, true)]    // новая программа и новые утилиты
    [InlineData(41, null, true, true)]     // нет файла версии утилит — обновить
    [InlineData(42, "old", false, false)]  // та же сборка — ничего (утилиты без программы не трогаем)
    [InlineData(50, "old", false, false)]  // у нас новее
    [InlineData(0, "abc", false, false)]   // локальная сборка — не обновляем
    public void Plan(int current, string? localTools, bool app, bool tools)
    {
        Assert.Equal(new UpdatePlan(app, tools), UpdatePlan.For(Manifest(), current, localTools));
    }

    [Fact]
    public void Version_build_part_is_the_build_number()
    {
        Assert.Equal(17, AppVersion.FromVersion(new Version(0, 2, 17, 0)));
        Assert.Equal(0, AppVersion.FromVersion(new Version(1, 0, 0, 0)));
        Assert.Equal(0, AppVersion.FromVersion(null));
    }

    [Fact]
    public void Apply_replaces_and_keeps_old_until_cleanup()
    {
        using var dir = new TempDir();
        var app = Path.Combine(dir.Path, "Программа");
        var staging = Path.Combine(dir.Path, "staging");
        Directory.CreateDirectory(Path.Combine(app, "tools", "ffmpeg"));
        Directory.CreateDirectory(Path.Combine(staging, "tools", "ffmpeg"));
        File.WriteAllText(Path.Combine(app, "DropWheel.exe"), "v1");
        File.WriteAllText(Path.Combine(app, "settings-like.txt"), "не трогать");
        File.WriteAllText(Path.Combine(staging, "DropWheel.exe"), "v2");
        File.WriteAllText(Path.Combine(staging, "tools", "ffmpeg", "ffmpeg.exe"), "new ffmpeg");

        var changed = UpdateApplier.Apply(staging, app);

        Assert.Equal(2, changed.Count);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(app, "DropWheel.exe")));
        Assert.Equal("v1", File.ReadAllText(Path.Combine(app, "DropWheel.exe.old")));
        Assert.Equal("new ffmpeg", File.ReadAllText(Path.Combine(app, "tools", "ffmpeg", "ffmpeg.exe")));
        Assert.Equal("не трогать", File.ReadAllText(Path.Combine(app, "settings-like.txt")));

        Assert.Equal(1, UpdateApplier.CleanupOld(app));
        Assert.False(File.Exists(Path.Combine(app, "DropWheel.exe.old")));
    }

    [Fact]
    public void Second_update_overwrites_stale_old_file()
    {
        using var dir = new TempDir();
        var app = Path.Combine(dir.Path, "app");
        var staging = Path.Combine(dir.Path, "staging");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(app, "a.exe"), "v2");
        File.WriteAllText(Path.Combine(app, "a.exe.old"), "v1");
        File.WriteAllText(Path.Combine(staging, "a.exe"), "v3");

        UpdateApplier.Apply(staging, app);

        Assert.Equal("v3", File.ReadAllText(Path.Combine(app, "a.exe")));
        Assert.Equal("v2", File.ReadAllText(Path.Combine(app, "a.exe.old")));
    }
}
