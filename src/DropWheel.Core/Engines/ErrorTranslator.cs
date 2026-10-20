using DropWheel.Conversions;

namespace DropWheel.Engines;

/// <summary>Короткая человеческая причина ошибки по выводу утилиты.</summary>
public static class ErrorTranslator
{
    /// <summary>
    /// Только для 7-Zip и раньше общих строк: у опасной ссылки 7-Zip пишет ещё и «No such file or directory»,
    /// а «Data Error» у других утилит может значить другое.
    /// Неверный пароль: «Data Error in encrypted file. Wrong password?» / «Cannot open encrypted archive. Wrong password?».
    /// </summary>
    private static readonly (string Needle, string Message)[] SevenZipKnown =
    [
        ("Wrong password", "Архив с паролем не поддерживается"),
        ("Dangerous link path", "Архив небезопасен: в нём ссылки за пределы папки"),
        ("Cannot create hard link", "Не удалось создать ссылку из архива"),
        ("Cannot create symbolic link", "Не удалось создать ссылку из архива"),
        ("Duplicate filename on disk", "В 7Z нельзя положить файлы с одинаковыми именами из разных папок"),
        ("Unsupported Method", "Архив сжат неподдерживаемым способом"),
        ("Unexpected end of archive", "Архив повреждён или не докачан"),
        ("Is not archive", "Файл повреждён или это не архив"),
        ("Cannot open the file as", "Файл повреждён или это не архив"),
        ("CRC Failed", "Архив повреждён"),
        ("Data Error", "Архив повреждён"),
        ("Headers Error", "Архив повреждён"),
    ];

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
        ("only possible from text to text", "Субтитры в видео — картинками, в текст (SRT) их не перевести"),
        ("no decode delegate", "ImageMagick не умеет читать этот формат"),
        ("no encode delegate", "ImageMagick не умеет сохранять в этот формат"),
        ("NoDecodeDelegateForThisImageFormat", "ImageMagick не умеет читать этот формат"),
        ("NoEncodeDelegateForThisImageFormat", "ImageMagick не умеет сохранять в этот формат"),
        ("not authorized", "ImageMagick запретил этот формат своей политикой безопасности"),
        ("improper image header", "Файл повреждён или формат не распознан"),
        ("Unsupported codec", "ImageMagick не умеет читать этот формат"),
        ("invalid password", "PDF защищён паролем"),        // qpdf
        ("Incorrect password", "PDF защищён паролем"),      // pdftoppm
        ("can't find PDF header", "Файл повреждён или это не PDF"),
        ("May not be a PDF file", "Файл повреждён или это не PDF"),
        ("Copying of text from this document is not allowed", "Автор PDF запретил копировать из него текст"), // pdftotext
        ("being used by another process", "Файл занят другой программой"), // Windows
        ("not enough space on the disk", "Закончилось место на диске"),    // Windows
    ];

    public static string Translate(EngineKind engine, string stderr)
    {
        foreach (var (needle, message) in engine == EngineKind.SevenZip ? SevenZipKnown.Concat(Known) : Known)
            if (stderr.Contains(needle, StringComparison.OrdinalIgnoreCase)) return message;
        return engine switch
        {
            EngineKind.FFmpeg => "ffmpeg не смог обработать файл",
            EngineKind.ImageMagick => "ImageMagick не смог обработать файл",
            EngineKind.Qpdf => "qpdf не смог обработать PDF",
            EngineKind.Pdftoppm or EngineKind.Pdftotext => "Не удалось прочитать PDF",
            EngineKind.SevenZip => "7-Zip не смог обработать архив",
            _ => "Не удалось обработать файл",
        };
    }
}
