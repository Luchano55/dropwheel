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
        return null;
    }

    public static bool SameFormat(string a, string b)
    {
        a = Normalize(a);
        b = Normalize(b);
        return a == b || Aliases.Any(g => g.Contains(a) && g.Contains(b));
    }

    public static IEnumerable<string> All => Image.Concat(Video).Concat(Audio);
}
