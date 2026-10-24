using System.Globalization;
using DropWheel.Conversions;

namespace DropWheel.Engines;

/// <summary>
/// Для окна «Обрезать…» у картинок (D-CROP): размер после -auto-orient и уменьшенная копия для показа — тем же
/// ImageMagick, что и обрезает. Так показ совпадает с результатом по повороту (EXIF), и открываются все форматы,
/// которые конвертирует программа (HEIC, AVIF, PSD, RAW…), а не только те, что умеет WPF.
/// </summary>
public static class ImageProbe
{
    /// <summary>Сторона копии для показа: не больше 1600 px (меньшие картинки не увеличиваются).</summary>
    public const int PreviewSide = 1600;

    /// <summary>
    /// Аргументы: первый кадр, -auto-orient, напечатать размер «W H» (уже повёрнутой, полной картинки),
    /// уменьшить и сохранить PNG (прозрачность сохраняется).
    /// </summary>
    public static IReadOnlyList<string> PreviewArgs(string image, string png, int side = PreviewSide) =>
    [
        image + "[0]", "-auto-orient", "-print", "%w %h\n",
        "-resize", $"{side}x{side}>", png,
    ];

    /// <summary>Только размер после -auto-orient (CLI: подрезать --crop по картинке).</summary>
    public static IReadOnlyList<string> SizeArgs(string image) =>
        [image + "[0]", "-auto-orient", "-format", "%w %h", "info:"];

    /// <summary>
    /// Копия для показа → png (временный файл, его удаляет вызывающий) и размер полной картинки после поворота.
    /// null — ImageMagick картинку не открыл. side — сторона копии («Оформить…» берёт поменьше, D-BEAUTIFY).
    /// </summary>
    public static async Task<(int W, int H)?> PreviewAsync(ToolLocator tools, string image, string png, CancellationToken ct = default,
        int side = PreviewSide)
    {
        var (code, stdout, _) = await VideoProbe.RunProcessAsync(tools.Resolve(EngineKind.ImageMagick), PreviewArgs(image, png, side), ct);
        if (code != 0 || !File.Exists(png) || new FileInfo(png).Length == 0) return null;
        return ParseSize(stdout);
    }

    /// <summary>Размер картинки после -auto-orient; null — не открылась.</summary>
    public static async Task<(int W, int H)?> SizeAsync(ToolLocator tools, string image, CancellationToken ct = default)
    {
        var (code, stdout, _) = await VideoProbe.RunProcessAsync(tools.Resolve(EngineKind.ImageMagick), SizeArgs(image), ct);
        return code == 0 ? ParseSize(stdout) : null;
    }

    /// <summary>
    /// Запустить ImageMagick с готовыми аргументами (предпросмотр «Оформить…»: тот же шаблон, что у задачи,
    /// на уменьшенной копии). true — результат записан. Отмена — процесс убивается.
    /// </summary>
    public static async Task<bool> RenderAsync(ToolLocator tools, IReadOnlyList<string> args, string output, CancellationToken ct = default)
    {
        var (code, _, _) = await VideoProbe.RunProcessAsync(tools.Resolve(EngineKind.ImageMagick), args, ct);
        return code == 0 && File.Exists(output) && new FileInfo(output).Length > 0;
    }

    /// <summary>
    /// Размер PNG по заголовку (IHDR: ширина и высота — байты 16…23, big-endian), без декодирования.
    /// Копия для «Оформить…»: размер копии нужен точно — по нему считаются маска и холст. null — не PNG.
    /// </summary>
    public static (int W, int H)? PngSize(ReadOnlySpan<byte> head)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (head.Length < 24 || !head[..8].SequenceEqual(signature) || !head[12..16].SequenceEqual("IHDR"u8)) return null;
        var w = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head[16..20]);
        var h = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head[20..24]);
        return w > 0 && h > 0 ? (w, h) : null;
    }

    /// <summary>«4032 3024» (первая непустая строка) → (4032, 3024); иначе null.</summary>
    public static (int W, int H)? ParseSize(string? text)
    {
        var line = text?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        var parts = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts is not { Length: 2 }
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var w)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var h)
            || w < 1 || h < 1) return null;
        return (w, h);
    }
}
