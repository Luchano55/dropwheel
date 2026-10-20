using DropWheel.Logging;

namespace DropWheel.Tests.Logging;

public class FileLogTests : IDisposable
{
    private readonly TempDir _dir = new();
    private string LogsDir => Path.Combine(_dir.Path, "logs");

    public void Dispose() => _dir.Dispose();

    /// <summary>Все строки из всех логов — на случай, если тест попал на полночь.</summary>
    private string[] AllLines() =>
        Directory.GetFiles(LogsDir, "*.log").OrderBy(f => f).SelectMany(File.ReadAllLines).ToArray();

    [Fact]
    public void Creates_directory_and_writes_formatted_line()
    {
        var log = new FileLog(LogsDir);
        log.Write("Готово: фото 1.jpg");

        Assert.True(Directory.Exists(LogsDir));
        Assert.Equal(DateTime.Now.ToString("yyyy-MM-dd") + ".log", Path.GetFileName(log.CurrentFile));
        var line = Assert.Single(AllLines());
        Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3} \[\d+\] Готово: фото 1\.jpg$", line);
    }

    [Fact]
    public void Multiline_messages_are_written()
    {
        var log = new FileLog(LogsDir);
        log.Write("ошибка\nстрока 2\nстрока 3");
        log.Write("дальше");

        var lines = AllLines();
        Assert.Equal(4, lines.Length);
        Assert.EndsWith("ошибка", lines[0]);
        Assert.Equal("строка 3", lines[2]);
        Assert.EndsWith("дальше", lines[3]);
    }

    [Fact]
    public void Parallel_writers_lose_nothing()
    {
        var log = new FileLog(LogsDir);
        Parallel.For(0, 20, new ParallelOptions { MaxDegreeOfParallelism = 20 }, w =>
        {
            for (var i = 0; i < 50; i++) log.Write($"писатель {w} строка {i}");
        });

        var lines = AllLines();
        Assert.Equal(1000, lines.Length);
        for (var w = 0; w < 20; w++)
            for (var i = 0; i < 50; i++)
                Assert.Contains(lines, l => l.EndsWith($"писатель {w} строка {i}"));
    }

    [Fact]
    public void Old_logs_are_deleted_others_kept()
    {
        Directory.CreateDirectory(LogsDir);
        var old = Path.Combine(LogsDir, DateTime.Today.AddDays(-30).ToString("yyyy-MM-dd") + ".log");
        var ancient = Path.Combine(LogsDir, "2001-01-01.log");
        var recent = Path.Combine(LogsDir, DateTime.Today.AddDays(-3).ToString("yyyy-MM-dd") + ".log");
        var foreign = Path.Combine(LogsDir, "заметки.log");
        var notLog = Path.Combine(LogsDir, "2001-01-01.txt");
        foreach (var f in new[] { old, ancient, recent, foreign, notLog }) File.WriteAllText(f, "x");

        _ = new FileLog(LogsDir, keepDays: 14);

        Assert.False(File.Exists(old));
        Assert.False(File.Exists(ancient));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(foreign));
        Assert.True(File.Exists(notLog));
    }

    [Fact]
    public void Bad_directory_does_not_throw()
    {
        // Папку нельзя создать: на её месте лежит файл.
        var file = _dir.File("занято");
        var log = new FileLog(Path.Combine(file, "logs"));
        log.Write("никуда");
    }
}
