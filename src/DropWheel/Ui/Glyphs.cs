using DropWheel.Conversions;

namespace DropWheel.Ui;

/// <summary>
/// Коды значков Segoe MDL2 Assets / Segoe Fluent Icons (одинаковые в обоих шрифтах).
/// Только давно существующие значки — чтобы на Windows 10 не было пустых квадратов.
/// </summary>
public static class Glyphs
{
    public const string Photo = "";
    public const string Video = "";
    public const string Audio = "";
    public const string Document = "";
    public const string Text = "";
    public const string Package = "";
    public const string FolderOpen = "";
    public const string Folder = "";
    public const string Convert = "";
    public const string Tool = "";
    public const string Compress = "";
    public const string Shrink = "";
    public const string Tag = "";
    public const string Mute = "";
    public const string Volume = "";
    public const string Merge = "";
    public const string Split = "";
    public const string Rotate = "";
    public const string Cancel = "";
    public const string Info = "";
    public const string Check = "";
    public const string Error = "";
    public const string OpenFile = "";
    public const string Open = "";
    public const string Clock = "";
}

/// <summary>
/// Значок сектора колеса / строки задачи по операции. Каталог операций — данные без значков,
/// поэтому значок подбирается здесь: по id, затем по расширению результата, затем по типу файла.
/// </summary>
public static class OperationGlyphs
{
    private static readonly string[] ImageExt = ["jpg", "jpeg", "jfif", "png", "webp", "avif", "heic", "heif", "bmp", "tif", "tiff", "gif", "ico"];
    private static readonly string[] VideoExt = ["mp4", "m4v", "webm", "mkv", "mov", "avi"];
    private static readonly string[] AudioExt = ["mp3", "m4a", "wav", "flac", "ogg", "opus", "aac", "wma"];
    private static readonly string[] DocExt = ["pdf", "doc", "docx", "docm", "rtf", "odt", "xls", "xlsx", "xlsm", "ods", "csv", "ppt", "pptx", "pptm", "odp", "epub"];
    private static readonly string[] TextExt = ["txt", "md", "markdown", "html", "htm"];
    private static readonly string[] ArchiveExt = ["zip", "7z", "rar", "tar", "gz"];

    public static string For(string operationId) =>
        OperationCatalog.Find(operationId) is { } op ? For(op) : Glyphs.Convert;

    public static string For(Operation op)
    {
        var id = op.Id.ToLowerInvariant();
        if (op.Mode == OperationMode.Tool)
        {
            if (Has(id, "compress", "optimize", "smaller")) return Glyphs.Compress;
            if (Has(id, "half", "1920", "720", "resize", "scale")) return Glyphs.Shrink;
            if (Has(id, "strip", "meta")) return Glyphs.Tag;
            if (Has(id, "mute")) return Glyphs.Mute;
            if (Has(id, "normalize", "loud", "volume")) return Glyphs.Volume;
            if (Has(id, "split")) return Glyphs.Split;
            if (Has(id, "merge", "join", "combine")) return Glyphs.Merge;
            if (Has(id, "rotate")) return Glyphs.Rotate;
            if (Has(id, "extract", "unpack", "unzip")) return Glyphs.FolderOpen;
            if (Has(id, "pack", "zip", "7z")) return Glyphs.Package;
            if (Has(id, "ocr", "text", "encoding", "utf")) return Glyphs.Text;
            if (op.Inputs == InputMode.Combine && ForExtension(op.Output.Fallback) is { } combined) return combined;
            return Glyphs.Tool;
        }
        return ForExtension(op.Output.Fallback) ?? ForKind(op.Kind) ?? Glyphs.Convert;
    }

    /// <summary>Значок по расширению файла (для строк задач без операции); null — неизвестно.</summary>
    public static string? ForExtension(string? extension)
    {
        if (string.IsNullOrEmpty(extension)) return null;
        var ext = Extensions.Normalize(extension);
        if (ImageExt.Contains(ext)) return Glyphs.Photo;
        if (VideoExt.Contains(ext)) return Glyphs.Video;
        if (AudioExt.Contains(ext)) return Glyphs.Audio;
        if (DocExt.Contains(ext)) return Glyphs.Document;
        if (TextExt.Contains(ext)) return Glyphs.Text;
        if (ArchiveExt.Contains(ext)) return Glyphs.Package;
        return null;
    }

    public static string? ForKind(MediaKind kind) => kind switch
    {
        MediaKind.Image => Glyphs.Photo,
        MediaKind.Video => Glyphs.Video,
        MediaKind.Audio => Glyphs.Audio,
        MediaKind.Pdf or MediaKind.Document => Glyphs.Document,
        MediaKind.Text => Glyphs.Text,
        MediaKind.Archive => Glyphs.Package,
        _ => null,
    };

    private static bool Has(string id, params string[] parts) => parts.Any(id.Contains);
}
