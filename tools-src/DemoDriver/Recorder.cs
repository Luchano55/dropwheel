using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DemoDriver;

/// <summary>
/// Запись экрана: ffmpeg -f gdigrab в промежуточный MKV (libx264 ultrafast, CRF 18).
/// MKV читается, даже если ffmpeg пришлось убить. Остановка — «q» в stdin, как в консоли.
/// <para>
/// Синхронизация с журналом сценария: ffmpeg печатает в stdout (-progress) out_time — сколько секунд
/// уже записано. Время начала записи = (момент строки − out_time); берём минимум по всем строкам,
/// так задержка кодирования почти не влияет. Монтаж переводит события сценария во время ролика по нему.
/// </para>
/// </summary>
internal sealed class Recorder : IDisposable
{
    private readonly Process _process;
    private readonly StreamWriter _log;
    private readonly object _gate = new();
    private long? _t0UnixMs;
    private double _lastOutSeconds;
    private string? _lastProgressLine;

    private Recorder(Process process, StreamWriter log)
    {
        _process = process;
        _log = log;
    }

    /// <summary>Unix-время (мс) кадра с меткой 0 в записи; null — ни одного кадра не записано.</summary>
    public long? T0UnixMs { get { lock (_gate) return _t0UnixMs; } }

    /// <summary>Сколько секунд записано.</summary>
    public double RecordedSeconds { get { lock (_gate) return _lastOutSeconds; } }

    public static Recorder? Start(string ffmpeg, string output, string logFile, int fps)
    {
        var x = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
        var y = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
        var w = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
        var h = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
        var args = new List<string>
        {
            "-hide_banner", "-y", "-loglevel", "info",
            "-f", "gdigrab", "-framerate", fps.ToString(CultureInfo.InvariantCulture), "-draw_mouse", "1",
            "-offset_x", x.ToString(CultureInfo.InvariantCulture), "-offset_y", y.ToString(CultureInfo.InvariantCulture),
            "-video_size", $"{w}x{h}",
            "-i", "desktop",
            // yuv420p требует чётных размеров.
            "-vf", "crop=trunc(iw/2)*2:trunc(ih/2)*2:0:0",
            "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency", "-crf", "18", "-pix_fmt", "yuv420p",
            "-g", (fps * 2).ToString(CultureInfo.InvariantCulture),
            "-progress", "pipe:1",
            output,
        };
        Log.Write("Запись экрана: " + ffmpeg + " " + string.Join(' ', args));

        var psi = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        StreamWriter log;
        try
        {
            log = new StreamWriter(logFile, append: false, new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch (Exception ex)
        {
            Log.Error($"журнал записи {logFile}: {ex.Message}");
            return null;
        }

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start вернул null");
        }
        catch (Exception ex)
        {
            Log.Error($"ffmpeg не запустился: {ex.Message}");
            log.Dispose();
            return null;
        }

        var recorder = new Recorder(process, log);
        process.ErrorDataReceived += (_, e) => recorder.OnStderr(e.Data);
        process.OutputDataReceived += (_, e) => recorder.OnProgress(e.Data);
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        // Ждём первые записанные кадры: так сценарий не начнётся раньше записи.
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(20))
        {
            if (process.HasExited)
            {
                Log.Error($"ffmpeg завершился сразу, код {process.ExitCode} — см. {Path.GetFileName(logFile)}");
                recorder.Dispose();
                return null;
            }
            if (recorder.RecordedSeconds >= 0.5)
            {
                Log.Write($"Запись идёт (через {sw.Elapsed.TotalSeconds:F1} с после запуска ffmpeg)");
                return recorder;
            }
            Thread.Sleep(100);
        }
        Log.Error("ffmpeg запущен, но за 20 с не записал ни одного кадра — останавливаю");
        recorder.Stop();
        recorder.Dispose();
        return null;
    }

    private void OnStderr(string? line)
    {
        if (line is null) return;
        lock (_gate)
        {
            try { _log.WriteLine(line); } catch { }
        }
    }

    private void OnProgress(string? line)
    {
        if (line is null) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        double? seconds = null;
        if (line.StartsWith("out_time_us=", StringComparison.Ordinal)
            && long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us))
            seconds = us / 1e6;
        else if (line.StartsWith("out_time=", StringComparison.Ordinal)
                 && TimeSpan.TryParse(line.AsSpan(9), CultureInfo.InvariantCulture, out var ts))
            seconds = ts.TotalSeconds;
        if (seconds is not { } s || s <= 0) return;

        lock (_gate)
        {
            _lastProgressLine = line;
            if (s >= _lastOutSeconds) _lastOutSeconds = s;
            var estimate = now - (long)Math.Round(s * 1000);
            if (_t0UnixMs is null || estimate < _t0UnixMs) _t0UnixMs = estimate;
        }
    }

    /// <summary>Остановить запись: «q» в stdin, ждём до 30 с, иначе убиваем (MKV всё равно читается).</summary>
    public void Stop()
    {
        if (_process.HasExited)
        {
            Log.Write($"ffmpeg (запись) уже завершился, код {_process.ExitCode}");
            return;
        }
        try
        {
            // stdin не закрываем сразу: ffmpeg читает «q» из канала через PeekNamedPipe.
            _process.StandardInput.Write('q');
            _process.StandardInput.Flush();
        }
        catch (Exception ex)
        {
            Log.Write("Не удалось послать ffmpeg «q»: " + ex.Message);
        }
        if (!_process.WaitForExit(30_000))
        {
            Log.Error("ffmpeg не остановился за 30 с после «q» — закрываю stdin");
            try { _process.StandardInput.Close(); } catch { }
            if (!_process.WaitForExit(5000))
            {
                Log.Error("ffmpeg всё ещё работает — убиваю процесс (MKV всё равно читается)");
                try { _process.Kill(entireProcessTree: true); } catch { }
                _process.WaitForExit(5000);
            }
        }
        else
        {
            _process.WaitForExit(); // дочитать stdout/stderr
        }
        Log.Write($"Запись остановлена: код {(_process.HasExited ? _process.ExitCode : -1)}, записано {RecordedSeconds:F2} с, "
            + $"последняя строка прогресса: {_lastProgressLine}");
    }

    public void Dispose()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { }
        _process.Dispose();
        lock (_gate)
        {
            try { _log.Dispose(); } catch { }
        }
    }
}
