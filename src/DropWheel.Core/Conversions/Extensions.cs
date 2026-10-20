namespace DropWheel.Conversions;

/// <summary>Поддерживаемые входные расширения и их типы.</summary>
public static class Extensions
{
    public static readonly IReadOnlyList<string> Image =
        ["jpg", "jpeg", "jfif", "png", "webp", "heic", "heif", "avif", "bmp", "tif", "tiff", "gif"];

    public static readonly IReadOnlyList<string> Video =
        ["mp4", "m4v", "mov", "mkv", "webm", "avi"];

    public static readonly IReadOnlyList<string> Audio =
        ["mp3", "m4a", "wav", "flac", "ogg", "opus"];

    public static readonly IReadOnlyList<string> Pdf = ["pdf"];

    /// <summary>Документы Microsoft Office (и форматы, которые Office открывает).</summary>
    public static readonly IReadOnlyList<string> Word = ["doc", "docx", "docm", "rtf", "odt"];
    public static readonly IReadOnlyList<string> Excel = ["xls", "xlsx", "xlsm", "ods", "csv"];
    public static readonly IReadOnlyList<string> PowerPoint = ["ppt", "pptx", "pptm", "odp"];
    public static readonly IReadOnlyList<string> Document = [.. Word, .. Excel, .. PowerPoint];

    public static readonly IReadOnlyList<string> Archive = ["zip"];

    /// <summary>Текстовые файлы: Markdown/HTML/TXT → PDF через Edge, перекодировка.</summary>
    public static readonly IReadOnlyList<string> Text = ["txt", "md", "markdown", "html", "htm"];

    /// <summary>Синонимы одного формата: исходнику .jpeg не предлагаем «→ JPG».</summary>
    private static readonly string[][] Aliases =
        [["jpg", "jpeg", "jfif"], ["tif", "tiff"], ["heic", "heif"], ["mp4", "m4v"]];

    public static string Normalize(string extension) =>
        extension.TrimStart('.').ToLowerInvariant();

    public static MediaKind? KindOf(string extension)
    {
        var ext = Normalize(extension);
        if (Image.Contains(ext)) return MediaKind.Image;
        if (Video.Contains(ext)) return MediaKind.Video;
        if (Audio.Contains(ext)) return MediaKind.Audio;
        if (Pdf.Contains(ext)) return MediaKind.Pdf;
        if (Document.Contains(ext)) return MediaKind.Document;
        if (Archive.Contains(ext)) return MediaKind.Archive;
        if (Text.Contains(ext)) return MediaKind.Text;
        return null;
    }

    public static bool SameFormat(string a, string b)
    {
        a = Normalize(a);
        b = Normalize(b);
        return a == b || Aliases.Any(g => g.Contains(a) && g.Contains(b));
    }

    /// <summary>Все известные расширения (для пункта меню проводника см. ContextMenuRegistration).</summary>
    public static IEnumerable<string> All => Image.Concat(Video).Concat(Audio).Concat(Pdf).Concat(Document).Concat(Archive).Concat(Text);
}
