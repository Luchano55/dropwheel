using DropWheel.Conversions;

namespace DropWheel.Wheel;

/// <summary>
/// Типы файлов, для которых колесо настраивается отдельно: фото, видео, звук, PDF, Word, Excel, PowerPoint,
/// текст, архивы и «любые файлы» (неизвестное расширение или файлы разных типов вместе).
/// Подписи для людей — в интерфейсе; здесь только id (они же ключи в settings.json).
/// </summary>
public static class WheelGroups
{
    public const string Photo = "photo";
    public const string Video = "video";
    public const string Audio = "audio";
    public const string Pdf = "pdf";
    public const string Word = "word";
    public const string Excel = "excel";
    public const string PowerPoint = "powerpoint";
    public const string Text = "text";
    public const string Archive = "archive";
    public const string Any = "any";

    /// <summary>Расширение, у которого точно нет своего типа, — для группы «Любые файлы».</summary>
    private const string UnknownExtension = "dropwheel-any";

    /// <summary>Все типы в порядке показа в настройках.</summary>
    public static IReadOnlyList<string> All { get; } = [Photo, Video, Audio, Pdf, Word, Excel, PowerPoint, Text, Archive, Any];

    /// <summary>Тип по расширению; неизвестное — «Любые файлы».</summary>
    public static string For(string extension)
    {
        var ext = Extensions.Normalize(extension);
        return Extensions.KindOf(ext) switch
        {
            MediaKind.Image => Photo,
            MediaKind.Video => Video,
            MediaKind.Audio => Audio,
            MediaKind.Pdf => Pdf,
            MediaKind.Document when Extensions.Word.Contains(ext) => Word,
            MediaKind.Document when Extensions.Excel.Contains(ext) => Excel,
            MediaKind.Document => PowerPoint,
            MediaKind.Archive => Archive,
            MediaKind.Text => Text,
            _ => Any,
        };
    }

    /// <summary>Тип набора файлов: у всех один тип — он, иначе (или пусто) — «Любые файлы».</summary>
    public static string ForFiles(IEnumerable<string> paths)
    {
        string? group = null;
        foreach (var path in paths)
        {
            var g = For(Path.GetExtension(path));
            if (group is null) group = g;
            else if (group != g) return Any;
        }
        return group ?? Any;
    }

    /// <summary>Расширения типа (для «Любых файлов» — пусто).</summary>
    public static IReadOnlyList<string> ExtensionsOf(string group) => group switch
    {
        Photo => Extensions.Image,
        Video => Extensions.Video,
        Audio => Extensions.Audio,
        Pdf => Extensions.Pdf,
        Word => Extensions.Word,
        Excel => Extensions.Excel,
        PowerPoint => Extensions.PowerPoint,
        Text => Extensions.Text,
        Archive => Extensions.Archive,
        _ => [],
    };

    /// <summary>
    /// Все операции каталога, которые колесо может предложить файлам этого типа (хотя бы одному расширению;
    /// без проверки, установлен ли Office), в порядке каталога. Для окна настроек.
    /// </summary>
    public static IReadOnlyList<Operation> Operations(string group)
    {
        var exts = ExtensionsOf(group);
        if (exts.Count == 0) exts = [UnknownExtension];
        return OperationCatalog.All.Where(o => exts.Any(o.AppliesTo)).ToList();
    }

    /// <summary>
    /// Операция подходит только части расширений типа — каким (CSV у Excel, DOCX/DOCM у Word → HTML).
    /// null — подходит всем.
    /// </summary>
    public static IReadOnlyList<string>? OnlyFor(Operation op, string group)
    {
        // «В тот же формат» (JPG для .jpg) не в счёт — смотрим только на сужение операции по расширениям.
        var exts = ExtensionsOf(group);
        if (exts.Count == 0 || op.InputExtensions is not { } only) return null;
        var fits = exts.Where(only.Contains).ToList();
        return fits.Count == exts.Count || fits.Count == 0 ? null : fits;
    }
}
