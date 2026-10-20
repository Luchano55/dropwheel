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
            // Пустой stdin: утилита никогда не ждёт ввода (пароль, «заменить файл?») — сразу ошибка.
            RedirectStandardInput = true,
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
            process.StandardInput.Close();
        }
        catch (Exception ex)
        {
            throw new ConversionException($"Не удалось запустить {Path.GetFileName(exe)}", ex.ToString(), ex);
        }
        process.BeginErrorReadLine();
        // 7-Zip пишет прогресс без перевода строки (стирает его \b) — читаем stdout кусками, а не строками.
        var rawStdout = engine == EngineKind.SevenZip ? ReadSevenZipProgressAsync(process.StandardOutput, progress) : null;
        if (rawStdout is null) process.BeginOutputReadLine();

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
        finally
        {
            if (rawStdout is not null) await rawStdout;
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
    /// 7-Zip: только 0. Код 1 («предупреждения») — не успех: при упаковке это значит, что какой-то файл
    /// не удалось прочитать и архив вышел неполным; при распаковке — что-то не распаковалось.
    /// 2 — ошибка (в том числе неверный пароль и опасные ссылки в архиве), 7 — ошибка в аргументах, 8 — мало памяти.
    /// pdftotext: только 0 (1 — не открыл PDF, 2 — не записал TXT, 3 — автор PDF запретил копировать текст).
    /// </summary>
    public static bool Succeeded(EngineKind engine, int exitCode) =>
        exitCode == 0 || (engine == EngineKind.Qpdf && exitCode == 3);

    private static async Task ReadSevenZipProgressAsync(StreamReader stdout, IProgress<double>? progress)
    {
        var parser = new SevenZipProgress();
        var buffer = new char[4096];
        try
        {
            int read;
            while ((read = await stdout.ReadAsync(buffer)) > 0)
                if (parser.Read(buffer.AsSpan(0, read)) is { } p) progress?.Report(p);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Процесс убит при отмене — поток закрыт.
        }
    }

    private static string Quote(string a) => a.Contains(' ') ? $"\"{a}\"" : a;
}
