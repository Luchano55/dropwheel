using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DropWheel.Conversions;
using DropWheel.Documents;
using DropWheel.Engines;
using DropWheel.Jobs;

namespace DropWheel.Tests.Documents;

/// <summary>
/// Документы без Office (DocLibsEngine): Word → TXT/Markdown/HTML, Excel → CSV, CSV → XLSX, Markdown → HTML.
/// Конвертер без движка Office — значит, операции Office выполняются запасным движком.
/// </summary>
public class DocLibsTests : IDisposable
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private readonly TempDir _dir = new();
    private readonly Converter _converter = new(new ToolLocator(), ConversionSettings.Default, [new DocLibsEngine(Ru)]);

    public void Dispose() => _dir.Dispose();

    // Операции Office: без Office их выполняет DocLibs (запасной движок).
    private static readonly Operation WordToTxt = OperationCatalog.Find(DocLibsEngine.WordToTxtId)!;
    private static readonly Operation ExcelToCsv = OperationCatalog.Find(DocLibsEngine.ExcelToCsvId)!;
    private static Operation Op(string id) => OperationCatalog.Find(id)!;

    private string Docx(string name = "отчёт 1.docx", bool landscape = false)
    {
        var path = Path.Combine(_dir.Path, name);
        TestDocs.Docx(path, landscape);
        return path;
    }

    private void AssertNoTempLeft() => Assert.Empty(Directory.GetFileSystemEntries(_dir.Path, ".dropwheel-*"));

    // ---------- Доступность ----------

    [Theory]
    [InlineData("docx", true)]
    [InlineData("docm", true)]
    [InlineData("doc", false)]
    [InlineData("rtf", false)]
    [InlineData("odt", false)]
    public void Word_to_txt_without_office_only_for_docx(string extension, bool expected)
    {
        Assert.Equal(expected, _converter.CanRun(WordToTxt, extension));
        Assert.Equal(expected ? EngineKind.DocLibs : null, _converter.EngineFor(WordToTxt, extension));
    }

    [Theory]
    [InlineData("xlsx", true)]
    [InlineData("xlsm", true)]
    [InlineData("xls", true)]
    [InlineData("ods", false)]
    [InlineData("csv", false)]
    public void Excel_to_csv_without_office(string extension, bool expected) =>
        Assert.Equal(expected, _converter.CanRun(ExcelToCsv, extension));

    [Fact]
    public void Engine_answers_only_for_its_operations()
    {
        var engine = new DocLibsEngine();
        Assert.True(engine.IsAvailable);
        Assert.False(engine.CanRun(Op("to-jpg")));
        Assert.False(engine.CanRun(Op("text-to-pdf"), "md")); // это Edge
        Assert.True(engine.CanRun(Op("md-to-html"), "markdown"));
        Assert.True(engine.CanRun(Op("csv-to-xlsx"), "csv"));
    }

    // ---------- Word ----------

    [Fact]
    public async Task Word_to_txt()
    {
        var src = Docx();

        var result = await _converter.ConvertAsync(src, WordToTxt);

        Assert.Equal(Path.Combine(_dir.Path, "отчёт 1.txt"), result);
        var bytes = File.ReadAllBytes(result);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]); // UTF-8 с BOM — Блокнот не ошибётся
        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n");
        Assert.Equal(
        [
            "Отчёт о работе",
            "Привет, жирный и курсив текст, важное",
            "Списки",
            "• яблоко",
            "• груша",
            "1. первый",
            "    1.1. вложенный",
            "2. второй",
            "Ссылка: сайт",
            "",
            "до разрыва",
            "после разрыва",
            "1. Набрано вручную",
            "Спецсимволы: <тег> & \"кавычки\" *звёздочки* [скобки]",
            "Имя\tВозраст",
            "Анна | Мария\t30",
            "Конец.",
            "",
        ], lines);
        AssertNoTempLeft();
    }

    [Fact]
    public async Task Word_to_markdown()
    {
        var result = await _converter.ConvertAsync(Docx(), Op("word-to-md"));

        Assert.EndsWith("отчёт 1.md", result);
        var md = File.ReadAllText(result, Encoding.UTF8);
        Assert.Equal("""
            # Отчёт о работе

            Привет, **жирный** и *курсив* текст, **важное**

            ## Списки

            - яблоко
            - груша
            1. первый
                1. вложенный
            2. второй

            Ссылка: [сайт](https://example.com/путь)

            *[Рисунок: красный квадрат]*

            до разрыва\
            после разрыва

            1\. Набрано вручную

            Спецсимволы: \<тег> & "кавычки" \*звёздочки\* \[скобки\]

            | Имя | Возраст |
            | --- | --- |
            | Анна \| Мария | 30 |

            Конец.

            """.ReplaceLineEndings("\n"), md);
    }

    [Fact]
    public async Task Word_to_html_is_self_contained()
    {
        var result = await _converter.ConvertAsync(Docx(), Op("word-to-html"));

        Assert.EndsWith("отчёт 1.html", result);
        var html = File.ReadAllText(result, Encoding.UTF8);
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<meta charset=\"UTF-8\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<title>отчёт 1</title>", html);
        Assert.Contains("Отчёт о работе", html);
        Assert.Matches("<h1[^>]*>", html);
        Assert.Contains("data:image/png;base64," + Convert.ToBase64String(TestDocs.Png), html); // картинка внутри файла
        Assert.Contains("https://example.com/", html);
        Assert.Contains("<table", html);
        Assert.Contains("&lt;тег&gt; &amp;", html);
        // Лист и поля — как в документе: A4, сверху/снизу 20 мм, справа 15, слева 30.
        Assert.Contains("@page { size: 210mm 297mm; margin: 20mm 15mm 20mm 30mm; }", html);
        // Разрыв страницы: «после разрыва» — в новом абзаце, который начинается с новой страницы.
        Assert.Matches(new Regex("style=\"break-before: page\"[^>]*>(<[^>]+>)*после разрыва"), html);
        // Ни одного «<span />»: браузер принял бы его за открытый тег.
        Assert.DoesNotMatch(new Regex(@"<(?!br|img|meta|hr|col|link|input|wbr)[a-z0-9]+(\s[^>]*)?/>"), html);
        Assert.DoesNotContain("xml:space", html);
        // Маркер списка Word (U+F0B7 шрифта Symbol) браузер не покажет — заменён на «•».
        Assert.DoesNotContain("\uF0B7", html);
        Assert.Contains("•</span>", html);
    }

    [Fact]
    public void Docx_to_html_landscape_page()
    {
        var result = DocxHtml.Convert(Docx("альбом.docx", landscape: true));

        Assert.Equal(297, result.Page.WidthMm, 0.1);
        Assert.Equal(210, result.Page.HeightMm, 0.1);
        Assert.Equal(20, result.Page.LeftMm, 0.1);
        Assert.Contains("@page { size: 297mm 210mm;", result.Html);
        Assert.Equal(DocxHtml.Convert(Docx()).Html, DocLibsEngine.DocxToHtml(Docx())); // публичный вход для печати через Edge
    }

    [Fact]
    public void Docx_page_breaks_from_style_and_sections()
    {
        var path = Path.Combine(_dir.Path, "гост.docx");
        TestDocs.DocxWithBreaks(path);

        var html = DocxHtml.Convert(path).Html;

        // С новой страницы: «Глава 2» (из стиля) и «Раздел 2» (разрыв раздела). «Глава 1» — начало документа,
        // «Раздел 3» — раздел «на текущей странице».
        Assert.Matches("break-before: page\"[^>]*>(<[^>]+>)*Глава 2<", html);
        Assert.Matches("break-before: page\"[^>]*>(<[^>]+>)*Раздел 2<", html);
        Assert.Equal(2, Regex.Count(html, "break-before: page"));
        Assert.Equal(1, Regex.Count(html, "Глава 1"));
    }

    [Fact]
    public async Task Not_a_word_file_is_a_clear_error()
    {
        var src = _dir.File("не документ.docx", "просто текст");

        var ex = await Assert.ThrowsAsync<ConversionException>(() => _converter.ConvertAsync(src, WordToTxt));

        Assert.Contains("Word", ex.Message);
        AssertNoTempLeft();
    }

    // ---------- Excel → CSV ----------

    private string Xlsx(string name = "таблица.xlsx")
    {
        var path = Path.Combine(_dir.Path, name);
        TestDocs.Xlsx(path);
        return path;
    }

    private static string[] CsvLines(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]); // без BOM русский Excel покажет «кракозябры»
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n");
    }

    [Fact]
    public async Task Excel_to_csv_like_russian_excel()
    {
        var result = await _converter.ConvertAsync(Xlsx(), ExcelToCsv);

        Assert.Equal(Path.Combine(_dir.Path, "таблица.csv"), result);
        Assert.Equal(
        [
            "Имя;Сумма;Дата;Доля;Деньги;",
            "\"Анна; \"\"Ш\"\"\nвторая строка\";1234,5;15.03.2023;15%;1 234,50 ₽;15.03.2023 18:00",
            "0,3;ИСТИНА;#ДЕЛ/0!;2469;;",
            "",
        ], CsvLines(result));
        AssertNoTempLeft();
    }

    [Fact]
    public async Task Excel_to_csv_follows_regional_settings()
    {
        var converter = new Converter(new ToolLocator(), ConversionSettings.Default, [new DocLibsEngine(CultureInfo.GetCultureInfo("en-US"))]);

        var result = await converter.ConvertAsync(Xlsx(), ExcelToCsv);

        var lines = CsvLines(result);
        Assert.Equal("Имя,Сумма,Дата,Доля,Деньги,", lines[0]);
        Assert.StartsWith("\"Анна; \"\"Ш\"\"\nвторая строка\",1234.5,3/15/2023,15%,\"1,234.50 ₽\",", lines[1]);
        Assert.Equal("0.3,TRUE,#DIV/0!,2469,,", lines[2]);
    }

    [Fact]
    public async Task Old_xls_to_csv()
    {
        // Fixtures/sample.xls — Excel 97 (BIFF8), сохранён из LibreOffice. Дату LibreOffice записал своим форматом
        // «m/d/yyyy» (не встроенным 14) — русский Excel тоже покажет её как 3.15.2023: «/» — разделитель даты Windows.
        var src = Path.Combine(_dir.Path, "города.xls");
        File.Copy(Integration.Tools.Fixture("sample.xls"), src);

        var result = await _converter.ConvertAsync(src, ExcelToCsv);

        Assert.Equal(
        [
            "Город;Население;Дата;Доля",
            "Москва;13010112;3.15.2023;50%",
            "\"Санкт-Петербург; Ленинград\";5601911,5;;25%",
            "",
        ], CsvLines(result));
    }

    [Theory]
    [InlineData("ru-RU", ';')]
    [InlineData("en-US", ',')]
    [InlineData("de-DE", ';')]
    public void Csv_separator_from_culture(string culture, char expected) =>
        Assert.Equal(expected, SpreadsheetCsv.Separator(CultureInfo.GetCultureInfo(culture)));

    // ---------- CSV → XLSX ----------

    private static List<object?[]> ReadXlsx(string path, out string sheet)
    {
        TextFiles.EnsureCodePages();
        using var stream = File.OpenRead(path);
        using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream);
        sheet = reader.Name;
        var rows = new List<object?[]>();
        while (reader.Read())
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++) row[i] = reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    [Fact]
    public async Task Csv_to_xlsx_typed_values()
    {
        var src = Path.Combine(_dir.Path, "данные.csv");
        File.WriteAllText(src, """"
            Имя;Сумма;Код;Телефон;Дата;Формула;Доля
            Анна;1234,5;007;+79161234567;20.10.2026;=1+1;15%
            "Борис; младший";"1 234 567,89";12;;2026-10-20 13:45;"текст ""в кавычках""";-0,5

            """", TextFiles.Utf8Bom);

        var result = await _converter.ConvertAsync(src, Op("csv-to-xlsx"));

        Assert.Equal(Path.Combine(_dir.Path, "данные.xlsx"), result);
        var rows = ReadXlsx(result, out var sheet);
        Assert.Equal("данные", sheet);
        Assert.Equal(3, rows.Count);
        Assert.Equal(["Имя", "Сумма", "Код", "Телефон", "Дата", "Формула", "Доля"], rows[0]);
        Assert.Equal("Анна", rows[1][0]);
        Assert.Equal(1234.5, rows[1][1]);
        Assert.Equal("007", rows[1][2]);           // код с ведущими нулями — текст
        Assert.Equal("+79161234567", rows[1][3]);  // телефон — текст
        Assert.Equal(new DateTime(2026, 10, 20), rows[1][4]);
        Assert.Equal("=1+1", rows[1][5]);          // никаких формул из CSV
        Assert.Equal(0.15, (double)rows[1][6]!, 10);
        Assert.Equal("Борис; младший", rows[2][0]);
        Assert.Equal(1234567.89, rows[2][1]);
        Assert.Equal(12.0, rows[2][2]);
        Assert.Null(rows[2][3]);
        Assert.Equal(new DateTime(2026, 10, 20, 13, 45, 0), rows[2][4]);
        Assert.Equal("текст \"в кавычках\"", rows[2][5]);
        Assert.Equal(-0.5, rows[2][6]);

        using var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(result, false);
        Assert.Empty(doc.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<DocumentFormat.OpenXml.Spreadsheet.CellFormula>());
        AssertNoTempLeft();
    }

    [Fact]
    public async Task Csv_to_xlsx_windows1251_with_commas()
    {
        var src = Path.Combine(_dir.Path, "старый.csv");
        File.WriteAllBytes(src, TextFiles.Windows1251.GetBytes(
            "Город,Население,Доля\r\nМосква,13010112,0.5\r\n\"Казань, Татарстан\",1308660,1.5\r\n"));

        var result = await _converter.ConvertAsync(src, Op("csv-to-xlsx"));

        var rows = ReadXlsx(result, out _);
        Assert.Equal(["Город", "Население", "Доля"], rows[0]);
        Assert.Equal(["Москва", 13010112.0, 0.5], rows[1]);
        Assert.Equal(["Казань, Татарстан", 1308660.0, 1.5], rows[2]);
    }

    [Theory]
    [InlineData("a;b;c\n1;2;3", ';')]
    [InlineData("a,b,c\n1,2,3", ',')]
    [InlineData("a\tb\tc\n1\t2\t3", '\t')]
    [InlineData("имя;комментарий\nАнна;\"да, нет, может\"\nБорис;\"1,2,3\"", ';')]
    [InlineData("a,b\n\"x;y;z\",2\n\"p;q\",3", ',')]
    [InlineData("Иванов, Иван;30\nПетров, Пётр;40", ';')]
    [InlineData("один столбец\nбез разделителей", ';')]
    public void Csv_separator_detection(string text, char expected) =>
        Assert.Equal(expected, CsvToXlsx.DetectSeparator(text));

    [Fact]
    public void Csv_parse_quotes_and_line_breaks()
    {
        var rows = CsvToXlsx.Parse("a;\"b;c\";\"d\"\"e\"\r\n\"много\nстрок\";x\n\nпоследняя\n", ';');

        Assert.Equal(4, rows.Count);
        Assert.Equal(["a", "b;c", "d\"e"], rows[0]);
        Assert.Equal(["много\nстрок", "x"], rows[1]);
        Assert.Equal([""], rows[2]);
        Assert.Equal(["последняя"], rows[3]);
    }

    [Theory]
    [InlineData("1234,5", true, 1234.5)]
    [InlineData("0,25", true, 0.25)]
    [InlineData("-12", true, -12.0)]
    [InlineData("12.05", true, 12.05)]
    [InlineData("1 234 567,89", true, 1234567.89)]
    [InlineData("1 234", true, 1234.0)]
    [InlineData("1.5", false, 1.5)]
    public void Csv_numbers(string text, bool commaDecimal, double expected)
    {
        var (kind, value) = CsvToXlsx.Classify(text, commaDecimal);
        Assert.Equal(CsvToXlsx.Kind.Number, kind);
        Assert.Equal(expected, value, 9);
    }

    [Theory]
    [InlineData("1234,5", false)]          // при разделителе «,» запятая — не десятичная
    [InlineData("007", true)]
    [InlineData("+79161234567", true)]
    [InlineData("1234567890123456", true)] // 16 цифр — Excel потеряет точность
    [InlineData("=СУММ(A1:A2)", true)]
    [InlineData("12-34", true)]
    [InlineData("31.02.2026", true)]       // такой даты нет
    public void Csv_texts_stay_text(string text, bool commaDecimal) =>
        Assert.Equal(CsvToXlsx.Kind.Text, CsvToXlsx.Classify(text, commaDecimal).Kind);

    [Fact]
    public void Csv_sheet_name_and_columns()
    {
        Assert.Equal("отчёт 2026", CsvToXlsx.SheetName("отчёт [2026]"));
        Assert.Equal("ab", CsvToXlsx.SheetName("a/b?"));
        Assert.Equal("Лист1", CsvToXlsx.SheetName("[]"));
        Assert.Equal(31, CsvToXlsx.SheetName(new string('я', 40)).Length);
        Assert.Equal(["A", "Z", "AA", "AZ", "XFD"], new[] { 0, 25, 26, 51, 16383 }.Select(CsvToXlsx.ColumnName));
    }

    // ---------- Markdown → HTML ----------

    [Fact]
    public async Task Markdown_to_html()
    {
        var src = Path.Combine(_dir.Path, "заметки.md");
        // Старый файл в Windows-1251 — кодировка угадывается.
        File.WriteAllBytes(src, TextFiles.Windows1251.GetBytes(
            "# Заголовок\n\nТекст с **жирным** и [ссылкой](https://example.com).\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n```\ncode <x>\n```\n\n- [x] сделано\n"));

        var result = await _converter.ConvertAsync(src, Op("md-to-html"));

        Assert.Equal(Path.Combine(_dir.Path, "заметки.html"), result);
        var html = File.ReadAllText(result, Encoding.UTF8);
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<meta charset=\"utf-8\">", html);
        Assert.Contains("<title>заметки</title>", html);
        Assert.Contains(">Заголовок</h1>", html);
        Assert.Contains("<strong>жирным</strong>", html);
        Assert.Contains("<a href=\"https://example.com\">ссылкой</a>", html);
        Assert.Contains("<table>", html);
        Assert.Contains("code &lt;x&gt;", html);
        Assert.Contains("type=\"checkbox\"", html);
        Assert.Contains("@page", html);
        Assert.DoesNotContain("<base", html); // HTML рядом с исходником — относительные ссылки и так работают
    }

    // ---------- CLI ----------

    [Fact]
    public async Task Cli_converts_csv_to_xlsx()
    {
        var src = Path.Combine(_dir.Path, "из консоли.csv");
        File.WriteAllText(src, "a;b\n1;2\n");
        var stdout = new StringWriter();

        var code = await Cli.Program.RunAsync(["convert", src, "csv-to-xlsx"], stdout, new StringWriter());

        Assert.Equal(0, code);
        Assert.Contains("Готово", stdout.ToString());
        Assert.True(File.Exists(Path.Combine(_dir.Path, "из консоли.xlsx")));
    }
}
