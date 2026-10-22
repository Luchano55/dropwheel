using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DropWheel.Conversions;

namespace DropWheel.Engines;

/// <summary>
/// Для окна обрезки (D-TRIM): длительность ролика и кадр в нужный момент — тем же ffmpeg, без ffprobe.
/// Длительность — из строки «Duration:» у «ffmpeg -i»; если её нет («N/A», запись из браузера) — ролик
/// пробегается копированием без записи (-f null), и берётся последнее «time=».
/// </summary>
public static partial class VideoProbe
{
    [GeneratedRegex(@"time=(\d+):(\d\d):(\d\d(?:\.\d+)?)")]
    private static partial Regex TimeRegex();

    /// <summary>Длительность в секундах; null — ffmpeg не прочитал файл.</summary>
    public static async Task<double?> DurationAsync(ToolLocator tools, string video, CancellationToken ct = default)
    {
        var ffmpeg = tools.Resolve(EngineKind.FFmpeg);
        // Без выхода ffmpeg пишет сведения о файле и завершается с кодом 1 — это ожидаемо.
        var (_, info) = await RunAsync(ffmpeg, ["-hide_banner", "-nostdin", "-i", video], ct);
        if (ParseDuration(info) is { } duration and > 0) return duration;

        var (_, scan) = await RunAsync(ffmpeg,
            ["-hide_banner", "-nostdin", "-i", video, "-map", "0:v:0?", "-map", "0:a:0?", "-c", "copy", "-f", "null", "-"], ct);
        return ParseLastTime(scan) is { } t and > 0 ? t : null;
    }

    /// <summary>Длительность из вывода «ffmpeg -i»: первая строка «Duration: 00:01:02.50» (у входа).</summary>
    public static double? ParseDuration(string stderr)
    {
        var progress = new FfmpegProgress();
        foreach (var line in stderr.Split('\n')) progress.ReadStderr(line.TrimEnd('\r'));
        return progress.DurationSeconds > 0 ? progress.DurationSeconds : null;
    }

    /// <summary>Последнее «time=00:00:02.96» в выводе ffmpeg (строка статистики).</summary>
    public static double? ParseLastTime(string stderr)
    {
        var matches = TimeRegex().Matches(stderr);
        if (matches.Count == 0) return null;
        var m = matches[^1];
        return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
            + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 60
            + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>Аргументы ffmpeg для кадра: поиск по входу до -i (быстро), один кадр, ширина width (высота — чётная, по пропорциям).</summary>
    public static IReadOnlyList<string> FrameArgs(string video, double seconds, string png, int width) =>
    [
        "-hide_banner", "-nostdin", "-y", "-v", "error",
        "-ss", VideoTrim.Arg(seconds), "-i", video,
        "-map", "0:v:0", "-frames:v", "1", "-vf", $"scale={width}:-2", "-update", "1", png,
    ];

    /// <summary>
    /// Кадр в момент seconds → PNG (временный файл, его удаляет вызывающий). У самого конца ролика кадра может
    /// не оказаться — тогда пробуем чуть раньше. false — кадр не получился.
    /// </summary>
    public static async Task<bool> FrameAsync(ToolLocator tools, string video, double seconds, string png, int width,
        CancellationToken ct = default)
    {
        var ffmpeg = tools.Resolve(EngineKind.FFmpeg);
        foreach (var back in new[] { 0.0, 0.25, 1.0 })
        {
            ct.ThrowIfCancellationRequested();
            TryDelete(png);
            await RunAsync(ffmpeg, FrameArgs(video, Math.Max(0, seconds - back), png, width), ct);
            if (File.Exists(png) && new FileInfo(png).Length > 0) return true;
            if (seconds - back <= 0) break;
        }
        return false;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Запустить ffmpeg, вернуть код выхода и stderr. Отмена — процесс убивается.</summary>
    private static async Task<(int Code, string Stderr)> RunAsync(string exe, IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = new Process { StartInfo = psi };
        process.Start();
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* уже завершился */ }
            throw;
        }
        await stdout;
        return (process.ExitCode, await stderr);
    }
}
