using DropWheel.Conversions;

namespace DropWheel.Engines;

/// <summary>
/// Где лежат ffmpeg и magick. Порядок: явный путь → переменная окружения
/// (DROPWHEEL_FFMPEG / _MAGICK / _QPDF / _PDFTOPPM / _PDFTOTEXT / _7Z) → папка tools/ рядом с exe → PATH.
/// На Linux без ImageMagick 7 используется convert из ImageMagick 6 (синтаксис совместим).
/// </summary>
public sealed class ToolLocator
{
    public string? FfmpegPath { get; init; }
    public string? MagickPath { get; init; }
    public string? QpdfPath { get; init; }
    public string? PdftoppmPath { get; init; }
    public string? PdftotextPath { get; init; }
    public string? SevenZipPath { get; init; }
    public string ToolsDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "tools");

    public string Resolve(EngineKind engine) => engine switch
    {
        EngineKind.FFmpeg => Find(FfmpegPath, "DROPWHEEL_FFMPEG", ["ffmpeg"])
            ?? throw new ConversionException("Не найден ffmpeg. Положите ffmpeg.exe в папку tools рядом с программой."),
        EngineKind.ImageMagick => Find(MagickPath, "DROPWHEEL_MAGICK", ["magick", "convert"])
            ?? throw new ConversionException("Не найден ImageMagick. Положите magick.exe в папку tools рядом с программой."),
        EngineKind.Qpdf => Find(QpdfPath, "DROPWHEEL_QPDF", ["qpdf"])
            ?? throw new ConversionException("Не найден qpdf. Положите qpdf.exe в папку tools рядом с программой."),
        EngineKind.Pdftoppm => Find(PdftoppmPath, "DROPWHEEL_PDFTOPPM", ["pdftoppm"])
            ?? throw new ConversionException("Не найден pdftoppm (poppler). Положите его в папку tools рядом с программой."),
        EngineKind.Pdftotext => Find(PdftotextPath, "DROPWHEEL_PDFTOTEXT", ["pdftotext"])
            ?? throw new ConversionException("Не найден pdftotext (poppler). Положите его в папку tools рядом с программой."),
        // 7z.exe (полная версия с 7z.dll — умеет RAR); 7za — урезанная, без RAR; 7zz — Linux.
        EngineKind.SevenZip => Find(SevenZipPath, "DROPWHEEL_7Z", ["7z", "7zz", "7za"])
            ?? throw new ConversionException("Не найден 7-Zip. Положите 7z.exe и 7z.dll в папку tools рядом с программой."),
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
