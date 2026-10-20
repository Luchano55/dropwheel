using System.Text;
using System.Text.RegularExpressions;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;

namespace DropWheel.Tests.Integration;

/// <summary>
/// PDF → TXT (pdftotext) и «Повернуть» (qpdf). Fixtures/sample-3pages.pdf — без текста (как скан);
/// PDF с текстом (латиница и кириллица) создаёт TextPdf.
/// </summary>
public class PdfExtrasTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly Converter _converter = new(Tools.Locator, ConversionSettings.Default);

    public void Dispose() => _dir.Dispose();

    private static bool HasPdftotext => Tools.Locator.IsAvailable(EngineKind.Pdftotext);

    private string FixturePdf(string name = "тест 1.pdf")
    {
        var p = Path.Combine(_dir.Path, name);
        File.Copy(Tools.Fixture("sample-3pages.pdf"), p);
        return p;
    }

    private async Task<string> Run(string opId, string input)
    {
        var hash = Tools.Hash(input);
        var output = await _converter.ConvertAsync([input], OperationCatalog.Find(opId)!);
        Assert.Equal(hash, Tools.Hash(input)); // исходник не тронут
        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path, ".dropwheel-*"));
        return output;
    }

    [SkippableFact]
    public async Task Pdf_to_txt_keeps_cyrillic_in_utf8()
    {
        Skip.IfNot(HasPdftotext, "нет pdftotext");
        var pdf = TextPdf.Write(Path.Combine(_dir.Path, "тест 1.pdf"), ["Hello DropWheel 2024", "Привет, мир! Ёлка"]);

        var output = await Run("pdf-to-txt", pdf);

        Assert.Equal(Path.Combine(_dir.Path, "тест 1.txt"), output);
        var text = File.ReadAllText(output, new UTF8Encoding(false, throwOnInvalidBytes: true));
        Assert.Contains("Hello DropWheel 2024", text);
        Assert.Contains("Привет, мир! Ёлка", text);
    }

    [SkippableFact]
    public async Task Scanned_pdf_without_text_fails_clearly_without_leftovers()
    {
        Skip.IfNot(HasPdftotext, "нет pdftotext");
        var pdf = FixturePdf(); // три страницы с картинками, текста нет: pdftotext пишет только «\f\f\f»

        var ex = await Assert.ThrowsAsync<ConversionException>(() =>
            _converter.ConvertAsync([pdf], OperationCatalog.Find("pdf-to-txt")!));

        Assert.Equal("В PDF нет текстового слоя (это скан)", ex.Message);
        Assert.Equal([pdf], Directory.GetFileSystemEntries(_dir.Path));
    }

    [SkippableFact]
    public async Task Pdf_to_txt_of_not_a_pdf_fails_clearly()
    {
        Skip.IfNot(HasPdftotext, "нет pdftotext");
        var broken = _dir.File("сломанный.pdf", "это не PDF");
        var ex = await Assert.ThrowsAsync<ConversionException>(() =>
            _converter.ConvertAsync([broken], OperationCatalog.Find("pdf-to-txt")!));
        Assert.Equal("Файл повреждён или это не PDF", ex.Message);
        Assert.Equal([broken], Directory.GetFileSystemEntries(_dir.Path));
    }

    /// <summary>Значения /Rotate всех объектов PDF (по JSON от qpdf).</summary>
    private static int[] Rotations(string pdf) =>
        Regex.Matches(Tools.Run(Tools.Qpdf, "--json", pdf), "\"/Rotate\":\\s*(-?\\d+)")
            .Select(m => int.Parse(m.Groups[1].Value)).ToArray();

    [SkippableFact]
    public async Task Rotate_turns_every_page_clockwise()
    {
        Skip.IfNot(Tools.HasQpdf, "нет qpdf");
        var pdf = FixturePdf();
        Assert.Empty(Rotations(pdf));

        var output = await Run("pdf-rotate", pdf);

        Assert.Equal(Path.Combine(_dir.Path, "тест 1 (повёрнуто).pdf"), output);
        Assert.Equal(3, Tools.PdfPages(output));
        Assert.Equal([90, 90, 90], Rotations(output));

        // Ещё раз — к уже повёрнутому добавляется 90°.
        var twice = await Run("pdf-rotate", output);
        Assert.Equal([180, 180, 180], Rotations(twice));
    }
}

/// <summary>
/// Минимальный PDF с настоящим текстовым слоем: шрифт Helvetica (не встраивается),
/// однобайтовые коды и таблица ToUnicode — по ней pdftotext восстанавливает и кириллицу.
/// </summary>
internal static class TextPdf
{
    public static string Write(string path, IReadOnlyList<string> lines)
    {
        // Коды: ASCII — как есть, остальные символы — 0x80, 0x81… по порядку появления.
        var codes = new Dictionary<char, byte>();
        foreach (var c in lines.SelectMany(l => l))
            if (c is < ' ' or > '~' && !codes.ContainsKey(c))
                codes[c] = checked((byte)(0x80 + codes.Count));
        string Hex(string line) => string.Concat(line.Select(c => (c is >= ' ' and <= '~' ? (byte)c : codes[c]).ToString("X2")));

        var content = new StringBuilder("BT /F1 14 Tf 40 760 Td\n");
        foreach (var line in lines) content.Append($"<{Hex(line)}> Tj 0 -24 Td\n");
        content.Append("ET");

        var cmap = new StringBuilder("""
            /CIDInit /ProcSet findresource begin
            12 dict begin
            begincmap
            /CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def
            /CMapName /Adobe-Identity-UCS def
            /CMapType 2 def
            1 begincodespacerange
            <00> <FF>
            endcodespacerange
            1 beginbfrange
            <20> <7E> <0020>
            endbfrange

            """);
        if (codes.Count > 0)
        {
            cmap.Append($"{codes.Count} beginbfchar\n");
            foreach (var (c, code) in codes) cmap.Append($"<{code:X2}> <{(int)c:X4}>\n");
            cmap.Append("endbfchar\n");
        }
        cmap.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend");

        string Stream(string data) => $"<< /Length {Encoding.ASCII.GetByteCount(data)} >>\nstream\n{data}\nendstream";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            // Ширины — явно: у кодов 0x80+ в стандартной кодировке Helvetica глифов нет (ширина была бы 0).
            $"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /FirstChar 32 /LastChar 255 /Widths [{string.Join(' ', Enumerable.Repeat(600, 224))}] /ToUnicode 6 0 R >>",
            Stream(content.ToString()),
            Stream(cmap.ToString()),
        ];

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length); // всё — ASCII: символ = байт
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");

        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(pdf.ToString()));
        return path;
    }
}
