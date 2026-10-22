using DropWheel.Conversions;

namespace DropWheel.Ui;

/// <summary>
/// Коды значков Segoe MDL2 Assets / Segoe Fluent Icons (одинаковые в обоих шрифтах).
/// Только давно существующие значки — чтобы на Windows 10 не было пустых квадратов.
/// </summary>
public static class Glyphs
{
    public const string Photo = "\uE91B";
    public const string Video = "\uE714";
    public const string Audio = "\uE8D6";
    public const string Document = "\uE8A5";
    public const string Text = "\uE8D2";
    public const string Package = "\uE7B8";
    public const string FolderOpen = "\uE838";
    public const string Folder = "\uE8B7";
    public const string Convert = "\uE8AB";
    public const string Tool = "\uE90F";
    public const string Compress = "\uE73F";
    public const string Shrink = "\uE71F";
    public const string Tag = "\uE8EC";
    public const string Mute = "\uE74F";
    public const string Volume = "\uE767";
    public const string Merge = "\uE8C8";
    public const string Split = "\uE8C6";
    public const string Rotate = "\uE7AD";
    public const string Cancel = "\uE711";
    public const string Info = "\uE946";
    public const string Check = "\uE73E";
    public const string Error = "\uE783";
    public const string OpenFile = "\uE8E5";
    public const string Open = "\uE8A7";
    public const string Clock = "\uE823";
    public const string Settings = "\uE713";
    public const string Quality = "\uE9E9";
    public const string WheelPage = "\uE8AB";
    public const string Up = "\uE70E";
    public const string Down = "\uE70D";
    public const string Undo = "\uE7A7";
    public const string Trim = "\uE78A";
    public const string Crop = "\uE7A8";
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
            if (Has(id, "trim")) return Glyphs.Trim;
            if (Has(id, "crop")) return Glyphs.Crop;
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
