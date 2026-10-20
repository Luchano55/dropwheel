using System.Diagnostics;
using System.Text;
using DropWheel.Conversions;

namespace DropWheel.Engines;

/// <summary>Запускает ffmpeg / magick / qpdf / pdftoppm, читает прогресс, превращает сбой в ConversionException.</summary>
public sealed class EngineRunner(ToolLocator tools)
{
    private const int StderrTailLines = 40;

    public async Task RunAsync(EngineKind engine, IReadOnlyList<string> args,
        IProgress<double>? progress = null, Action<string>? log = null, CancellationToken ct = default)
    {
        var exe = tools.Resolve(engine);
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        log?.Invoke($"> {exe} {string.Join(' ', args.Select(Quote))}");

        using var process = new Process { StartInfo = psi };
        var stderr = new Queue<string>();
        var ffProgress = new FfmpegProgress();

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stderr)
            {
                stderr.Enqueue(e.Data);
                if (stderr.Count > StderrTailLines) stderr.Dequeue();
            }
            if (ffProgress.ReadStderr(e.Data) is { } p) progress?.Report(p);
        };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null && ffProgress.ReadProgressLine(e.Data) is { } p) progress?.Report(p);
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new ConversionException($"Не удалось запустить {Path.GetFileName(exe)}", ex.ToString(), ex);
        }
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* уже завершился */ }
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        string tail;
        lock (stderr) tail = string.Join('\n', stderr);
        if (!Succeeded(engine, process.ExitCode))
        {
            log?.Invoke(tail);
            throw new ConversionException(ErrorTranslator.Translate(engine, tail),
                $"{Path.GetFileName(exe)} завершился с кодом {process.ExitCode}\n{tail}");
        }
        if (process.ExitCode != 0 && tail.Length > 0) log?.Invoke(tail); // предупреждения при успехе
        progress?.Report(1.0);
    }

    /// <summary>
    /// Код выхода «успех». qpdf: 3 — были предупреждения (например, починил битую таблицу xref),
    /// но результат записан.
    /// </summary>
    public static bool Succeeded(EngineKind engine, int exitCode) =>
        exitCode == 0 || (engine == EngineKind.Qpdf && exitCode == 3);

    private static string Quote(string a) => a.Contains(' ') ? $"\"{a}\"" : a;
}
