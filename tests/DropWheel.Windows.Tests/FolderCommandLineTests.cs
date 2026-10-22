using DropWheel.App;

namespace DropWheel.Windows.Tests;

/// <summary>«Конвертировать…» по папке (D-FOLDERS): путь к папке из проводника доходит до приложения.</summary>
public class FolderCommandLineTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "dropwheel-тесты", Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Folder_path_is_accepted_like_a_file()
    {
        var folder = Path.Combine(_root, "Мои фото", "Отпуск 2023");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(_root, "тест 1.heic");
        File.WriteAllText(file, "x");

        var args = CommandLine.Parse(["--convert", folder, file, Path.Combine(_root, "нет такого")]);

        Assert.Equal([folder, file], args.ConvertPaths);
    }
}
