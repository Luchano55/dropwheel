using DropWheel.Conversions;

namespace DropWheel.Shell;

/// <summary>Что положить в буфер обмена после готовой задачи (настройка «Копировать результат в буфер обмена»).</summary>
public enum ResultClipboardKind
{
    /// <summary>Нечего: результата нет на диске.</summary>
    None,
    /// <summary>Одна картинка: сама картинка (вставка в мессенджер, редактор) и файл (вставка в проводнике).</summary>
    Image,
    /// <summary>Файлы или папки как «Копировать» в проводнике (CF_HDROP).</summary>
    Files,
}

public sealed record ResultClipboardAction(ResultClipboardKind Kind, IReadOnlyList<string> Paths);

/// <summary>
/// Логика «Копировать результат в буфер обмена» (решение D-COPYRESULT) без самого буфера: что копировать для
/// результатов одной пачки уведомления. Запись в буфер — в приложении (Shell/ClipboardWriter).
/// </summary>
public static class ResultClipboard
{
    /// <summary>
    /// Картинки, которые кладутся в буфер картинкой: статичные форматы, которые Windows открывает сама.
    /// GIF — файлом (картинкой в буфере он потерял бы анимацию), ICO/HEIC/AVIF/SVG — файлом.
    /// </summary>
    public static readonly IReadOnlyList<string> ImageExtensions = ["png", "jpg", "jpeg", "bmp", "webp", "tif", "tiff"];

    /// <summary>Файл картинки больше этого — только файлом: огромная картинка в буфере — сотни мегабайт памяти.</summary>
    public const long MaxImageBytes = 50L * 1024 * 1024;

    /// <summary>
    /// results — готовые результаты (файлы или папки) одной пачки. Один файл-картинка — картинкой (и файлом);
    /// остальное (не картинка, папка, несколько результатов) — файлами. Чего нет на диске — не в счёт.
    /// </summary>
    public static ResultClipboardAction Decide(IReadOnlyList<string> results,
        Func<string, bool>? fileExists = null, Func<string, bool>? folderExists = null, Func<string, long>? fileLength = null)
    {
        var isFile = fileExists ?? File.Exists;
        var isFolder = folderExists ?? Directory.Exists;
        var length = fileLength ?? (p => new FileInfo(p).Length);
        var existing = results
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.TrimEnd('\\', '/'))
            .Where(p => isFile(p) || isFolder(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (existing.Count == 0) return new(ResultClipboardKind.None, []);
        if (existing.Count == 1 && isFile(existing[0]) && IsImage(existing[0]) && length(existing[0]) <= MaxImageBytes)
            return new(ResultClipboardKind.Image, existing);
        return new(ResultClipboardKind.Files, existing);
    }

    public static bool IsImage(string path) => ImageExtensions.Contains(Extensions.Normalize(Path.GetExtension(path)));
}
