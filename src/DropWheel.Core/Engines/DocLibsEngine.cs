using DropWheel.Localization;
using System.Globalization;
using DropWheel.Conversions;
using DropWheel.Documents;

namespace DropWheel.Engines;

/// <summary>
/// Документы без Microsoft Office — бесплатные библиотеки (OpenXML SDK, OpenXmlPowerTools, ExcelDataReader,
/// ExcelNumberFormat, Markdig), работает везде и сразу. Свои операции (Word → HTML/Markdown, CSV → XLSX,
/// Markdown → HTML) и запасной движок для операций Office, когда Office не установлен:
/// Word → TXT (только DOCX) и Excel → CSV (XLSX, XLSM, XLS). Решение D-DOCLIBS.
/// </summary>
/// <param name="csvCulture">Региональные настройки для CSV (разделитель, формат чисел и дат);
/// null — текущие настройки Windows в момент конвертации, как у самого Excel.</param>
public sealed class DocLibsEngine(CultureInfo? csvCulture = null) : IInternalEngine
{
    // Операции Office, которые этот движок выполняет вместо Office.
    public const string WordToTxtId = "word-to-txt";
    public const string ExcelToCsvId = "excel-to-csv";
    // Свои операции.
    public const string WordToHtmlId = "word-to-html";
    public const string WordToMdId = "word-to-md";
    public const string CsvToXlsxId = "csv-to-xlsx";
    public const string MdToHtmlId = "md-to-html";

    /// <summary>Word в формате OOXML. DOC, RTF, ODT библиотеки не читают — для них нужен Office.</summary>
    public static readonly IReadOnlyList<string> WordInputs = ["docx", "docm"];
    public static readonly IReadOnlyList<string> ExcelInputs = ["xlsx", "xlsm", "xls"];
    public static readonly IReadOnlyList<string> CsvInputs = ["csv"];
    public static readonly IReadOnlyList<string> MarkdownInputs = ["md", "markdown"];

    public EngineKind Kind => EngineKind.DocLibs;
    public bool IsAvailable => true;

    public bool CanRun(Operation op) => InputsFor(op.Id) is not null;

    public bool CanRun(Operation op, string extension) => InputsFor(op.Id)?.Contains(extension) == true;

    private static IReadOnlyList<string>? InputsFor(string id) => id switch
    {
        WordToTxtId or WordToHtmlId or WordToMdId => WordInputs,
        ExcelToCsvId => ExcelInputs,
        CsvToXlsxId => CsvInputs,
        MdToHtmlId => MarkdownInputs,
        _ => null,
    };

    /// <summary>DOCX → самодостаточный HTML (картинки внутри, CSS для печати на листе из документа). Для печати в PDF через Edge.</summary>
    public static string DocxToHtml(string path) => DocxHtml.Convert(path).Html;

    public Task RunAsync(Operation op, IReadOnlyList<string> inputs, string output, ConversionSettings settings,
        IProgress<double>? progress, CancellationToken ct)
    {
        var input = inputs[0];
        var extension = Extensions.Normalize(Path.GetExtension(input));
        if (!CanRun(op, extension))
            throw new ConversionException(Loc.F(Loc.T.ErrNoOfficeFor, Loc.Label(op), extension), op.Id);

        // Библиотеки синхронные — не занимаем поток вызывающего (может быть поток интерфейса).
        return Task.Run(() => Run(op.Id, input, output, ct), ct);
    }

    private void Run(string id, string input, string output, CancellationToken ct)
    {
        switch (id)
        {
            case WordToTxtId:
                File.WriteAllText(output, DocxText.ToPlainText(input, ct), TextFiles.Utf8Bom);
                break;
            case WordToMdId:
                File.WriteAllText(output, DocxText.ToMarkdown(input, ct), TextFiles.Utf8);
                break;
            case WordToHtmlId:
                File.WriteAllText(output, DocxHtml.Convert(input, ct).Html, TextFiles.Utf8);
                break;
            case ExcelToCsvId:
                SpreadsheetCsv.Convert(input, output, csvCulture ?? CultureInfo.CurrentCulture, ct);
                break;
            case CsvToXlsxId:
                CsvToXlsx.Convert(input, output, ct);
                break;
            case MdToHtmlId:
                var html = HtmlPages.FromMarkdown(TextFiles.Read(input), Path.GetFileNameWithoutExtension(input));
                File.WriteAllText(output, html, TextFiles.Utf8);
                break;
            default:
                throw new ConversionException(Loc.T.ErrDocUnknownOp, id);
        }
    }
}
