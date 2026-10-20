using System.Text;
using DropWheel.Conversions;
using DropWheel.Engines;

namespace DropWheel.Tests;

/// <summary>Блок OFFICE каталога и таблица OfficeFormats (без COM — работает на Linux).</summary>
public class OfficeCatalogTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static List<string> Ids(string file) => OperationCatalog.ForFile(file).Select(o => o.Id).ToList();

    private static IEnumerable<Operation> OfficeOps => OperationCatalog.All.Where(o => o.Engine == EngineKind.Office);

    [Fact]
    public void Every_office_operation_has_a_target_and_every_target_an_operation()
    {
        Assert.Equal(OfficeOps.Select(o => o.Id).Order(), OfficeFormats.OperationIds.Order());
        Assert.All(OfficeOps, op => Assert.NotNull(OfficeFormats.For(op)));
        Assert.Null(OfficeFormats.For(OperationCatalog.Find("to-jpg")!));
    }

    [Fact]
    public void Target_application_opens_exactly_the_operation_inputs()
    {
        foreach (var op in OfficeOps)
        {
            var app = OfficeFormats.For(op)!.App;
            IReadOnlyList<string> opens = app switch
            {
                OfficeApp.Word => [.. Extensions.Word, "pdf"],
                OfficeApp.Excel => Extensions.Excel,
                _ => Extensions.PowerPoint,
            };
            var inputs = Extensions.All.Where(op.AppliesTo).ToList();
            Assert.NotEmpty(inputs);
            Assert.All(inputs, ext => Assert.Contains(ext, opens));
        }
    }

    [Theory]
    [InlineData("word-to-pdf", "PDF", "pdf", EngineKind.WebPdf)]
    [InlineData("word-to-docx", "DOCX", "docx", null)]
    [InlineData("word-to-doc", "DOC", "doc", null)]
    [InlineData("word-to-odt", "ODT", "odt", null)]
    [InlineData("word-to-rtf", "RTF", "rtf", null)]
    [InlineData("word-to-txt", "TXT", "txt", EngineKind.DocLibs)]
    [InlineData("excel-to-pdf", "PDF", "pdf", null)]
    [InlineData("excel-to-xlsx", "XLSX", "xlsx", null)]
    [InlineData("excel-to-xls", "XLS", "xls", null)]
    [InlineData("excel-to-csv", "CSV", "csv", EngineKind.DocLibs)]
    [InlineData("excel-to-ods", "ODS", "ods", null)]
    [InlineData("ppt-to-pdf", "PDF", "pdf", null)]
    [InlineData("ppt-to-pptx", "PPTX", "pptx", null)]
    [InlineData("ppt-to-ppt", "PPT", "ppt", null)]
    [InlineData("ppt-to-odp", "ODP", "odp", null)]
    [InlineData("pdf-to-docx", "DOCX", "docx", null)]
    public void Office_file_operations_shape(string id, string label, string ext, EngineKind? fallback)
    {
        var op = OperationCatalog.Find(id)!;
        Assert.Equal((label, OperationMode.Convert, EngineKind.Office, ResultKind.File, InputMode.PerFile, ""),
            (op.Label, op.Mode, op.Engine, op.Result, op.Inputs, op.Suffix));
        Assert.Equal(ext, op.Output.Resolve(".anything"));
        Assert.Equal(fallback, op.Fallback);
        Assert.True(op.Label.Length <= 16, op.Label);
    }

    [Fact]
    public void Slides_go_to_a_folder()
    {
        var op = OperationCatalog.Find("ppt-to-png")!;
        Assert.Equal(("Слайды PNG", ResultKind.Folder, " (слайды)", MediaKind.Document),
            (op.Label, op.Result, op.Suffix, op.Kind));
        Assert.Equal(OfficeAction.SlidesToPng, OfficeFormats.For(op)!.Action);
        Assert.True(op.Label.Length <= 16);
    }

    [Theory]
    [InlineData("отчёт.docx", "word-to-docx")]
    [InlineData("отчёт.DOC", "word-to-doc")]
    [InlineData("отчёт.odt", "word-to-odt")]
    [InlineData("отчёт.rtf", "word-to-rtf")]
    [InlineData("таблица.xlsx", "excel-to-xlsx")]
    [InlineData("таблица.xls", "excel-to-xls")]
    [InlineData("таблица.csv", "excel-to-csv")]
    [InlineData("таблица.ods", "excel-to-ods")]
    [InlineData("доклад.pptx", "ppt-to-pptx")]
    [InlineData("доклад.ppt", "ppt-to-ppt")]
    [InlineData("доклад.odp", "ppt-to-odp")]
    public void Same_format_is_not_offered(string file, string hidden)
    {
        var ids = Ids(file);
        Assert.DoesNotContain(hidden, ids);
        Assert.Contains(ids, id => id.EndsWith("-to-pdf")); // остальное на месте
    }

    [Theory]
    [InlineData("макрос.docm", "word-to-docx")]
    [InlineData("макрос.xlsm", "excel-to-xlsx")]
    [InlineData("макрос.pptm", "ppt-to-pptx")]
    [InlineData("данные.csv", "excel-to-xlsx")]
    [InlineData("данные.csv", "excel-to-pdf")]
    [InlineData("скан.pdf", "pdf-to-docx")]
    [InlineData("доклад.pptx", "ppt-to-png")]
    public void Offered(string file, string op) => Assert.Contains(op, Ids(file));

    [Fact]
    public void Applications_do_not_mix()
    {
        Assert.DoesNotContain(Ids("таблица.xlsx"), id => id.StartsWith("word-") || id.StartsWith("ppt-"));
        Assert.DoesNotContain(Ids("доклад.pptx"), id => id.StartsWith("word-") || id.StartsWith("excel-"));
        Assert.DoesNotContain(Ids("отчёт.docx"), id => id.StartsWith("excel-") || id.StartsWith("ppt-") || id == "pdf-to-docx");
        Assert.DoesNotContain(Ids("readme.txt"), id => id.StartsWith("word-"));
        Assert.DoesNotContain(Ids("скан.pdf"), id => id.StartsWith("word-"));
        // Документы разных приложений вместе — только общее (архивы).
        Assert.Equal(["zip-pack", "pack-7z"], OperationCatalog.ForFiles(["a.docx", "b.xlsx"]).Select(o => o.Id));
    }

    [Theory]
    [InlineData(1, 1, "слайд-1.png")]
    [InlineData(9, 9, "слайд-9.png")]
    [InlineData(1, 12, "слайд-01.png")]
    [InlineData(12, 12, "слайд-12.png")]
    [InlineData(5, 100, "слайд-005.png")]
    public void Slide_file_names_sort_by_name(int number, int count, string expected) =>
        Assert.Equal(expected, OfficeFormats.SlideFileName(number, count));

    // --- Документ с паролем ---

    private static readonly byte[] Cfb = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private string Write(string name, params byte[][] parts)
    {
        var path = Path.Combine(_dir.Path, name);
        File.WriteAllBytes(path, parts.SelectMany(p => p).ToArray());
        return path;
    }

    private static byte[] Zeros(int count) => new byte[count];
    private static byte[] Utf16(string s) => Encoding.Unicode.GetBytes(s);

    [Theory]
    [InlineData("тест 1.docx")]
    [InlineData("тест 1.XLSX")]
    [InlineData("тест 1.pptx")]
    public void Encrypted_open_xml_is_password_protected(string name) =>
        Assert.True(OfficeFormats.IsPasswordProtected(Write(name, Cfb, Zeros(500), Utf16("EncryptedPackage"), Zeros(100))));

    [Fact]
    public void Marker_across_read_chunks_is_found()
    {
        // Образец разрезан границей куска чтения (64 КБ).
        var path = Write("большой.pptx", Cfb, Zeros((1 << 16) - Cfb.Length - 7), Utf16("EncryptedPackage"), Zeros(10));
        Assert.True(OfficeFormats.IsPasswordProtected(path));
    }

    [Fact]
    public void Ordinary_files_are_not_password_protected()
    {
        Assert.False(OfficeFormats.IsPasswordProtected(Write("обычный.docx", "PK\u0003\u0004"u8.ToArray(), Zeros(100), Utf16("EncryptedPackage"))));
        // Старый DOC, переименованный в DOCX: составной файл, но без зашифрованного пакета.
        Assert.False(OfficeFormats.IsPasswordProtected(Write("старый.docx", Cfb, Zeros(2000), Utf16("WordDocument"))));
        // Старые форматы так не проверяем.
        Assert.False(OfficeFormats.IsPasswordProtected(Write("старый.doc", Cfb, Zeros(100), Utf16("EncryptedPackage"))));
        Assert.False(OfficeFormats.IsPasswordProtected(Write("пустой.docx")));
        Assert.False(OfficeFormats.IsPasswordProtected(Path.Combine(_dir.Path, "нет такого.docx")));
    }
}
