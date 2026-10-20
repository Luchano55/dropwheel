using System.Globalization;
using System.Text;

namespace DropWheel.Logging;

/// <summary>
/// Простой лог в файлы по дням: logs/2025-01-31.log. Потокобезопасный, никогда не бросает —
/// сбой записи лога не должен ронять конвертацию.
/// </summary>
public sealed class FileLog
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly object _lock = new();
    private readonly string _directory;

    public FileLog(string directory, int keepDays = 14)
    {
        _directory = directory;
        try
        {
            Directory.CreateDirectory(directory);
            DeleteOld(keepDays);
        }
        catch { }
    }

    /// <summary>Файл, в который сейчас пишется лог (по сегодняшней дате).</summary>
    public string CurrentFile => FileFor(DateTime.Now);

    public void Write(string message)
    {
        try
        {
            var now = DateTime.Now;
            var line = string.Create(CultureInfo.InvariantCulture,
                $"{now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
            var bytes = Utf8.GetBytes(line);
            lock (_lock)
            {
                // FileShare.ReadWrite — лог можно открыть в блокноте, пока программа работает.
                using var stream = new FileStream(FileFor(now), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                stream.Write(bytes);
            }
        }
        catch { }
    }

    private string FileFor(DateTime date) =>
        Path.Combine(_directory, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");

    /// <summary>Удаляет логи старше keepDays дней — дата берётся из имени файла.</summary>
    private void DeleteOld(int keepDays)
    {
        var border = DateTime.Today.AddDays(-keepDays);
        foreach (var file in Directory.EnumerateFiles(_directory, "*.log"))
        {
            if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;
            if (date >= border) continue;
            try { File.Delete(file); } catch { }
        }
    }
}
