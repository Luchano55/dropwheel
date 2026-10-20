using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DemoDriver;

/// <summary>Журнал робота: каждая строка — время, секунды от старта и что делаем. Дублируется в файл.</summary>
internal static class Log
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object Gate = new();
    private static StreamWriter? _file;

    /// <summary>Дописывать журнал ещё и в файл (UTF-8, без BOM).</summary>
    public static void To(string path)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                _file = new StreamWriter(path, append: true, new UTF8Encoding(false)) { AutoFlush = true };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"(журнал в файл {path} не открылся: {ex.Message})");
            }
        }
    }

    public static void Write(string message)
    {
        var line = string.Create(CultureInfo.InvariantCulture,
            $"[{DateTime.Now:HH:mm:ss.fff} +{Clock.Elapsed.TotalSeconds,7:F3}s] {message}");
        lock (Gate)
        {
            Console.WriteLine(line);
            try { _file?.WriteLine(line); } catch { }
        }
    }

    public static void Error(string message) => Write("ОШИБКА: " + message);

    /// <summary>Строка для GitHub Actions: подсвечивается в интерфейсе запуска.</summary>
    public static void Annotate(string level, string message)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            Console.WriteLine($"::{level}::{message.Replace("\r", " ").Replace("\n", " ")}");
    }
}
