using System.Diagnostics;
using System.Security.Cryptography;
using DropWheel.Conversions;
using DropWheel.Engines;

namespace DropWheel.Tests.Integration;

/// <summary>
/// Помощник интеграционных тестов: настоящие ffmpeg/magick, генерация исходников, проверка результатов.
/// Если утилит нет — тесты пропускаются (Skip), а не падают.
/// </summary>
internal static class Tools
{
    public static readonly ToolLocator Locator = new();

    public static bool HasFfmpeg => Locator.IsAvailable(EngineKind.FFmpeg);
    public static bool HasMagick => Locator.IsAvailable(EngineKind.ImageMagick);
    public static bool HasQpdf => Locator.IsAvailable(EngineKind.Qpdf);
    public static bool HasPdftoppm => Locator.IsAvailable(EngineKind.Pdftoppm);

    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string Run(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"{exe} {string.Join(' ', args)}\n{stderr}");
        return stdout.Result.Trim();
    }

    public static string Ffmpeg => Locator.Resolve(EngineKind.FFmpeg);
    public static string Magick => Locator.Resolve(EngineKind.ImageMagick);
    public static string Qpdf => Locator.Resolve(EngineKind.Qpdf);

    /// <summary>Число страниц PDF (заодно проверка, что qpdf его читает).</summary>
    public static int PdfPages(string path) => int.Parse(Run(Qpdf, "--show-npages", path));

    public static string Ffprobe
    {
        get
        {
            var dir = Path.GetDirectoryName(Ffmpeg)!;
            var name = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
            return Path.Combine(dir, name);
        }
    }

    /// <summary>Видео с тестовой картинкой и звуком (или без звука). Нечётный размер кадра — нарочно.</summary>
    public static string MakeVideo(string path, int w = 1281, int h = 721, bool audio = true, double seconds = 2)
    {
        var dur = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var args = new List<string> { "-v", "error", "-y", "-f", "lavfi", "-i", $"testsrc=size={w}x{h}:rate=25:duration={dur}" };
        if (audio) args.AddRange(["-f", "lavfi", "-i", $"sine=frequency=440:duration={dur}", "-c:a", "flac"]);
        args.AddRange(["-c:v", "ffv1", "-shortest", path]);
        Run(Ffmpeg, [.. args]);
        return path;
    }

    public static string MakeAudio(string path)
    {
        Run(Ffmpeg, "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=2", path);
        return path;
    }

    public static string MakeImage(string path, int w, int h)
    {
        Run(Ffmpeg, "-v", "error", "-y", "-f", "lavfi", "-i", $"testsrc=size={w}x{h}", "-frames:v", "1", path);
        return path;
    }

    /// <summary>Размер картинки через ImageMagick (одинаково для IM6 convert и IM7 magick).</summary>
    public static (int W, int H) ImageSize(string path)
    {
        var s = Run(Magick, path + "[0]", "-format", "%w %h", "info:").Split(' ');
        return (int.Parse(s[0]), int.Parse(s[1]));
    }

    /// <summary>Потоки файла: «h264 1280 720», «aac» …</summary>
    public static string[] Streams(string path) =>
        Run(Ffprobe, "-v", "error", "-show_entries", "stream=codec_name,width,height", "-of", "csv=p=0:s=x", path)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public static bool CanWrite(string format) =>
        Run(Magick, "-list", "format").Split('\n')
            .Any(l => l.TrimStart().StartsWith(format, StringComparison.OrdinalIgnoreCase) && l.Contains("rw"));
}
