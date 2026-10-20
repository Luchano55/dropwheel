namespace DropWheel.Tests;

/// <summary>Временная папка с кириллицей и пробелом в имени — удаляется после теста.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dropwheel-тесты", Guid.NewGuid().ToString("N")[..8], "Мои фото");
        Directory.CreateDirectory(Path);
    }

    public string File(string name, string content = "")
    {
        var p = System.IO.Path.Combine(Path, name);
        System.IO.File.WriteAllText(p, content);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(System.IO.Path.GetDirectoryName(Path)!, recursive: true); } catch { }
    }
}
