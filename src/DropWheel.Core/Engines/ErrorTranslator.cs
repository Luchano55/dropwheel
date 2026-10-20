using DropWheel.Conversions;

namespace DropWheel.Engines;

/// <summary>Короткая человеческая причина ошибки по выводу утилиты.</summary>
public static class ErrorTranslator
{
    private static readonly (string Needle, string Message)[] Known =
    [
        ("matches no streams", "В файле нет нужной дорожки (например, звука)"),
        ("does not contain any stream", "В файле нет нужной дорожки (например, звука)"),
        ("Invalid data found", "Файл повреждён или формат не распознан"),
        ("moov atom not found", "Видео повреждено или не докачано"),
        ("No such file or directory", "Файл не найден"),
        ("Permission denied", "Нет доступа к файлу или папке"),
        ("No space left", "Закончилось место на диске"),
        ("Unknown encoder", "В этой сборке ffmpeg нет нужного кодировщика"),
        ("no decode delegate", "ImageMagick не умеет читать этот формат"),
        ("no encode delegate", "ImageMagick не умеет сохранять в этот формат"),
        ("NoDecodeDelegateForThisImageFormat", "ImageMagick не умеет читать этот формат"),
        ("NoEncodeDelegateForThisImageFormat", "ImageMagick не умеет сохранять в этот формат"),
        ("not authorized", "ImageMagick запретил этот формат своей политикой безопасности"),
        ("improper image header", "Файл повреждён или формат не распознан"),
        ("Unsupported codec", "ImageMagick не умеет читать этот формат"),
    ];

    public static string Translate(EngineKind engine, string stderr)
    {
        foreach (var (needle, message) in Known)
            if (stderr.Contains(needle, StringComparison.OrdinalIgnoreCase)) return message;
        return engine == EngineKind.FFmpeg ? "ffmpeg не смог обработать файл" : "ImageMagick не смог обработать файл";
    }
}
