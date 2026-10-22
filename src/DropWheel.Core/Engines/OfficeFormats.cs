using DropWheel.Localization;
using DropWheel.Conversions;

namespace DropWheel.Engines;

public enum OfficeApp { Word, Excel, PowerPoint }

/// <summary>Как Office получает результат: «Сохранить как» с кодом формата, экспорт в PDF или слайды по одному в PNG.</summary>
public enum OfficeAction { SaveAs, ExportPdf, SlidesToPng }

/// <param name="App">Приложение, которое открывает вход.</param>
/// <param name="Format">Код формата Office: WdSaveFormat / XlFileFormat / PpSaveAsFileType для SaveAs,
/// WdExportFormat / XlFixedFormatType для ExportPdf.</param>
public sealed record OfficeTarget(OfficeApp App, OfficeAction Action, int Format);

/// <summary>
/// Операции каталога (блок OFFICE) → что делает Office. Только данные и проверки файлов, без COM —
/// поэтому тестируется на Linux; сам COM — в OfficeEngine (WPF-проект).
/// </summary>
public static class OfficeFormats
{
    // Word: WdSaveFormat, WdExportFormat.
    public const int WordDoc = 0, WordRtf = 6, WordUnicodeText = 7, WordDocx = 16, WordPdf = 17, WordOdt = 23;
    /// <summary>msoEncodingUTF8 — кодировка TXT из Word.</summary>
    public const int Utf8CodePage = 65001;

    // Excel: XlFixedFormatType.xlTypePDF; XlFileFormat. CSV UTF-8 есть с Excel 2016 — для старых запасной ANSI-CSV.
    public const int ExcelPdf = 0, ExcelCsvAnsi = 6, ExcelXlsx = 51, ExcelXls = 56, ExcelOds = 60, ExcelCsvUtf8 = 62;

    // PowerPoint: PpSaveAsFileType.
    public const int PptPpt = 1, PptPptx = 24, PptPdf = 32, PptOdp = 35;

    private static readonly Dictionary<string, OfficeTarget> ById = new(StringComparer.OrdinalIgnoreCase)
    {
        ["word-to-pdf"] = new(OfficeApp.Word, OfficeAction.ExportPdf, WordPdf),
        ["word-to-docx"] = new(OfficeApp.Word, OfficeAction.SaveAs, WordDocx),
        ["word-to-doc"] = new(OfficeApp.Word, OfficeAction.SaveAs, WordDoc),
        ["word-to-odt"] = new(OfficeApp.Word, OfficeAction.SaveAs, WordOdt),
        ["word-to-rtf"] = new(OfficeApp.Word, OfficeAction.SaveAs, WordRtf),
        ["word-to-txt"] = new(OfficeApp.Word, OfficeAction.SaveAs, WordUnicodeText),
        // Word сам «перетекает» PDF в редактируемый документ (PDF Reflow, Word 2013+).
        ["pdf-to-docx"] = new(OfficeApp.Word, OfficeAction.SaveAs, WordDocx),
        // PDF → Markdown: то же PDF → DOCX во временный файл, дальше DocxText.ToMarkdown (см. ViaDocx).
        [PdfToMdId] = new(OfficeApp.Word, OfficeAction.SaveAs, WordDocx),

        ["excel-to-pdf"] = new(OfficeApp.Excel, OfficeAction.ExportPdf, ExcelPdf),
        ["excel-to-xlsx"] = new(OfficeApp.Excel, OfficeAction.SaveAs, ExcelXlsx),
        ["excel-to-xls"] = new(OfficeApp.Excel, OfficeAction.SaveAs, ExcelXls),
        ["excel-to-csv"] = new(OfficeApp.Excel, OfficeAction.SaveAs, ExcelCsvUtf8),
        ["excel-to-ods"] = new(OfficeApp.Excel, OfficeAction.SaveAs, ExcelOds),

        ["ppt-to-pdf"] = new(OfficeApp.PowerPoint, OfficeAction.SaveAs, PptPdf),
        ["ppt-to-pptx"] = new(OfficeApp.PowerPoint, OfficeAction.SaveAs, PptPptx),
        ["ppt-to-ppt"] = new(OfficeApp.PowerPoint, OfficeAction.SaveAs, PptPpt),
        ["ppt-to-odp"] = new(OfficeApp.PowerPoint, OfficeAction.SaveAs, PptOdp),
        ["ppt-to-png"] = new(OfficeApp.PowerPoint, OfficeAction.SlidesToPng, 0),
    };

    /// <summary>PDF → Markdown: Word сохраняет DOCX, из которого делается Markdown.</summary>
    public const string PdfToMdId = "pdf-to-md";

    /// <summary>
    /// Office сохраняет не сам результат, а промежуточный DOCX (рядом с временным результатом, в скрытой папке),
    /// который затем переводится в Markdown. null — Office пишет результат сам.
    /// </summary>
    public static string? ViaDocx(Operation op, string output) =>
        string.Equals(op.Id, PdfToMdId, StringComparison.OrdinalIgnoreCase) ? Path.ChangeExtension(output, ".docx") : null;

    /// <summary>Что делать Office для операции; null — операция не для Office.</summary>
    public static OfficeTarget? For(Operation op) => ById.GetValueOrDefault(op.Id);

    public static IReadOnlyCollection<string> OperationIds => ById.Keys;

    /// <summary>Имя файла слайда в папке результата: «слайд-1.png»…, при 10+ слайдах — «слайд-01.png» (как страницы PDF).</summary>
    public static string SlideFileName(int number, int count) =>
        $"{Loc.T.SlideFile}-{number.ToString().PadLeft(count.ToString().Length, '0')}.png";

    private static readonly string[] OpenXml = ["docx", "docm", "xlsx", "xlsm", "pptx", "pptm"];
    private static readonly byte[] CompoundFileSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] EncryptedPackage = System.Text.Encoding.Unicode.GetBytes("EncryptedPackage");

    /// <summary>
    /// DOCX/XLSX/PPTX с паролем на открытие — не ZIP, а составной файл OLE (сигнатура D0 CF 11 E0)
    /// с потоком «EncryptedPackage». Распознаём заранее, не открывая Office: PowerPoint, в отличие от Word
    /// и Excel, не принимает пароль-заглушку и показал бы невидимое окно с вопросом пароля.
    /// Старые DOC/XLS/PPT всегда составные файлы — для них проверка ничего не говорит (false).
    /// </summary>
    public static bool IsPasswordProtected(string path)
    {
        if (!OpenXml.Contains(Extensions.Normalize(Path.GetExtension(path)))) return false;
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var head = new byte[CompoundFileSignature.Length];
            if (file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) < head.Length
                || !head.AsSpan().SequenceEqual(CompoundFileSignature))
                return false;
            return Contains(file, EncryptedPackage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // не прочитали — пусть Office сам скажет, что не так
        }
    }

    /// <summary>Поиск байтов по всему потоку кусками (с перекрытием на длину образца).</summary>
    private static bool Contains(Stream stream, byte[] pattern)
    {
        var buffer = new byte[1 << 16];
        var kept = 0;
        int read;
        while ((read = stream.Read(buffer, kept, buffer.Length - kept)) > 0)
        {
            var length = kept + read;
            if (buffer.AsSpan(0, length).IndexOf(pattern) >= 0) return true;
            kept = Math.Min(pattern.Length - 1, length);
            buffer.AsSpan(length - kept, kept).CopyTo(buffer);
        }
        return false;
    }
}
