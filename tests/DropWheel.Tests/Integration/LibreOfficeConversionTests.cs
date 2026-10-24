using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;
using DropWheel.Tests.Documents;

namespace DropWheel.Tests.Integration;

/// <summary>
/// Настоящий LibreOffice (soffice --headless), если установлен; нет — тесты пропускаются (как в CI).
/// Файлы — в «Мои фото» с пробелами и кириллицей; исходник не меняется, временного не остаётся.
/// </summary>
public class LibreOfficeConversionTests : IDisposable
{
    private static readonly bool HasSoffice = new LibreOfficeEngine().IsAvailable;

    private readonly TempDir _dir = new();
    private readonly string _temp;
    private readonly Converter _converter;

    public LibreOfficeConversionTests()
    {
        // Временный профиль LibreOffice — в своей папке (с кириллицей), чтобы проверить, что он удалён.
        _temp = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(_dir.Path)!, "временное")).FullName;
        _converter = new Converter(new ToolLocator { ToolsDirectory = "/нет/такой/папки" }, ConversionSettings.Default,
            [new LibreOfficeEngine(csvCulture: CultureInfo.GetCultureInfo("ru-RU"), tempDirectory: _temp)]);
    }

    public void Dispose() => _dir.Dispose();

    private async Task<string> ConvertAsync(string input, string opId)
    {
        var before = SHA256.HashData(File.ReadAllBytes(input));
        var output = await _converter.ConvertAsync(input, OperationCatalog.Find(opId)!);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(input))); // исходник не тронут
        Assert.False(Directory.Exists(Path.Combine(_dir.Path, Converter.TempDirName)));
        Assert.Empty(Directory.GetFileSystemEntries(_temp)); // профиль и папка результата удалены
        return output;
    }

    private static void AssertPdf(string path) =>
        Assert.Equal("%PDF", Encoding.ASCII.GetString(File.ReadAllBytes(path)[..4]));

    [SkippableFact]
    public async Task Docx_to_pdf_and_doc_to_pdf()
    {
        Skip.IfNot(HasSoffice, "LibreOffice не установлен");
        var docx = Path.Combine(_dir.Path, "тест 1.docx");
        TestDocs.Docx(docx);

        var pdf = await ConvertAsync(docx, "word-to-pdf");
        Assert.Equal(Path.Combine(_dir.Path, "тест 1.pdf"), pdf);
        AssertPdf(pdf);

        // DOC — то, ради чего LibreOffice: без Office его больше никто не читает.
        var doc = await ConvertAsync(docx, "word-to-doc");
        Assert.Equal(Path.Combine(_dir.Path, "тест 1.doc"), doc);
        var pdf2 = await ConvertAsync(doc, "word-to-pdf");
        Assert.Equal(Path.Combine(_dir.Path, "тест 1 (1).pdf"), pdf2);
        AssertPdf(pdf2);

        var txt = await ConvertAsync(doc, "word-to-txt");
        var bytes = File.ReadAllBytes(txt);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Contains("\r\n", Encoding.UTF8.GetString(bytes));
    }

    [SkippableFact]
    public async Task Xlsx_to_csv_is_utf8_with_russian_separator()
    {
        Skip.IfNot(HasSoffice, "LibreOffice не установлен");
        var xlsx = Path.Combine(_dir.Path, "таблица 1.xlsx");
        TestDocs.Xlsx(xlsx);

        var csv = await ConvertAsync(xlsx, "excel-to-csv");

        Assert.Equal(Path.Combine(_dir.Path, "таблица 1.csv"), csv);
        var bytes = File.ReadAllBytes(csv);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes[3..]);
        Assert.Contains("Имя;Сумма;Дата;Доля;Деньги", text);
        Assert.Contains("\"Анна; \"\"Ш\"\"", text);
    }

    [SkippableFact]
    public async Task Presentation_to_pptx_and_pdf()
    {
        Skip.IfNot(HasSoffice, "LibreOffice не установлен");
        var odp = Path.Combine(_dir.Path, "доклад 1.odp");
        MinimalOdp(odp, "Привет, слайд");

        var pptx = await ConvertAsync(odp, "ppt-to-pptx");
        Assert.Equal(Path.Combine(_dir.Path, "доклад 1.pptx"), pptx);
        Assert.Equal("PK", Encoding.ASCII.GetString(File.ReadAllBytes(pptx)[..2]));

        var pdf = await ConvertAsync(pptx, "ppt-to-pdf");
        Assert.Equal(Path.Combine(_dir.Path, "доклад 1.pdf"), pdf);
        AssertPdf(pdf);
    }

    /// <summary>Минимальная презентация ODP: ZIP с mimetype (первым, без сжатия), content.xml и манифестом.</summary>
    private static void MinimalOdp(string path, string text)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, string content, CompressionLevel level = CompressionLevel.Optimal)
        {
            using var w = new StreamWriter(zip.CreateEntry(name, level).Open(), new UTF8Encoding(false));
            w.Write(content);
        }
        Add("mimetype", "application/vnd.oasis.opendocument.presentation", CompressionLevel.NoCompression);
        Add("content.xml", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
              xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"
              xmlns:svg="urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0" office:version="1.3">
              <office:body><office:presentation>
                <draw:page draw:name="s1"><draw:frame svg:x="2cm" svg:y="2cm" svg:width="20cm" svg:height="3cm">
                  <draw:text-box><text:p>{text}</text:p></draw:text-box></draw:frame></draw:page>
              </office:presentation></office:body>
            </office:document-content>
            """);
        Add("META-INF/manifest.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <manifest:manifest xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0" manifest:version="1.3">
              <manifest:file-entry manifest:full-path="/" manifest:media-type="application/vnd.oasis.opendocument.presentation"/>
              <manifest:file-entry manifest:full-path="content.xml" manifest:media-type="text/xml"/>
            </manifest:manifest>
            """);
    }
}
