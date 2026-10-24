using System.Diagnostics;
using System.Text;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;
using DropWheel.Localization;

namespace DropWheel.Capture;

/// <summary>
/// Один запущенный ffmpeg записи экрана (решение D-RECORD). Пишет во временный MP4 в скрытой папке
/// <see cref="Converter.TempDirName"/> внутри папки записей; «Стоп» — «q» в stdin (ffmpeg штатно дописывает файл),
/// не ответил за отведённое время — процесс убивается. После выхода <see cref="SaveAsync"/> переносит MP4 на свободное
/// имя или делает из него GIF (та же операция, что «GIF» на колесе) и удаляет временный файл.
/// </summary>
public sealed class ScreenRecorder : IDisposable
{
    private const int StderrTailLines = 40;

    private readonly Process _process;
    private readonly Queue<string> _stderr = new();
    private readonly string _tempDir;
    private readonly Action<string>? _log;
    private bool _released;

    private ScreenRecorder(Process process, string tempDir, string tempPath, Action<string>? log)
    {
        _process = process;
        _tempDir = tempDir;
        TempPath = tempPath;
        _log = log;
        Exited = process.WaitForExitAsync();
    }

    /// <summary>Временный MP4 (пока идёт запись — растёт).</summary>
    public string TempPath { get; }

    /// <summary>Завершается, когда ffmpeg вышел (по «q», по пределу длины или с ошибкой).</summary>
    public Task Exited { get; }

    public bool HasExited => Exited.IsCompleted;

    /// <summary>Последние строки stderr ffmpeg (для журнала и текста ошибки).</summary>
    public string StderrTail
    {
        get { lock (_stderr) return string.Join('\n', _stderr); }
    }

    /// <summary>
    /// Запустить ffmpeg. <paramref name="arguments"/> получает путь временного MP4 и возвращает команду
    /// (<see cref="ScreenRecord.Arguments(PixelRect, string)"/>). Не запустился — ConversionException.
    /// </summary>
    public static ScreenRecorder Start(ToolLocator tools, string folder, Func<string, IReadOnlyList<string>> arguments,
        Action<string>? log = null)
    {
        var exe = tools.Resolve(EngineKind.FFmpeg);
        Directory.CreateDirectory(folder);
        var tempDir = Converter.AcquireTempDir(folder);
        var temp = Path.Combine(tempDir, $".dropwheel-{Guid.NewGuid():N}.mp4");
        var args = arguments(temp);
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            // stdin открыт: «q» — штатная остановка.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        log?.Invoke($"> {exe} {string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}");

        var process = new Process { StartInfo = psi };
        ScreenRecorder? recorder = null;
        try
        {
            process.Start();
            recorder = new ScreenRecorder(process, tempDir, temp, log);
            process.ErrorDataReceived += (_, e) => recorder.AddStderr(e.Data);
            process.OutputDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            return recorder;
        }
        catch (Exception ex)
        {
            process.Dispose();
            Converter.ReleaseTempDir(tempDir);
            throw new ConversionException(Loc.F(Loc.T.ErrCantStart, Path.GetFileName(exe)), ex.ToString(), ex);
        }
    }

    private void AddStderr(string? line)
    {
        if (line is null) return;
        lock (_stderr)
        {
            _stderr.Enqueue(line);
            if (_stderr.Count > StderrTailLines) _stderr.Dequeue();
        }
    }

    /// <summary>«q» в stdin и ждать выхода; не вышел за <paramref name="timeout"/> — убить.</summary>
    public async Task StopAsync(TimeSpan timeout)
    {
        if (!SendQuit()) return;
        if (await Task.WhenAny(Exited, Task.Delay(timeout)) != Exited) Kill();
        await Exited;
    }

    /// <summary>То же, что <see cref="StopAsync"/>, но синхронно — при выходе из программы.</summary>
    public void Stop(TimeSpan timeout)
    {
        if (!SendQuit()) return;
        if (!_process.WaitForExit(timeout)) Kill();
        _process.WaitForExit();
    }

    private bool SendQuit()
    {
        if (HasExited) return false;
        try
        {
            _process.StandardInput.Write('q');
            _process.StandardInput.Flush();
            _process.StandardInput.Close();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // ffmpeg уже выходит — stdin закрыт.
        }
        return true;
    }

    private void Kill()
    {
        _log?.Invoke("Запись экрана: ffmpeg не остановился по «q» — завершаю процесс");
        try { _process.Kill(entireProcessTree: true); }
        catch (Exception) { /* уже вышел */ }
    }

    /// <summary>
    /// После выхода ffmpeg: MP4 → «Запись ….mp4» на свободное имя в <paramref name="folder"/>;
    /// GIF → «Запись ….gif» второй ступенью (<paramref name="converter"/>, операция «GIF»). Временный MP4 удаляется всегда.
    /// ffmpeg завершился с ошибкой или ничего не записал — ConversionException.
    /// </summary>
    public async Task<string> SaveAsync(RecordFormat format, string folder, string stem, Converter converter,
        CancellationToken ct = default)
    {
        await Exited;
        try
        {
            EnsureRecorded();
            if (format == RecordFormat.Mp4) return Converter.MoveToFreeName(TempPath, Path.Combine(folder, stem + ".mp4"), folder: false);
            return await converter.ConvertAsync([TempPath], ScreenRecord.GifOperation, log: _log, ct: ct,
                target: new OutputTarget(folder, stem, KeepSuffix: false));
        }
        finally
        {
            Release();
        }
    }

    /// <summary>Выход из программы: только MP4 (GIF делать некогда), синхронно. null — нечего сохранять.</summary>
    public string? SaveMp4Now(string folder, string stem)
    {
        try
        {
            EnsureRecorded();
            return Converter.MoveToFreeName(TempPath, Path.Combine(folder, stem + ".mp4"), folder: false);
        }
        catch (Exception ex)
        {
            _log?.Invoke("Запись экрана не сохранилась: " + ex.Message);
            return null;
        }
        finally
        {
            Release();
        }
    }

    private void EnsureRecorded()
    {
        var tail = StderrTail;
        var code = _process.HasExited ? _process.ExitCode : -1;
        var info = new FileInfo(TempPath);
        if (code != 0)
        {
            _log?.Invoke($"ffmpeg (запись экрана) завершился с кодом {code}\n{tail}");
            throw new ConversionException(ErrorTranslator.Translate(EngineKind.FFmpeg, tail), $"ffmpeg: код {code}\n{tail}");
        }
        if (!info.Exists || info.Length == 0) throw new ConversionException(Loc.T.RecordNothing, tail);
    }

    private void Release()
    {
        if (_released) return;
        _released = true;
        try
        {
            if (File.Exists(TempPath)) File.Delete(TempPath);
        }
        catch (Exception) { /* не критично */ }
        Converter.ReleaseTempDir(_tempDir);
    }

    /// <summary>Не сохранять: ffmpeg убит (если ещё пишет), временный файл удалён.</summary>
    public void Dispose()
    {
        if (!HasExited)
        {
            Kill();
            _process.WaitForExit(10_000);
            // Exited — задача WaitForExitAsync: после синхронного ожидания она завершается чуть позже — дождаться.
            try { Exited.Wait(5_000); } catch (Exception) { }
        }
        Release();
        _process.Dispose();
    }
}
