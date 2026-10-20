using static DropWheel.Conversions.EngineKind;
using static DropWheel.Conversions.MediaKind;


namespace DropWheel.Conversions;

/// <summary>
/// Каталог операций. Новая операция — одна строка.
/// Плейсхолдеры: {jpgq} {webpq} {crf} {crfsmall} {mp3k} — из ConversionSettings;
/// {acodec} — кодек звука под расширение результата (см. ArgumentBuilder.AudioCodec).
/// </summary>
public static class OperationCatalog
{
    private const OperationMode Conv = OperationMode.Convert, Tool = OperationMode.Tool;

    private static readonly string[] RasterKeep = ["jpg", "jpeg", "png", "webp", "bmp", "tif", "tiff", "gif"];

    // Чётные размеры кадра обязательны для yuv420p.
    private const string H264 =
        "-c:v libx264 -preset medium -pix_fmt yuv420p -c:a aac -b:a 160k -movflags +faststart";
    private const string Even = "scale=trunc(iw/2)*2:trunc(ih/2)*2";
    // Короткая сторона → не больше 720, пропорции сохраняются, размеры чётные.
    private const string To720 =
        "scale=w='trunc(iw*min(720,min(iw,ih))/min(iw,ih)/2)*2':h='trunc(ih*min(720,min(iw,ih))/min(iw,ih)/2)*2'";

    public static readonly IReadOnlyList<Operation> All =
    [
        // Изображения — конвертация
        new("to-jpg",  "JPG",  Conv, Image, ImageMagick, "-auto-orient -quality {jpgq}",  OutputRule.Fixed("jpg")),
        new("to-png",  "PNG",  Conv, Image, ImageMagick, "-auto-orient",                  OutputRule.Fixed("png")),
        new("to-webp", "WebP", Conv, Image, ImageMagick, "-auto-orient -quality {webpq}", OutputRule.Fixed("webp")),
        new("to-avif", "AVIF", Conv, Image, ImageMagick, "-auto-orient -quality 70",      OutputRule.Fixed("avif")),
        new("to-pdf",  "PDF",  Conv, Image, ImageMagick, "-auto-orient -compress jpeg -quality {jpgq}",OutputRule.Fixed("pdf")),
        // Изображения — инструменты
        new("img-compress", "Сжать",          Tool, Image, ImageMagick, "-auto-orient -quality 80",               OutputRule.Same("jpg", "jpg", "jpeg", "webp"), " (сжато)"),
        new("img-half",     "Уменьшить ×2",   Tool, Image, ImageMagick, "-auto-orient -resize 50%",               OutputRule.Same("jpg", RasterKeep), " (50%)"),
        new("img-1920",     "До 1920 px",     Tool, Image, ImageMagick, "-auto-orient -resize 1920x1920>",        OutputRule.Same("jpg", RasterKeep), " (1920)"),
        new("img-strip",    "Без метаданных", Tool, Image, ImageMagick, "-auto-orient -strip",                    OutputRule.Same("jpg", RasterKeep), " (без метаданных)"),

        // Видео — конвертация
        new("to-mp4",       "MP4",  Conv, Video, FFmpeg, $"-map 0:v:0 -map 0:a:0? -vf {Even} -crf {{crf}} {H264}", OutputRule.Fixed("mp4")),
        new("to-webm",      "WebM", Conv, Video, FFmpeg, "-map 0:v:0 -map 0:a:0? -c:v libvpx-vp9 -crf 32 -b:v 0 -row-mt 1 -deadline good -cpu-used 4 -c:a libopus -b:a 128k", OutputRule.Fixed("webm")),
        new("to-gif",       "GIF",  Conv, Video, FFmpeg, "-an -vf fps=15,scale='min(640,iw)':-1:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse -loop 0", OutputRule.Fixed("gif")),
        new("video-to-mp3", "MP3",  Conv, Video, FFmpeg, "-vn -map 0:a:0 {acodec}", OutputRule.Fixed("mp3")),
        // Видео — инструменты
        new("video-compress", "Сжать",      Tool, Video, FFmpeg, $"-map 0:v:0 -map 0:a:0? -vf {Even} -crf {{crfsmall}} {H264}", OutputRule.Fixed("mp4"), " (сжато)"),
        new("video-720p",     "720p",       Tool, Video, FFmpeg, $"-map 0:v:0 -map 0:a:0? -vf {To720} -crf {{crf}} {H264}",     OutputRule.Fixed("mp4"), " (720p)"),
        new("video-mute",     "Без звука",  Tool, Video, FFmpeg, "-map 0 -map -0:a -c copy",                                     OutputRule.Same("mp4", "mp4", "m4v", "mov", "mkv", "webm", "avi"), " (без звука)"),

        // Аудио — конвертация
        new("to-mp3",  "MP3",  Conv, Audio, FFmpeg, "-vn {acodec}", OutputRule.Fixed("mp3")),
        new("to-m4a",  "M4A",  Conv, Audio, FFmpeg, "-vn {acodec}", OutputRule.Fixed("m4a")),
        new("to-wav",  "WAV",  Conv, Audio, FFmpeg, "-vn {acodec}", OutputRule.Fixed("wav")),
        new("to-flac", "FLAC", Conv, Audio, FFmpeg, "-vn {acodec}", OutputRule.Fixed("flac")),
        new("to-ogg",  "OGG",  Conv, Audio, FFmpeg, "-vn {acodec}", OutputRule.Fixed("ogg")),
        // Аудио — инструменты (loudnorm внутри работает на 192 кГц — возвращаем 48 кГц)
        new("audio-normalize", "Нормализовать", Tool, Audio, FFmpeg, "-vn -af loudnorm=I=-16:TP=-1.5:LRA=11 -ar 48000 {acodec}",
            OutputRule.Same("mp3", "mp3", "m4a", "wav", "flac", "ogg", "opus"), " (норм)"),
    ];

    public static Operation? Find(string id) =>
        All.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Операции для одного файла (по расширению).</summary>
    public static IReadOnlyList<Operation> ForFile(string path) =>
        All.Where(o => o.AppliesTo(Path.GetExtension(path))).ToList();

    /// <summary>Операции, общие для всех файлов. Пустой список — «Нечего предложить».</summary>
    public static IReadOnlyList<Operation> ForFiles(IEnumerable<string> paths)
    {
        IEnumerable<Operation>? common = null;
        foreach (var path in paths)
        {
            var ops = ForFile(path);
            common = common is null ? ops : common.Intersect(ops);
        }
        return common?.ToList() ?? [];
    }
}
