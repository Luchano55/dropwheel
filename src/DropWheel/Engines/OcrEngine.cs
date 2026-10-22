using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using DropWheel.Conversions;
using DropWheel.Engines;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using WinOcr = Windows.Media.Ocr.OcrEngine;
using DropWheel.Localization;

namespace DropWheel.AppEngines;

/// <summary>
/// «Текст (OCR)»: распознавание текста на картинке встроенным OCR Windows (Windows.Media.Ocr) → .txt.
/// </summary>
/// <remarks>
/// - Вход — только то, что Windows декодирует сама (JPG, PNG, BMP, TIFF); HEIC/WebP/AVIF/RAW без расширений
///   из Store она не открывает — их сначала → PNG.
/// - Язык: русский, если установлен (модель ru-RU читает и латиницу — смешанный текст подходит);
///   если русская модель не нашла ни одной кириллической буквы, а английская есть — текст, видимо, английский,
///   и мы перечитываем его английской моделью (у неё лучше словарь). Нет русского — языки профиля пользователя,
///   иначе первый установленный.
/// - Картинка больше OcrEngine.MaxImageDimension уменьшается (иначе OCR отказывается).
/// - Результат — UTF-8 с BOM, по строке текста на строку файла.
/// </remarks>
public sealed class OcrEngine(Action<string>? log = null) : IInternalEngine
{
    public EngineKind Kind => EngineKind.Ocr;

    private static readonly Lazy<IReadOnlyList<Language>> Languages = new(() =>
    {
        try { return WinOcr.AvailableRecognizerLanguages.ToList(); }
        catch { return []; } // нет компонента OCR (урезанные сборки Windows)
    });

    /// <summary>Установлен хотя бы один язык распознавания (Параметры → Время и язык → Язык).</summary>
    public bool IsAvailable => Languages.Value.Count > 0;

    public bool CanRun(Operation op) => IsAvailable && op.Id == OperationCatalog.OcrId;

    public async Task RunAsync(Operation op, IReadOnlyList<string> inputs, string output, ConversionSettings settings,
        IProgress<double>? progress, CancellationToken ct)
    {
        if (!CanRun(op)) throw new ConversionException(Loc.T.ErrOcrNoLanguage, op.Id);

        using var bitmap = OnWhite(await LoadAsync(inputs[0], ct));
        progress?.Report(0.3);

        var (engine, english) = CreateEngines();
        log?.Invoke($"OCR: {engine.RecognizerLanguage.LanguageTag}, {bitmap.PixelWidth}×{bitmap.PixelHeight}");
        var result = await engine.RecognizeAsync(bitmap).AsTask(ct);
        if (english is not null && !result.Text.Any(IsCyrillic))
        {
            log?.Invoke("OCR: кириллицы нет — перечитываем английской моделью");
            result = await english.RecognizeAsync(bitmap).AsTask(ct);
        }
        progress?.Report(0.9);

        var lines = result.Lines.Select(l => l.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (lines.Count == 0) throw new ConversionException(Loc.T.ErrOcrNoText, inputs[0]);
        await File.WriteAllLinesAsync(output, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct);
    }

    private static bool IsCyrillic(char c) => c is >= 'Ѐ' and <= 'ӿ';

    /// <summary>Основная модель и запасная английская (только если основная — русская).</summary>
    private static (WinOcr Engine, WinOcr? English) CreateEngines()
    {
        var languages = Languages.Value;
        var russian = languages.FirstOrDefault(l => l.LanguageTag.StartsWith("ru", StringComparison.OrdinalIgnoreCase));
        if (russian is not null && WinOcr.TryCreateFromLanguage(russian) is { } ru)
        {
            var en = languages.FirstOrDefault(l => l.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            return (ru, en is null ? null : WinOcr.TryCreateFromLanguage(en));
        }
        var engine = WinOcr.TryCreateFromUserProfileLanguages()
            ?? languages.Select(WinOcr.TryCreateFromLanguage).FirstOrDefault(e => e is not null)
            ?? throw new ConversionException(Loc.T.ErrOcrNoLanguage);
        return (engine, null);
    }

    /// <summary>
    /// Картинка → SoftwareBitmap (BGRA8) с учётом поворота из EXIF, длинная сторона — не больше MaxImageDimension.
    /// </summary>
    internal static async Task<SoftwareBitmap> LoadAsync(string path, CancellationToken ct)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            1 << 16, useAsync: true);
        using var stream = file.AsRandomAccessStream();
        BitmapDecoder decoder;
        try
        {
            decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ConversionException(Loc.T.ErrOcrCantOpen, ex.Message, ex);
        }

        var max = WinOcr.MaxImageDimension;
        uint width = decoder.PixelWidth, height = decoder.PixelHeight;
        if (Math.Max(width, height) <= max)
            return await DecodeAsync(decoder, new BitmapTransform(), ct);

        // Масштаб задаётся в размерах исходного кадра (до поворота по EXIF): порядок BitmapTransform —
        // масштаб, потом поворот. Если Windows поняла размеры как «после поворота» (у повёрнутых на 90° фото
        // стороны меняются местами), картинку растянет — тогда перечитываем с переставленными сторонами.
        var scale = (double)max / Math.Max(width, height);
        uint w = Scaled(width, scale), h = Scaled(height, scale);
        var bitmap = await DecodeAsync(decoder, Transform(w, h), ct);
        var rotated = decoder.OrientedPixelWidth != decoder.PixelWidth;
        if (rotated && bitmap.PixelWidth != Scaled(decoder.OrientedPixelWidth, scale))
        {
            bitmap.Dispose();
            bitmap = await DecodeAsync(decoder, Transform(h, w), ct);
        }
        return bitmap;

        static uint Scaled(uint side, double scale) => Math.Max(1u, (uint)Math.Round(side * scale));
        static BitmapTransform Transform(uint w, uint h) =>
            new() { ScaledWidth = w, ScaledHeight = h, InterpolationMode = BitmapInterpolationMode.Fant };
    }

    /// <summary>
    /// Прозрачное → на белый фон: OCR альфу не учитывает, и чёрный текст на прозрачном PNG оказался бы
    /// на чёрном. Пиксели BGRA8 с premultiplied-альфой: цвет на белом = c + (255 − a).
    /// </summary>
    internal static SoftwareBitmap OnWhite(SoftwareBitmap bitmap)
    {
        var buffer = new global::Windows.Storage.Streams.Buffer((uint)(bitmap.PixelWidth * bitmap.PixelHeight * 4));
        bitmap.CopyToBuffer(buffer);
        var pixels = buffer.ToArray();
        var transparent = false;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            var a = pixels[i];
            if (a == 255) continue;
            transparent = true;
            var add = 255 - a;
            pixels[i - 3] = (byte)Math.Min(255, pixels[i - 3] + add);
            pixels[i - 2] = (byte)Math.Min(255, pixels[i - 2] + add);
            pixels[i - 1] = (byte)Math.Min(255, pixels[i - 1] + add);
            pixels[i] = 255;
        }
        if (!transparent) return bitmap;

        var flat = SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8,
            bitmap.PixelWidth, bitmap.PixelHeight, BitmapAlphaMode.Premultiplied);
        bitmap.Dispose();
        return flat;
    }

    private static async Task<SoftwareBitmap> DecodeAsync(BitmapDecoder decoder, BitmapTransform transform, CancellationToken ct) =>
        await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(ct);
}
