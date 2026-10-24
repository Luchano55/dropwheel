namespace DropWheel.Conversions;

/// <summary>
/// «Пометить…» (D-ANNOTATE): стрелки, рамки, текст, маркер, размытие и номера поверх картинки. Рисует и сохраняет
/// само окно «Пометки» (WPF, в полном размере картинки) — очередь задач и ImageMagick для результата не нужны.
/// Операция в каталоге — чтобы был лепесток колеса; её «параметр» заполняет окно, поэтому колесо открывает окно
/// (как у «Обрезать…»), папки и командная строка её не берут.
/// </summary>
public static class ImageAnnotate
{
    public const string Id = "img-annotate";
    public const string Key = "shapes";
    public static readonly IReadOnlyList<string> ParameterNames = [Key];

    /// <summary>
    /// Полная картинка для окна: первый кадр, -auto-orient (поворот по EXIF — как у остальных инструментов),
    /// размер «W H» и PNG без уменьшения, 8 бит на канал (WPF открывает его, какой бы ни была исходная — HEIC, RAW, PSD…).
    /// </summary>
    public static IReadOnlyList<string> LoadArgs(string image, string png) =>
        [image + "[0]", "-auto-orient", "-print", "%w %h\n", "-depth", "8", png];

    /// <summary>Больше — не открываем: 100 Мп в памяти — это 400 МБ на картинку и столько же на результат.</summary>
    public const long MaxPixels = 100_000_000;
}
