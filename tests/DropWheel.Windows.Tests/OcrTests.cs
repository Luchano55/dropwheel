using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DropWheel.AppEngines;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;
using WinOcr = global::Windows.Media.Ocr.OcrEngine;

namespace DropWheel.Windows.Tests;

/// <summary>
/// Распознавание текста (Windows.Media.Ocr) на картинках, нарисованных WPF. Без установленного языка OCR
/// (бывает на Windows Server) тесты пропускаются.
/// </summary>
public class OcrTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "dropwheel-тесты", Guid.NewGuid().ToString("N")[..8], "Мои сканы");
    private readonly Converter _converter;
    private static readonly Operation Ocr = OperationCatalog.Find(OperationCatalog.OcrId)!;

    public OcrTests()
    {
        Directory.CreateDirectory(_dir);
        _converter = new Converter(new ToolLocator(), ConversionSettings.Default, [new OcrEngine()]);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_dir)!, recursive: true); } catch { }
    }

    private static void SkipWithoutOcr() =>
        Skip.IfNot(new OcrEngine().IsAvailable, "в Windows не установлен ни один язык распознавания текста");

    /// <summary>Чёрный текст (Segoe UI) построчно на белом или прозрачном фоне → PNG.</summary>
    private string Render(string name, string[] lines, int width, int height, double fontSize, bool transparent = false)
    {
        var path = Path.Combine(_dir, name);
        Sta.Run(() =>
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                if (!transparent) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                var y = fontSize;
                foreach (var line in lines)
                {
                    var text = new FormattedText(line, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), fontSize, Brushes.Black, 1.0);
                    dc.DrawText(text, new Point(fontSize, y));
                    y += fontSize * 1.8;
                }
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(path);
            encoder.Save(file);
        });
        return path;
    }

    private async Task<string[]> Recognize(string image)
    {
        var output = await _converter.ConvertAsync(image, Ocr);
        Assert.Equal(Path.ChangeExtension(image, ".txt"), output);
        var bytes = File.ReadAllBytes(output);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3)); // UTF-8 с BOM
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
    }

    private static int LineWith(string[] lines, string text) =>
        Array.FindIndex(lines, l => l.Contains(text, StringComparison.OrdinalIgnoreCase));

    [SkippableFact]
    public async Task Text_lines_are_recognized_in_order()
    {
        SkipWithoutOcr();
        var image = Render("скан 1.png", ["Hello DropWheel", "Second line 12345"], 900, 300, 48);

        var lines = await Recognize(image);

        var hello = LineWith(lines, "Hello");
        var digits = LineWith(lines, "12345");
        Assert.True(hello >= 0 && digits > hello, string.Join(" | ", lines));
    }

    [SkippableFact]
    public async Task Russian_text_is_recognized_when_language_installed()
    {
        SkipWithoutOcr();
        Skip.IfNot(WinOcr.AvailableRecognizerLanguages.Any(l => l.LanguageTag.StartsWith("ru", StringComparison.OrdinalIgnoreCase)),
            "русский язык OCR не установлен");
        var image = Render("скан 1.png", ["Привет, мир", "Проверка 2026"], 900, 300, 48);

        var lines = await Recognize(image);

        Assert.True(LineWith(lines, "Привет") >= 0, string.Join(" | ", lines));
        Assert.True(LineWith(lines, "2026") > LineWith(lines, "Привет"), string.Join(" | ", lines));
    }

    [SkippableFact]
    public async Task Black_text_on_transparent_background_is_found()
    {
        SkipWithoutOcr();
        var image = Render("прозрачный.png", ["Transparent 67890"], 900, 200, 48, transparent: true);

        var lines = await Recognize(image);

        Assert.True(LineWith(lines, "67890") >= 0, string.Join(" | ", lines));
    }

    [SkippableFact]
    public async Task Image_larger_than_ocr_limit_is_scaled_down()
    {
        SkipWithoutOcr();
        var height = (int)WinOcr.MaxImageDimension + 1000;
        var image = Render("длинный скан.png", ["Large 24680"], 1500, height, 120);

        var lines = await Recognize(image);

        Assert.True(LineWith(lines, "24680") >= 0, string.Join(" | ", lines));
    }

    [SkippableFact]
    public async Task Blank_image_says_no_text()
    {
        SkipWithoutOcr();
        var image = Render("пусто.png", [], 400, 300, 48);

        var ex = await Assert.ThrowsAsync<ConversionException>(() => _converter.ConvertAsync(image, Ocr));

        Assert.Equal("Текст не найден", ex.Message);
        Assert.Single(Directory.GetFiles(_dir)); // только картинка
    }

    [Fact]
    public void Engine_offers_only_ocr()
    {
        var engine = new OcrEngine();
        Assert.Equal(EngineKind.Ocr, engine.Kind);
        Assert.False(engine.CanRun(OperationCatalog.Find("to-jpg")!));
        Assert.Equal(engine.IsAvailable, engine.CanRun(Ocr));
    }
}
