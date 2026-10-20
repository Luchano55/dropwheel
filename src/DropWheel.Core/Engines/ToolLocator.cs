using DropWheel.Conversions;

namespace DropWheel.Engines;

/// <summary>
/// Где лежат ffmpeg и magick. Порядок: явный путь → переменная окружения
/// (DROPWHEEL_FFMPEG / DROPWHEEL_MAGICK) → папка tools/ рядом с exe → PATH.
/// На Linux без ImageMagick 7 используется convert из ImageMagick 6 (синтаксис совместим).
/// </summary>
public sealed class ToolLocator
{
    public string? FfmpegPath { get; init; }
    public string? MagickPath { get; init; }
    public string ToolsDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "tools");

    public string Resolve(EngineKind engine) => engine switch
    {
        EngineKind.FFmpeg => Find(FfmpegPath, "DROPWHEEL_FFMPEG", ["ffmpeg"])
            ?? throw new ConversionException("Не найден ffmpeg. Положите ffmpeg.exe в папку tools рядом с программой."),
        EngineKind.ImageMagick => Find(MagickPath, "DROPWHEEL_MAGICK", ["magick", "convert"])
            ?? throw new ConversionException("Не найден ImageMagick. Положите magick.exe в папку tools рядом с программой."),
        _ => throw new ArgumentOutOfRangeException(nameof(engine)),
    };

    public bool IsAvailable(EngineKind engine)
    {
        try { Resolve(engine); return true; }
        catch (ConversionException) { return false; }
    }

    private string? Find(string? explicitPath, string envVar, string[] names)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return File.Exists(explicitPath) ? explicitPath : null;

        var env = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

        foreach (var name in names)
        {
            // convert.exe в Windows — системная утилита для дисков, его не ищем.
            if (OperatingSystem.IsWindows() && name == "convert") continue;
            var exe = OperatingSystem.IsWindows() ? name + ".exe" : name;
            foreach (var dir in CandidateDirs())
            {
                var candidate = Path.Combine(dir, exe);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private IEnumerable<string> CandidateDirs()
    {
        yield return ToolsDirectory;
        // Распакованные архивы обычно кладут exe в подпапки: tools/ffmpeg/bin, tools/imagemagick.
        if (Directory.Exists(ToolsDirectory))
            foreach (var sub in Directory.EnumerateDirectories(ToolsDirectory))
            {
                yield return sub;
                yield return Path.Combine(sub, "bin");
            }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (!string.IsNullOrWhiteSpace(dir)) yield return dir;
    }
}
