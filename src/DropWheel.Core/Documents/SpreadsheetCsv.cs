using DropWheel.Localization;
using System.Globalization;
using System.Text;
using DropWheel.Engines;
using ExcelDataReader;
using ExcelDataReader.Exceptions;
using ExcelNumberFormat;

namespace DropWheel.Documents;

/// <summary>
/// XLSX / XLSM / XLS → CSV, как «Сохранить как → CSV» в Excel: первый видимый лист, значения так,
/// как они показаны в ячейках (формат чисел и дат), разделитель — из региональных настроек
/// (в России «;», десятичная запятая), UTF-8 с BOM, строки через CRLF. Формулы — их последние значения.
/// </summary>
public static class SpreadsheetCsv
{
    /// <summary>
    /// Разделитель полей — «разделитель элементов списка» из региональных настроек (ru-RU — «;»),
    /// как у Excel при сохранении в CSV (и у движка Office, Local:=True); пусто — «;».
    /// </summary>
    public static char Separator(CultureInfo culture) =>
        culture.TextInfo.ListSeparator is { Length: > 0 } list && list[0] is not '"' and not '\r' and not '\n' ? list[0] : ';';

    public static void Convert(string input, string output, CultureInfo culture, CancellationToken ct = default)
    {
        TextFiles.EnsureCodePages(); // старые .xls хранят строки в кодовых страницах Windows
        var rows = Read(input, culture, ct);
        var separator = Separator(culture);

        using var writer = new StreamWriter(output, append: false, TextFiles.Utf8Bom);
        writer.NewLine = "\r\n";
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            for (var i = 0; i < row.Length; i++)
            {
                if (i > 0) writer.Write(separator);
                writer.Write(Quote(row[i], separator));
            }
            writer.WriteLine();
        }
    }

    /// <summary>Поле в кавычках, если в нём разделитель, кавычка или перевод строки; кавычки удваиваются.</summary>
    public static string Quote(string value, char separator) =>
        value.IndexOfAny([separator, '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    /// <summary>Таблица текстов первого видимого листа без пустых строк и столбцов в конце.</summary>
    public static List<string[]> Read(string input, CultureInfo culture, CancellationToken ct = default)
    {
        TextFiles.EnsureCodePages();
        using var stream = TextFiles.OpenRead(input);
        IExcelDataReader reader;
        try
        {
            reader = ExcelReaderFactory.CreateReader(stream);
        }
        catch (InvalidPasswordException ex)
        {
            throw new ConversionException(Loc.T.ErrSheetPassword, ex.Message, ex);
        }
        catch (Exception ex) when (ex is ExcelReaderException or InvalidDataException or IOException or NotSupportedException)
        {
            throw new ConversionException(Loc.T.ErrSheetOpen, ex.Message, ex);
        }

        using (reader)
        {
            // Первый видимый лист (скрытые Excel в CSV не сохраняет).
            var found = false;
            do
            {
                if (reader.VisibleState is null or "visible") { found = true; break; }
            } while (reader.NextResult());
            if (!found) reader.Reset();

            var formats = new Dictionary<string, NumberFormat>();
            var rows = new List<string[]>();
            try
            {
                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var cells = new string[reader.FieldCount];
                    for (var i = 0; i < cells.Length; i++) cells[i] = CellText(reader, i, culture, formats);
                    rows.Add(cells);
                }
            }
            catch (Exception ex) when (ex is ExcelReaderException or InvalidDataException or InvalidCastException or FormatException)
            {
                throw new ConversionException(Loc.T.ErrSheetRead, ex.Message, ex);
            }
            return Trim(rows);
        }
    }

    private static List<string[]> Trim(List<string[]> rows)
    {
        var lastRow = rows.FindLastIndex(r => r.Any(c => c.Length > 0));
        if (lastRow < 0) return [];
        rows.RemoveRange(lastRow + 1, rows.Count - lastRow - 1);
        var columns = rows.Max(r => Array.FindLastIndex(r, c => c.Length > 0)) + 1;
        return rows.Select(r => r.Length == columns ? r : Enumerable.Range(0, columns).Select(i => i < r.Length ? r[i] : "").ToArray()).ToList();
    }

    private static string CellText(IExcelDataReader reader, int i, CultureInfo culture, Dictionary<string, NumberFormat> formats)
    {
        var value = reader.GetValue(i);
        if (value is null)
            return reader.GetCellError(i) is { } error ? ErrorText(error, culture) : "";

        switch (value)
        {
            case string s:
                return s;
            case bool b:
                return IsRussian(culture) ? (b ? "ИСТИНА" : "ЛОЖЬ") : (b ? "TRUE" : "FALSE");
        }

        // Встроенные форматы 14 и 22 в Excel зависят от региональных настроек: в России — 31.12.2026 и 31.12.2026 18:00.
        var index = reader.GetNumberFormatIndex(i);
        if (value is DateTime date && index is 14 or 22)
        {
            var pattern = culture.DateTimeFormat.ShortDatePattern + (index == 22 ? " " + culture.DateTimeFormat.ShortTimePattern : "");
            return date.ToString(pattern, culture);
        }

        var code = reader.GetNumberFormatString(i) ?? "General";
        if (!formats.TryGetValue(code, out var format)) formats[code] = format = new NumberFormat(code);
        try
        {
            if (format.IsValid) return format.Format(value, culture);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidCastException or OverflowException)
        {
        }
        return value switch
        {
            double d => d.ToString("G15", culture),
            DateTime d => d.ToString(culture),
            IFormattable f => f.ToString(null, culture),
            _ => value.ToString() ?? "",
        };
    }

    private static bool IsRussian(CultureInfo culture) => culture.TwoLetterISOLanguageName == "ru";

    private static string ErrorText(CellError error, CultureInfo culture)
    {
        var ru = IsRussian(culture);
        return error switch
        {
            CellError.NULL => ru ? "#ПУСТО!" : "#NULL!",
            CellError.DIV0 => ru ? "#ДЕЛ/0!" : "#DIV/0!",
            CellError.VALUE => ru ? "#ЗНАЧ!" : "#VALUE!",
            CellError.REF => ru ? "#ССЫЛКА!" : "#REF!",
            CellError.NAME => ru ? "#ИМЯ?" : "#NAME?",
            CellError.NUM => ru ? "#ЧИСЛО!" : "#NUM!",
            CellError.NA => ru ? "#Н/Д" : "#N/A",
            _ => "",
        };
    }
}
