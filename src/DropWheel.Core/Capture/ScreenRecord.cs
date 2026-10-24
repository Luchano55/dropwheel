using System.Globalization;
using DropWheel.Conversions;
using DropWheel.Localization;

namespace DropWheel.Capture;

/// <summary>Во что сохранить запись экрана.</summary>
public enum RecordFormat
{
    Mp4,
    Gif,
}

/// <summary>
/// Запись экрана (решение D-RECORD) без окон: аргументы ffmpeg, имя и папка файла, показ времени, раскладка рамки.
/// Пишет ffmpeg (gdigrab — встроенный в Windows-сборку захват рабочего стола) во временный MP4; GIF делается из него
/// второй ступенью той же цепочкой, что и «GIF» на колесе.
/// </summary>
public static class ScreenRecord
{
    /// <summary>
    /// Сочетание по умолчанию — Ctrl+Alt+R: «R» — «record», пара к Ctrl+Alt+S (снимок) и Ctrl+Alt+V (буфер).
    /// Win+Alt+R — системная запись игр, Ctrl+Shift+R во многих программах — «обновить без кэша».
    /// </summary>
    public const string DefaultKeys = "Ctrl+Alt+R";

    public static KeyChord DefaultChord { get; } = KeyChord.Parse(DefaultKeys)!;

    /// <summary>Неверное сочетание в settings.json → Ctrl+Alt+R (каноническая запись).</summary>
    public static string NormalizeKeys(string? text) => (KeyChord.Parse(text) ?? DefaultChord).ToString();

    public const int FrameRate = 30;

    /// <summary>Предел длины записи: ffmpeg сам остановится (-t), даже если про запись забыли.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Область для H.264 (yuv420p требует чётные стороны): ширина и высота округляются вниз до чётных,
    /// не меньше 2; левый верхний угол — как был.
    /// </summary>
    public static PixelRect EvenSize(PixelRect region) =>
        new(region.X, region.Y, Math.Max(2, region.Width & ~1), Math.Max(2, region.Height & ~1));

    /// <summary>
    /// Вход ffmpeg: gdigrab, область виртуального экрана в физических пикселях (у мониторов левее или выше
    /// основного координаты отрицательные — gdigrab понимает их так же), с курсором.
    /// </summary>
    public static IReadOnlyList<string> GdigrabInput(PixelRect region)
    {
        var r = EvenSize(region);
        return
        [
            "-f", "gdigrab",
            "-framerate", Num(FrameRate),
            "-draw_mouse", "1",
            "-offset_x", Num(r.X),
            "-offset_y", Num(r.Y),
            "-video_size", $"{Num(r.Width)}x{Num(r.Height)}",
            "-i", "desktop",
        ];
    }

    /// <summary>
    /// Полная команда ffmpeg: общие ключи, вход (<see cref="GdigrabInput"/>; в тестах — генератор картинки),
    /// H.264 во временный MP4. stdin оставлен открытым: «q» в него — штатная остановка с дописанным файлом.
    /// </summary>
    public static IReadOnlyList<string> Arguments(IReadOnlyList<string> input, string output, TimeSpan? maxDuration = null) =>
    [
        "-hide_banner", "-nostats", "-y",
        .. input,
        "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-pix_fmt", "yuv420p",
        "-movflags", "+faststart",
        "-t", Num((int)(maxDuration ?? MaxDuration).TotalSeconds),
        output,
    ];

    /// <summary>Команда записи области экрана.</summary>
    public static IReadOnlyList<string> Arguments(PixelRect region, string output) => Arguments(GdigrabInput(region), output);

    /// <summary>Вторая ступень для GIF — операция «GIF» каталога (15 кадров/с, не шире 640 px, своя палитра).</summary>
    public static Operation GifOperation => OperationCatalog.Find("to-gif")
        ?? throw new InvalidOperationException("В каталоге нет to-gif");

    /// <summary>«Запись 2026-10-24 15-30-05» / «Recording 2026-10-24 15-30-05» — без двоеточий и без расширения.</summary>
    public static string FileStem(DateTime now) =>
        Loc.F(Loc.T.RecordFile, now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture));

    public static string Extension(RecordFormat format) => format == RecordFormat.Gif ? "gif" : "mp4";

    /// <summary>Свободный путь «Запись … .mp4»; две записи за секунду — «… (1).mp4».</summary>
    public static string FilePath(string folder, DateTime now, RecordFormat format, Func<string, bool>? exists = null) =>
        Jobs.OutputNamer.FirstFree(Path.Combine(folder, FileStem(now) + "." + Extension(format)), exists);

    /// <summary>
    /// Папка записей: своя папка картинок из буфера (куда снимки — туда и записи) → «В одну папку» → «Видео\DropWheel».
    /// </summary>
    public static string Folder(string? clipboardFolder, ConversionSettings settings) =>
        clipboardFolder is { Length: > 0 } own ? own
        : settings.OutputFolder is { Length: > 0 } shared ? shared
        : DefaultFolder;

    /// <summary>«Видео\DropWheel» (в Windows — %USERPROFILE%\Videos\DropWheel).</summary>
    public static string DefaultFolder
    {
        get
        {
            var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrEmpty(videos))
                videos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
            return Path.Combine(videos, "DropWheel");
        }
    }

    /// <summary>Время записи на пилюле: «00:12», «12:34», после часа — «1:02:03».</summary>
    public static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        var total = (long)elapsed.TotalSeconds;
        var (h, m, s) = (total / 3600, total / 60 % 60, total % 60);
        return h > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{h}:{m:00}:{s:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{m:00}:{s:00}");
    }

    /// <summary>
    /// Красная рамка вокруг записываемой области (толщина — физические пиксели). Окно рамки скрыто от захвата
    /// (Windows 10 2004+) — рамка рисуется по краю самой области; не скрыто — снаружи, чтобы не попасть в запись.
    /// Возвращает границы окна рамки: внутри него — кольцо толщиной <paramref name="thickness"/> по краю.
    /// </summary>
    public static PixelRect FrameBounds(PixelRect region, int thickness, bool excludedFromCapture) =>
        excludedFromCapture
            ? region
            : new PixelRect(region.X - thickness, region.Y - thickness, region.Width + 2 * thickness, region.Height + 2 * thickness);

    private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);
}
