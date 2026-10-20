using System.Diagnostics;
using System.Text;

namespace DemoDriver;

/// <summary>Запуск утилиты (ffmpeg): аргументы через ArgumentList (кириллица, пробелы), вывод — в файл журнала.</summary>
internal static class Proc
{
    /// <returns>Код выхода; −1 — не запустилась или не уложилась во время.</returns>
    public static int Run(string exe, IReadOnlyList<string> args, string workDir, string logFile, TimeSpan timeout)
    {
        Log.Write($"> {Path.GetFileName(exe)} {string.Join(' ', args.Select(Quote))}");
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            WorkingDirectory = workDir,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var output = new StringBuilder();
        var gate = new object();
        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (gate) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (gate) output.AppendLine(e.Data); };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            Log.Error($"{exe} не запустился: {ex.Message}");
            return -1;
        }
        process.StandardInput.Close(); // ffmpeg не ждёт ввода с клавиатуры
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var sw = Stopwatch.StartNew();
        var finished = process.WaitForExit((int)timeout.TotalMilliseconds);
        if (!finished)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            process.WaitForExit(5000);
        }
        else
        {
            process.WaitForExit();
        }

        string text;
        lock (gate) text = output.ToString();
        try { File.AppendAllText(logFile, $"> {exe} {string.Join(' ', args.Select(Quote))}\n{text}\n", new UTF8Encoding(false)); }
        catch { }

        var code = finished ? process.ExitCode : -1;
        Log.Write($"  код {code}, {sw.Elapsed.TotalSeconds:F1} с");
        if (code != 0)
        {
            var tail = text.Split('\n').Where(l => l.Trim().Length > 0).TakeLast(15);
            foreach (var line in tail) Log.Write("  | " + line.TrimEnd());
        }
        return code;
    }

    private static string Quote(string a) => a.Length == 0 || a.Any(char.IsWhiteSpace) || a.Contains('\'') ? "\"" + a + "\"" : a;
}
