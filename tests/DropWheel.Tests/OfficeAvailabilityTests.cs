using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;

namespace DropWheel.Tests;

/// <summary>
/// Операции Office появляются в колесе, только если есть чем их выполнить: Office (по приложению)
/// или запасной движок. Вместо настоящего Office — подделка.
/// </summary>
public class OfficeAvailabilityTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>Движок-подделка: что умеет — решает функция; «конвертирует», записывая файл (или слайды в папку).</summary>
    private sealed class FakeEngine(EngineKind kind, Func<Operation, string?, bool> can) : IInternalEngine
    {
        public readonly List<IReadOnlyList<Operation>> Prepared = [];
        public readonly List<(string Op, string Input, string Output)> Runs = [];

        public EngineKind Kind => kind;
        public bool IsAvailable => true;
        public bool CanRun(Operation op) => can(op, null);
        public bool CanRun(Operation op, string extension) => can(op, extension);
        public void Prepare(IReadOnlyList<Operation> offered) => Prepared.Add(offered);

        public Task RunAsync(Operation op, IReadOnlyList<string> inputs, string output, ConversionSettings settings,
            IProgress<double>? progress, CancellationToken ct)
        {
            Runs.Add((op.Id, inputs[0], output));
            if (op.Result == ResultKind.Folder)
                for (var i = 1; i <= 2; i++) File.WriteAllText(Path.Combine(output, OfficeFormats.SlideFileName(i, 2)), "png");
            else
                File.WriteAllText(output, kind.ToString());
            return Task.CompletedTask;
        }
    }

    /// <summary>Office, в котором установлены только эти приложения.</summary>
    private static FakeEngine Office(params OfficeApp[] apps) =>
        new(EngineKind.Office, (op, _) => OfficeFormats.For(op) is { } t && apps.Contains(t.App));

    private static Converter ConverterWith(params IInternalEngine[] engines) =>
        new(new ToolLocator { ToolsDirectory = "/нет/такой/папки" }, ConversionSettings.Default, engines);

    private static List<string> Offered(Converter converter, params string[] files) =>
        OperationCatalog.ForFiles(files, converter.CanRun).Select(o => o.Id).ToList();

    [Fact]
    public void Without_office_nothing_from_office_is_offered()
    {
        var converter = ConverterWith(new ZipEngine());
        foreach (var file in new[] { "отчёт.docx", "таблица.xlsx", "доклад.pptx", "данные.csv" })
            Assert.Equal(["zip-pack", "pack-7z"], Offered(converter, file));
        Assert.DoesNotContain("pdf-to-docx", Offered(converter, "скан.pdf"));
    }

    [Fact]
    public void Only_installed_applications_are_offered()
    {
        var converter = ConverterWith(new ZipEngine(), Office(OfficeApp.Word));

        Assert.Contains("word-to-pdf", Offered(converter, "отчёт.docx"));
        Assert.Contains("word-to-txt", Offered(converter, "отчёт.rtf"));
        Assert.Contains("pdf-to-docx", Offered(converter, "скан.pdf"));
        Assert.Equal(["zip-pack", "pack-7z"], Offered(converter, "таблица.xlsx"));
        Assert.Equal(["zip-pack", "pack-7z"], Offered(converter, "доклад.pptx"));
    }

    [Fact]
    public void Fallback_engine_covers_only_its_inputs()
    {
        // Без Office: Edge печатает в PDF только DOCX, библиотеки делают TXT из DOCX.
        var webPdf = new FakeEngine(EngineKind.WebPdf, (op, ext) => op.Id == "word-to-pdf" && ext is null or "docx");
        var docLibs = new FakeEngine(EngineKind.DocLibs, (op, ext) => op.Id == "word-to-txt" && ext is null or "docx");
        var converter = ConverterWith(webPdf, docLibs);

        // Общие для любых файлов архиваторы (В ZIP/В 7Z) здесь не про Office — не учитываем.
        Assert.Equal(["word-to-pdf", "word-to-txt"], Offered(converter, "отчёт.docx").Where(id => id is not ("zip-pack" or "pack-7z")));
        Assert.DoesNotContain(Offered(converter, "старый.doc"), id => id is not ("zip-pack" or "pack-7z"));
        Assert.Equal(EngineKind.WebPdf, converter.EngineFor(OperationCatalog.Find("word-to-pdf")!, "docx"));
        Assert.Null(converter.EngineFor(OperationCatalog.Find("word-to-pdf")!, "doc"));

        // С Office — основной движок Office.
        var withOffice = ConverterWith(webPdf, docLibs, Office(OfficeApp.Word));
        Assert.Equal(EngineKind.Office, withOffice.EngineFor(OperationCatalog.Find("word-to-pdf")!, "docx"));
        Assert.Contains("word-to-pdf", Offered(withOffice, "старый.doc"));
    }

    [Fact]
    public void Prepare_reaches_office_with_office_operations_only()
    {
        var office = Office(OfficeApp.Word, OfficeApp.Excel, OfficeApp.PowerPoint);
        var converter = ConverterWith(new ZipEngine(), office);
        var ops = OperationCatalog.ForFiles(["отчёт.docx"], converter.CanRun);

        converter.Prepare(ops);

        var prepared = Assert.Single(office.Prepared);
        Assert.NotEmpty(prepared);
        Assert.All(prepared, op => Assert.Equal(OfficeApp.Word, OfficeFormats.For(op)!.App));
        Assert.DoesNotContain(prepared, op => op.Id == "zip-pack");
    }

    [Fact]
    public async Task Office_result_lands_next_to_the_source()
    {
        var office = Office(OfficeApp.Word);
        var converter = ConverterWith(office);
        var input = _dir.File("тест 1.docx", "docx");

        var result = await converter.ConvertAsync(input, OperationCatalog.Find("word-to-pdf")!);

        Assert.Equal(Path.Combine(_dir.Path, "тест 1.pdf"), result);
        Assert.Equal("Office", File.ReadAllText(result));
        var run = Assert.Single(office.Runs);
        // Временный файл — в скрытой папке рядом, потом переименован; папка после работы удалена.
        Assert.Equal(Path.Combine(_dir.Path, Converter.TempDirName), Path.GetDirectoryName(run.Output));
        Assert.False(Directory.Exists(Path.Combine(_dir.Path, Converter.TempDirName)));
        Assert.EndsWith(".pdf", run.Output);
        Assert.Equal(["тест 1.docx", "тест 1.pdf"], Directory.GetFiles(_dir.Path).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task Slides_land_in_a_folder()
    {
        var converter = ConverterWith(Office(OfficeApp.PowerPoint));
        var input = _dir.File("доклад.pptx", "pptx");

        var result = await converter.ConvertAsync(input, OperationCatalog.Find("ppt-to-png")!);

        Assert.Equal(Path.Combine(_dir.Path, "доклад (слайды)"), result);
        Assert.Equal(["слайд-1.png", "слайд-2.png"], Directory.GetFiles(result).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task Without_office_the_error_says_so()
    {
        var converter = ConverterWith();
        var input = _dir.File("таблица.xlsx", "xlsx");
        var ex = await Assert.ThrowsAsync<ConversionException>(() =>
            converter.ConvertAsync(input, OperationCatalog.Find("excel-to-pdf")!));
        Assert.Equal("Не найден Microsoft Office", ex.Message);
        Assert.Single(Directory.GetFiles(_dir.Path)); // ничего лишнего не осталось
    }
}
