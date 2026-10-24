using System.Diagnostics;
using System.Globalization;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;
using DropWheel.Localization;

namespace DropWheel.Tests;

/// <summary>
/// LibreOffice вместо Microsoft Office (D-LIBREOFFICE): таблица операций, аргументы soffice, порядок движков
/// (подделки вместо Office, LibreOffice и Edge) и запуск — с поддельным soffice (скрипт), без LibreOffice.
/// </summary>
public class LibreOfficeTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static Operation Op(string id) => OperationCatalog.Find(id)!;

    // ---------- таблица операций ----------

    [Fact]
    public void Mapped_operations_are_office_operations_with_matching_output()
    {
        Assert.Equal(15, LibreOfficeFormats.OperationIds.Count);
        foreach (var id in LibreOfficeFormats.OperationIds)
        {
            var op = Op(id);
            Assert.Equal(EngineKind.Office, op.Engine);
            Assert.NotNull(OfficeFormats.For(op));
            var filter = LibreOfficeFormats.Filter(op, CultureInfo.InvariantCulture);
            Assert.Equal(op.Output.Fallback, LibreOfficeFormats.OutputExtension(filter));
        }
    }

    [Theory]
    [InlineData("ppt-to-png")]  // LibreOffice сохраняет только первый слайд
    [InlineData("pdf-to-docx")] // плохо перестраивает PDF
    [InlineData("pdf-to-md")]
    [InlineData("word-to-md")]  // не операция Office
    public void Not_mapped(string id) => Assert.False(LibreOfficeFormats.Supports(Op(id)));

    [Theory]
    [InlineData("word-to-pdf", "doc", true)]
    [InlineData("word-to-pdf", "RTF", true)]
    [InlineData("word-to-pdf", "odt", true)]
    [InlineData("word-to-docx", "docx", false)] // в тот же формат не предлагается
    [InlineData("word-to-docx", "doc", true)]
    [InlineData("excel-to-pdf", "ods", true)]
    [InlineData("excel-to-pdf", "xls", true)]
    [InlineData("excel-to-pdf", "csv", false)]  // CSV LibreOffice читал бы со своими настройками
    [InlineData("excel-to-csv", "xlsx", true)]
    [InlineData("ppt-to-pdf", "ppt", true)]
    [InlineData("ppt-to-pdf", "odp", true)]
    [InlineData("ppt-to-pdf", "docx", false)]
    public void Supports_inputs(string id, string ext, bool expected) =>
        Assert.Equal(expected, LibreOfficeFormats.Supports(Op(id), ext));

    [Fact]
    public void Csv_filter_uses_utf8_and_list_separator()
    {
        Assert.Equal("csv:Text - txt - csv (StarCalc):59,34,76,1",
            LibreOfficeFormats.Filter(Op("excel-to-csv"), CultureInfo.GetCultureInfo("ru-RU")));
        Assert.Equal("csv:Text - txt - csv (StarCalc):44,34,76,1",
            LibreOfficeFormats.Filter(Op("excel-to-csv"), CultureInfo.GetCultureInfo("en-US")));
        // Первый лист скрыт — CSV из второго (12-й параметр), значения — «как показано» (9-й).
        Assert.Equal("csv:Text - txt - csv (StarCalc):59,34,76,1,,,false,true,true,false,false,2",
            LibreOfficeFormats.Filter(Op("excel-to-csv"), CultureInfo.GetCultureInfo("ru-RU"), sheet: 2));
        Assert.Equal("pdf:writer_pdf_Export", LibreOfficeFormats.Filter(Op("word-to-pdf"), CultureInfo.InvariantCulture));
        Assert.Equal("txt:Text (encoded):UTF8,CRLF", LibreOfficeFormats.Filter(Op("word-to-txt"), CultureInfo.InvariantCulture));
    }

    // ---------- аргументы ----------

    [Theory]
    [InlineData(@"C:\Users\Роман\AppData\Local\Temp\dw lo\profile",
        "file:///C:/Users/%D0%A0%D0%BE%D0%BC%D0%B0%D0%BD/AppData/Local/Temp/dw%20lo/profile")]
    [InlineData("/tmp/Мои фото/p", "file:///tmp/%D0%9C%D0%BE%D0%B8%20%D1%84%D0%BE%D1%82%D0%BE/p")]
    [InlineData(@"\\server\share\a#b%c", "file://server/share/a%23b%25c")]
    public void Profile_url_is_percent_encoded_utf8(string path, string url) =>
        Assert.Equal(url, LibreOfficeFormats.FileUrl(path));

    [Fact]
    public void Arguments_are_separate_items()
    {
        var args = LibreOfficeFormats.Arguments(@"C:\Мои фото\тест 1.docx", @"C:\t\out", @"C:\t\profile", "pdf:writer_pdf_Export");
        Assert.Equal(
        [
            "-env:UserInstallation=file:///C:/t/profile",
            "--headless", "--norestore", "--nologo", "--nodefault", "--nolockcheck",
            "--convert-to", "pdf:writer_pdf_Export",
            "--outdir", @"C:\t\out",
            @"C:\Мои фото\тест 1.docx",
        ], args);
    }

    // ---------- порядок движков ----------

    private sealed class FakeEngine(EngineKind kind, Func<Operation, string?, bool> can) : IInternalEngine
    {
        public readonly List<string> Runs = [];
        public string? FailWith { get; init; }
        public EngineKind Kind => kind;
        public bool IsAvailable => true;
        public bool CanRun(Operation op) => can(op, null);
        public bool CanRun(Operation op, string extension) => can(op, extension);

        public Task RunAsync(Operation op, IReadOnlyList<string> inputs, string output, ConversionSettings settings,
            IProgress<double>? progress, CancellationToken ct)
        {
            Runs.Add(op.Id);
            if (FailWith is not null) throw new ConversionException(FailWith, "сбой");
            File.WriteAllText(output, kind.ToString());
            return Task.CompletedTask;
        }
    }

    private static FakeEngine MsOffice(string? failWith = null) =>
        new(EngineKind.Office, (op, _) => OfficeFormats.For(op) is not null) { FailWith = failWith };

    private static FakeEngine Libre(string? failWith = null) =>
        new(EngineKind.LibreOffice, LibreOfficeFormats.Supports) { FailWith = failWith };

    private static FakeEngine Edge() =>
        new(EngineKind.WebPdf, (op, ext) => op.Id == "word-to-pdf" && ext is null or "docx");

    private static Converter ConverterWith(params IInternalEngine[] engines) =>
        new(new ToolLocator { ToolsDirectory = "/нет/такой/папки" }, ConversionSettings.Default, engines);

    private static List<string> Offered(Converter converter, string file) =>
        OperationCatalog.ForFiles([file], converter.CanRun).Select(o => o.Id)
            .Where(id => id is not ("zip-pack" or "pack-7z")).ToList();

    [Fact]
    public void Without_microsoft_office_libreoffice_operations_are_offered()
    {
        var converter = ConverterWith(Libre());

        Assert.Contains("word-to-pdf", Offered(converter, "старый.doc"));
        Assert.Contains("word-to-docx", Offered(converter, "письмо.rtf"));
        Assert.Contains("word-to-pdf", Offered(converter, "текст.odt"));
        Assert.Contains("excel-to-pdf", Offered(converter, "таблица.xls"));
        Assert.Contains("excel-to-csv", Offered(converter, "таблица.ods"));
        Assert.Contains("ppt-to-pdf", Offered(converter, "доклад.ppt"));
        Assert.DoesNotContain("ppt-to-png", Offered(converter, "доклад.pptx"));
        Assert.DoesNotContain("excel-to-pdf", Offered(converter, "данные.csv"));
        Assert.DoesNotContain("pdf-to-docx", Offered(converter, "скан.pdf"));
    }

    [Fact]
    public void Engine_order_is_office_then_libreoffice_then_fallback()
    {
        var op = Op("word-to-pdf");
        Assert.Equal(EngineKind.Office, ConverterWith(Edge(), Libre(), MsOffice()).EngineFor(op, "docx"));
        Assert.Equal(EngineKind.LibreOffice, ConverterWith(Edge(), Libre()).EngineFor(op, "docx"));
        Assert.Equal(EngineKind.LibreOffice, ConverterWith(Edge(), Libre()).EngineFor(op, "doc"));
        Assert.Equal(EngineKind.WebPdf, ConverterWith(Edge()).EngineFor(op, "docx"));
        // Не операция Office — LibreOffice не участвует.
        Assert.Null(ConverterWith(Libre()).EngineFor(Op("word-to-md"), "docx"));
    }

    [Fact]
    public async Task Broken_office_is_replaced_by_libreoffice()
    {
        var office = MsOffice("Word не смог открыть файл");
        var libre = Libre();
        var edge = Edge();
        var input = _dir.File("тест 1.docx", "docx");

        var output = await ConverterWith(edge, libre, office).ConvertAsync(input, Op("word-to-pdf"));

        Assert.Equal(Path.Combine(_dir.Path, "тест 1.pdf"), output);
        Assert.Equal("LibreOffice", File.ReadAllText(output));
        Assert.Equal((1, 1, 0), (office.Runs.Count, libre.Runs.Count, edge.Runs.Count));
        Assert.Equal("docx", File.ReadAllText(input));
        Assert.Equal(["тест 1.docx", "тест 1.pdf"], Directory.GetFileSystemEntries(_dir.Path).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task Broken_office_and_libreoffice_fall_back_to_edge()
    {
        var office = MsOffice("Word не смог открыть файл");
        var libre = Libre("LibreOffice не смог сконвертировать файл");
        var edge = Edge();
        var input = _dir.File("тест 1.docx", "docx");

        var output = await ConverterWith(edge, libre, office).ConvertAsync(input, Op("word-to-pdf"));

        Assert.Equal("WebPdf", File.ReadAllText(output));
        Assert.Equal((1, 1, 1), (office.Runs.Count, libre.Runs.Count, edge.Runs.Count));
    }

    [Fact]
    public async Task Last_engine_error_is_reported_and_password_is_not_retried()
    {
        var input = _dir.File("старый.doc", "doc");
        // DOC: Edge не умеет — после LibreOffice пробовать нечем, ошибка — его.
        var ex = await Assert.ThrowsAsync<ConversionException>(() =>
            ConverterWith(Edge(), Libre("LibreOffice сломался"), MsOffice("Word сломался")).ConvertAsync(input, Op("word-to-pdf")));
        Assert.Equal("LibreOffice сломался", ex.Message);

        var libre = Libre();
        ex = await Assert.ThrowsAsync<ConversionException>(() =>
            ConverterWith(libre, MsOffice(Loc.T.ErrDocPassword)).ConvertAsync(input, Op("word-to-pdf")));
        Assert.Equal(Loc.T.ErrDocPassword, ex.Message);
        Assert.Empty(libre.Runs);
        Assert.Equal(["старый.doc"], Directory.GetFileSystemEntries(_dir.Path).Select(Path.GetFileName));
    }

    // ---------- запуск: поддельный soffice (скрипт sh) ----------

    private string FakeSoffice(string body)
    {
        var path = Path.Combine(_dir.Path, "soffice");
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    // Пишет аргументы в args.txt и результат «<outdir>/<имя входа>.<расширение фильтра>», как soffice.
    private const string WritesResult = """
        printf '%s\n' "$@" > "$(dirname "$0")/args.txt"
        while [ $# -gt 1 ]; do
          case "$1" in --convert-to) filter="$2";; --outdir) out="$2";; esac
          shift
        done
        name=$(basename "$1"); printf 'a,б' > "$out/${name%.*}.${filter%%:*}"
        """;

    [SkippableFact]
    public async Task Fake_soffice_result_goes_through_the_normal_pipeline()
    {
        Skip.If(OperatingSystem.IsWindows(), "поддельный soffice — скрипт sh");
        var temp = Directory.CreateDirectory(Path.Combine(_dir.Path, "темп")).FullName;
        var engine = new LibreOfficeEngine(FakeSoffice(WritesResult), csvCulture: CultureInfo.GetCultureInfo("ru-RU"), tempDirectory: temp);
        var converter = ConverterWith(engine);
        var input = Path.Combine(_dir.Path, "отчёт 2024.xlsx");
        File.WriteAllText(input, "xlsx");

        var output = await converter.ConvertAsync(input, Op("excel-to-csv"));

        Assert.Equal(Path.Combine(_dir.Path, "отчёт 2024.csv"), output);
        Assert.Equal([0xEF, 0xBB, 0xBF, (byte)'a', (byte)','], File.ReadAllBytes(output)[..5]); // BOM добавлен
        var args = File.ReadAllLines(Path.Combine(_dir.Path, "args.txt"));
        Assert.StartsWith("-env:UserInstallation=file:///", args[0]);
        Assert.Contains("%D1%82%D0%B5%D0%BC%D0%BF/dropwheel-lo-", args[0]); // «темп» — %-кодирован
        Assert.Contains("csv:Text - txt - csv (StarCalc):59,34,76,1", args);
        Assert.Equal(input, args[^1]);
        Assert.Equal("xlsx", File.ReadAllText(input));
        Assert.Empty(Directory.GetFileSystemEntries(temp)); // профиль и папка результата удалены
    }

    [SkippableFact]
    public async Task Fake_soffice_without_result_is_an_error()
    {
        Skip.If(OperatingSystem.IsWindows(), "поддельный soffice — скрипт sh");
        var temp = Directory.CreateDirectory(Path.Combine(_dir.Path, "темп")).FullName;
        // Настоящий soffice так и делает: «Error: source file could not be loaded», код выхода 0.
        var engine = new LibreOfficeEngine(FakeSoffice("echo 'Error: source file could not be loaded'"), tempDirectory: temp);
        var input = _dir.File("битый.doc", "doc");

        var ex = await Assert.ThrowsAsync<ConversionException>(() => ConverterWith(engine).ConvertAsync(input, Op("word-to-pdf")));

        Assert.Equal(Loc.F(Loc.T.ErrAppCantConvert, "LibreOffice"), ex.Message);
        Assert.Empty(Directory.GetFileSystemEntries(temp));
        Assert.Equal(["soffice", "битый.doc", "темп"], Directory.GetFileSystemEntries(_dir.Path).Select(Path.GetFileName).Order());
    }

    [SkippableFact]
    public async Task Hanging_soffice_is_killed_after_timeout()
    {
        Skip.If(OperatingSystem.IsWindows(), "поддельный soffice — скрипт sh");
        var temp = Directory.CreateDirectory(Path.Combine(_dir.Path, "темп")).FullName;
        var engine = new LibreOfficeEngine(FakeSoffice("sleep 60"), timeout: TimeSpan.FromSeconds(1), tempDirectory: temp);
        var input = _dir.File("тест 1.pptx", "pptx");
        var watch = Stopwatch.StartNew();

        var ex = await Assert.ThrowsAsync<ConversionException>(() => ConverterWith(engine).ConvertAsync(input, Op("ppt-to-pdf")));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), watch.Elapsed.ToString());
        Assert.Equal(Loc.F(Loc.T.ErrAppTimeout, "LibreOffice", "0"), ex.Message); // 1 с ≈ «0 мин»
        Assert.Empty(Directory.GetFileSystemEntries(temp));
    }

    [Fact]
    public void Missing_soffice_is_not_available()
    {
        var engine = new LibreOfficeEngine(Path.Combine(_dir.Path, "нет", "soffice"));
        Assert.False(engine.IsAvailable);
        Assert.False(ConverterWith(engine).CanRun(Op("word-to-pdf"), "doc"));
    }
}
