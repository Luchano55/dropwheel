using System.Globalization;
using System.Text;
using DropWheel.Conversions;
using DropWheel.Documents;

namespace DropWheel.Engines;

/// <summary>
/// Операции Office (блок OFFICE каталога), которые умеет установленный LibreOffice, и аргументы soffice.
/// Только данные — тестируется без LibreOffice. Решение D-LIBREOFFICE.
/// </summary>
public static class LibreOfficeFormats
{
    /// <summary>Фильтр «--convert-to»: «расширение:имя фильтра[:параметры]».</summary>
    private static readonly Dictionary<string, string> ById = new(StringComparer.OrdinalIgnoreCase)
    {
        ["word-to-pdf"] = "pdf:writer_pdf_Export",
        ["word-to-docx"] = "docx:MS Word 2007 XML",
        ["word-to-doc"] = "doc:MS Word 97",
        ["word-to-odt"] = "odt:writer8",
        ["word-to-rtf"] = "rtf:Rich Text Format",
        // Как у Word: UTF-8 с BOM, строки через CRLF.
        ["word-to-txt"] = "txt:Text (encoded):UTF8,CRLF",

        ["excel-to-pdf"] = "pdf:calc_pdf_Export",
        ["excel-to-xlsx"] = "xlsx:Calc MS Excel 2007 XML",
        ["excel-to-xls"] = "xls:MS Excel 97",
        [ExcelToCsvId] = "csv:Text - txt - csv (StarCalc)", // параметры — в Filter (разделитель из настроек Windows)
        ["excel-to-ods"] = "ods:calc8",

        ["ppt-to-pdf"] = "pdf:impress_pdf_Export",
        ["ppt-to-pptx"] = "pptx:Impress MS PowerPoint 2007 XML",
        ["ppt-to-ppt"] = "ppt:MS PowerPoint 97",
        ["ppt-to-odp"] = "odp:impress8",
        // Нет: «Слайды PNG» (LibreOffice сохраняет только первый слайд), PDF → DOCX / Markdown (плохо перестраивает PDF).
    };

    public const string ExcelToCsvId = "excel-to-csv";

    public static IReadOnlyCollection<string> OperationIds => ById.Keys;

    /// <summary>Может ли LibreOffice выполнить операцию; extension — вход без точки (null — не проверять).</summary>
    public static bool Supports(Operation op, string? extension = null)
    {
        if (!ById.ContainsKey(op.Id)) return false;
        if (extension is null) return true;
        var ext = Extensions.Normalize(extension);
        // CSV на входе LibreOffice читал бы со своими настройками (запятая, кодировка) — CSV → XLSX есть отдельно.
        return ext != "csv" && op.AppliesTo(ext);
    }

    /// <summary>
    /// Фильтр «--convert-to» для операции. CSV — UTF-8, разделитель — как у Excel и запасного движка
    /// (разделитель элементов списка из региональных настроек: у русской Windows «;»), кавычки «"».
    /// </summary>
    /// <param name="sheet">CSV: номер листа (с 1) — первый видимый, как у Excel; null — первый.</param>
    public static string Filter(Operation op, CultureInfo culture, int? sheet = null)
    {
        var filter = ById[op.Id];
        if (string.Equals(op.Id, ExcelToCsvId, StringComparison.OrdinalIgnoreCase))
        {
            // Разделитель, кавычки, UTF-8, первая строка; затем (с 6-го) язык, «текст в кавычках», особые числа,
            // «как показано» (значения в формате ячеек), формулы, пробелы; 12-й — номер листа.
            filter += $":{(int)SpreadsheetCsv.Separator(culture)},34,76,1";
            if (sheet is > 1) filter += $",,,false,true,true,false,false,{sheet.Value.ToString(CultureInfo.InvariantCulture)}";
        }
        return filter;
    }

    /// <summary>Расширение файла, который LibreOffice запишет в --outdir (часть фильтра до «:»).</summary>
    public static string OutputExtension(string filter) => filter.Split(':')[0];

    /// <summary>
    /// Аргументы soffice: без окон, без восстановления и проверки блокировок, свой профиль (не мешает
    /// открытому LibreOffice пользователя и не показывает окон первого запуска), результат — в outDir.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string input, string outDir, string profileDir, string filter) =>
    [
        "-env:UserInstallation=" + FileUrl(profileDir),
        "--headless", "--norestore", "--nologo", "--nodefault", "--nolockcheck",
        "--convert-to", filter,
        "--outdir", outDir,
        input,
    ];

    /// <summary>
    /// Путь → file:// URL (так LibreOffice принимает папку профиля): UTF-8 с %-кодированием всего,
    /// кроме латиницы, цифр и «-._~/:». «C:\Users\Роман\x y» → «file:///C:/Users/%D0%A0…/x%20y».
    /// </summary>
    public static string FileUrl(string path)
    {
        var p = path.Replace('\\', '/');
        string prefix;
        if (p.StartsWith("//", StringComparison.Ordinal)) { prefix = "file:"; }          // \\сервер\папка
        else if (p.Length >= 2 && p[1] == ':' && char.IsAsciiLetter(p[0])) { prefix = "file:///"; } // C:/…
        else { prefix = "file://"; if (!p.StartsWith('/')) p = "/" + p; }
        var sb = new StringBuilder(prefix);
        foreach (var b in Encoding.UTF8.GetBytes(p))
        {
            var c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~' or '/' or ':') sb.Append(c);
            else sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}
