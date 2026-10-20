using System.Text;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;

namespace DropWheel.Tests.Integration;

/// <summary>
/// PDF через настоящие qpdf / pdftoppm (и ImageMagick для «Один PDF») в папке «Мои фото».
/// Исходник — Fixtures/sample-3pages.pdf (3 страницы 100×140), копия «тест 1.pdf».
/// </summary>
public class PdfTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly Converter _converter = new(Tools.Locator, ConversionSettings.Default);

    public void Dispose() => _dir.Dispose();

    private string Pdf(string name = "тест 1.pdf")
    {
        var p = Path.Combine(_dir.Path, name);
        File.Copy(Tools.Fixture("sample-3pages.pdf"), p);
        return p;
    }

    private async Task<string> Run(string opId, params string[] inputs)
    {
        var hashes = inputs.Select(Tools.Hash).ToList();
        var output = await _converter.ConvertAsync(inputs, OperationCatalog.Find(opId)!);
        Assert.Equal(hashes, inputs.Select(Tools.Hash)); // исходники не тронуты
        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path, ".dropwheel-*")); // временных файлов/папок не осталось
        return output;
    }

    private static string[] Names(string folder) =>
        Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    [SkippableFact]
    public async Task Merge_concatenates_pages()
    {
        Skip.IfNot(Tools.HasQpdf, "нет qpdf");
        var a = Pdf();
        var b = Pdf("второй файл.pdf");
        var output = await Run("pdf-merge", a, b, a);
        Assert.Equal(Path.Combine(_dir.Path, "тест 1 (объединено).pdf"), output);
        Assert.Equal(9, Tools.PdfPages(output));
    }

    [SkippableFact]
    public async Task Merge_needs_two_files()
    {
        Skip.IfNot(Tools.HasQpdf, "нет qpdf");
        await Assert.ThrowsAsync<ConversionException>(() =>
            _converter.ConvertAsync([Pdf()], OperationCatalog.Find("pdf-merge")!));
    }

    [SkippableFact]
    public async Task Split_makes_folder_with_one_pdf_per_page()
    {
        Skip.IfNot(Tools.HasQpdf, "нет qpdf");
        var output = await Run("pdf-split", Pdf());
        Assert.Equal(Path.Combine(_dir.Path, "тест 1 (по страницам)"), output);
        Assert.Equal(["стр-1.pdf", "стр-2.pdf", "стр-3.pdf"], Names(output));
        Assert.All(Directory.GetFiles(output), f => Assert.Equal(1, Tools.PdfPages(f)));
    }

    [SkippableFact]
    public async Task Split_twice_gets_numbered_folder()
    {
        Skip.IfNot(Tools.HasQpdf, "нет qpdf");
        var src = Pdf();
        await Run("pdf-split", src);
        Assert.Equal(Path.Combine(_dir.Path, "тест 1 (по страницам) (1)"), await Run("pdf-split", src));
    }

    [SkippableTheory]
    [InlineData("pdf-to-png", "png")]
    [InlineData("pdf-to-jpg", "jpg")]
    public async Task Pages_to_images(string opId, string ext)
    {
        Skip.IfNot(Tools.HasPdftoppm, "нет pdftoppm");
        var output = await Run(opId, Pdf());
        Assert.Equal(Path.Combine(_dir.Path, "тест 1 (страницы)"), output);
        Assert.Equal([$"стр-1.{ext}", $"стр-2.{ext}", $"стр-3.{ext}"], Names(output));
        if (Tools.HasMagick)
            // 100×140 точек при 72 dpi → 150 dpi: 208×292 (pdftoppm округляет вверх).
            Assert.All(Directory.GetFiles(output), f => Assert.Equal((209, 292), Tools.ImageSize(f)));
    }

    [SkippableFact]
    public async Task Optimize_gives_valid_pdf()
    {
        Skip.IfNot(Tools.HasQpdf, "нет qpdf");
        var output = await Run("pdf-optimize", Pdf());
        Assert.Equal(Path.Combine(_dir.Path, "тест 1 (оптим.).pdf"), output);
        Assert.Equal(3, Tools.PdfPages(output));
        Tools.Run(Tools.Qpdf, "--check", output);
    }

    [SkippableFact]
    public async Task Qpdf_warnings_exit_code_3_is_success()
    {
        Skip.IfNot(Tools.HasQpdf, "нет qpdf");
        // Битая ссылка на таблицу xref: qpdf восстанавливает файл, предупреждает и выходит с кодом 3.
        var src = Pdf("битый.pdf");
        var bytes = File.ReadAllBytes(src);
        var tail = Encoding.ASCII.GetBytes("startxref");
        var i = bytes.AsSpan().LastIndexOf(tail);
        File.WriteAllBytes(src, [.. bytes[..i], .. Encoding.ASCII.GetBytes("startxref\n12345\n%%EOF\n")]);

        var log = new List<string>();
        var output = await _converter.ConvertAsync([src], OperationCatalog.Find("pdf-optimize")!, log: log.Add);
        Assert.Equal(3, Tools.PdfPages(output));
        Assert.Contains(log, l => l.Contains("damaged", StringComparison.OrdinalIgnoreCase)); // предупреждения в журнале
    }

    [SkippableTheory]
    [InlineData("pdf-optimize")]
    [InlineData("pdf-split")]
    [InlineData("pdf-to-png")]
    public async Task Not_a_pdf_fails_clearly_without_leftovers(string opId)
    {
        var op = OperationCatalog.Find(opId)!;
        Skip.IfNot(Tools.Locator.IsAvailable(op.Engine), $"нет {op.Engine}");
        var broken = _dir.File("сломанный.pdf", "это не PDF");
        var ex = await Assert.ThrowsAsync<ConversionException>(() => _converter.ConvertAsync(broken, op));
        Assert.Equal("Файл повреждён или это не PDF", ex.Message);
        Assert.Single(Directory.GetFileSystemEntries(_dir.Path)); // только исходник
    }

    [SkippableFact]
    public async Task Images_to_one_pdf()
    {
        Skip.IfNot(Tools.HasMagick && Tools.HasFfmpeg && Tools.HasQpdf, "нет ImageMagick, ffmpeg или qpdf");
        Skip.IfNot(Tools.CanWrite("pdf"), "эта сборка ImageMagick не пишет pdf");
        var a = Tools.MakeImage(Path.Combine(_dir.Path, "тест 1.png"), 320, 240);
        var b = Tools.MakeImage(Path.Combine(_dir.Path, "второе фото.jpg"), 200, 300);
        var c = Path.Combine(_dir.Path, "третье.heic");
        File.Copy(Tools.Fixture("sample.heic"), c);
        var output = await Run("images-to-pdf", a, b, c);
        Assert.Equal(Path.Combine(_dir.Path, "тест 1 (все).pdf"), output);
        Assert.Equal(3, Tools.PdfPages(output));
    }
}
