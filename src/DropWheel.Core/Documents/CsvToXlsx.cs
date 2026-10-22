using DropWheel.Localization;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DropWheel.Engines;

namespace DropWheel.Documents;

/// <summary>
/// CSV → XLSX. Кодировка угадывается (BOM, UTF-8, иначе Windows-1251), разделитель — «;», «,» или табуляция
/// (по тому, какой даёт одинаковое число полей в строках). Числа и даты становятся числами и датами Excel,
/// но коды с ведущими нулями («007»), телефоны («+7…») и длинные номера (больше 15 цифр) остаются текстом.
/// Формулы не создаются никогда: «=…» — просто текст.
/// </summary>
public static partial class CsvToXlsx
{
    public const int MaxRows = 1_048_576;
    public const int MaxColumns = 16_384;
    private const int MaxCellText = 32_767;
    private static readonly char[] Candidates = [';', '\t', ','];

    public static void Convert(string input, string output, CancellationToken ct = default)
    {
        var text = TextFiles.Decode(TextFiles.ReadAllBytes(input), out _);
        var separator = DetectSeparator(text);
        var rows = Parse(text, separator, ct);
        if (rows.Count > MaxRows)
            throw new ConversionException(Loc.T.ErrCsvTooManyRows);
        if (rows.Count > 0 && rows.Max(r => r.Length) > MaxColumns)
            throw new ConversionException(Loc.T.ErrCsvTooManyColumns);
        Write(rows, output, SheetName(Path.GetFileNameWithoutExtension(input)), commaDecimal: separator != ',', ct);
    }

    // ---------- Разбор CSV ----------

    /// <summary>
    /// Разделитель — тот, при котором первые строки делятся на одинаковое (и большее единицы) число полей.
    /// При равенстве: «;» (русский Excel), затем табуляция, затем «,».
    /// </summary>
    public static char DetectSeparator(string text)
    {
        var sample = text.Length > 64 * 1024 ? text[..(64 * 1024)] : text;
        var best = Candidates[0];
        var bestScore = (Consistent: -1, Fields: 0);
        foreach (var candidate in Candidates)
        {
            var rows = Parse(sample, candidate, CancellationToken.None, maxRows: 50);
            if (text.Length > sample.Length && rows.Count > 1) rows.RemoveAt(rows.Count - 1); // последняя строка может быть обрезана
            if (rows.Count == 0) continue;
            var mode = rows.GroupBy(r => r.Length).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First();
            if (mode.Key < 2) continue;
            var score = (Consistent: mode.Count(), Fields: mode.Key);
            if (score.Consistent > bestScore.Consistent
                || (score.Consistent == bestScore.Consistent && score.Fields > bestScore.Fields))
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>RFC 4180: поля в кавычках могут содержать разделитель, перевод строки и удвоенные кавычки.</summary>
    public static List<string[]> Parse(string text, char separator, CancellationToken ct = default, int maxRows = int.MaxValue)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var fieldStarted = false;
        var i = 0;

        void EndField()
        {
            row.Add(field.ToString());
            field.Clear();
            fieldStarted = false;
        }
        void EndRow()
        {
            EndField();
            rows.Add([.. row]);
            row.Clear();
            if ((rows.Count & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
        }

        while (i < text.Length && rows.Count < maxRows)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    quoted = false;
                }
                else field.Append(c);
                i++;
                continue;
            }

            if (c == '"' && !fieldStarted)
            {
                quoted = true;
                fieldStarted = true;
            }
            else if (c == separator) EndField();
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                EndRow();
            }
            else
            {
                field.Append(c);
                fieldStarted = true;
            }
            i++;
        }
        if (rows.Count < maxRows && (field.Length > 0 || row.Count > 0 || fieldStarted)) EndRow();
        return rows;
    }

    // ---------- Значения ----------

    internal enum Kind { Text, Number, Percent, Date, DateTime }

    private static readonly string[] DateFormats = ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd"];
    private static readonly string[] DateTimeFormats =
    [
        "dd.MM.yyyy H:mm", "dd.MM.yyyy H:mm:ss", "d.M.yyyy H:mm", "d.M.yyyy H:mm:ss",
        "yyyy-MM-dd H:mm", "yyyy-MM-dd H:mm:ss", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss",
    ];

    /// <param name="commaDecimal">Десятичная запятая допустима (разделитель полей не «,»).</param>
    internal static (Kind Kind, double Value) Classify(string raw, bool commaDecimal)
    {
        var s = raw.Trim();
        if (s.Length is 0 or > 40) return (Kind.Text, 0);

        var percent = s.EndsWith('%');
        var number = percent ? s[..^1].TrimEnd() : s;
        var match = NumberPattern().Match(number);
        if (match.Success && (commaDecimal || !number.Contains(',')))
        {
            var integer = match.Groups["int"].Value;
            var digits = number.Count(char.IsAsciiDigit);
            // «007», «0123» — коды, не числа; больше 15 цифр Excel хранит с потерей точности.
            if (!(integer.Length > 1 && integer[0] == '0') && digits <= 15)
            {
                var invariant = GroupSpaces().Replace(number, "").Replace(',', '.');
                if (double.TryParse(invariant, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var value))
                    return percent ? (Kind.Percent, value / 100) : (Kind.Number, value);
            }
        }

        if (!percent && s.Length >= 8 && char.IsAsciiDigit(s[0]))
        {
            if (DateTime.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && date.Year >= 1900)
                return (Kind.Date, date.ToOADate());
            if (DateTime.TryParseExact(s, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime)
                && dateTime.Year >= 1900)
                return (Kind.DateTime, dateTime.ToOADate());
        }
        return (Kind.Text, 0);
    }

    // -12345, 1234,5, 1 234 567,89 (пробел или неразрывный пробел между тысячами), 0.25
    [GeneratedRegex(@"^-?(?<int>\d+|\d{1,3}(?:[ \u00A0\u202F]\d{3})+)(?:[.,]\d+)?$")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"[ \u00A0\u202F]")]
    private static partial Regex GroupSpaces();

    // Недопустимые в XML управляющие символы.
    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F]")]
    private static partial Regex ControlChars();

    // ---------- Запись XLSX ----------

    /// <summary>Имя листа: без []:*?/\, не длиннее 31 символа.</summary>
    internal static string SheetName(string stem)
    {
        var name = new string(stem.Where(c => c is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\')).ToArray()).Trim().Trim('\'');
        if (name.Length > 31) name = name[..31];
        return name.Length == 0 ? Loc.T.SheetName : name;
    }

    private const uint StyleDate = 1, StyleDateTime = 2, StylePercent = 3, StylePercentDecimals = 4;

    private static void Write(List<string[]> rows, string output, string sheetName, bool commaDecimal, CancellationToken ct)
    {
        using var doc = SpreadsheetDocument.Create(output, SpreadsheetDocumentType.Workbook);
        var workbook = doc.AddWorkbookPart();
        workbook.Workbook = new Workbook();

        var styles = workbook.AddNewPart<WorkbookStylesPart>();
        styles.Stylesheet = new Stylesheet(
            new Fonts(new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" })) { Count = 1 },
            new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
            new Borders(new Border(new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder())) { Count = 1 },
            new CellStyleFormats(new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 }) { Count = 1 },
            new CellFormats(
                new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 },
                new CellFormat { NumberFormatId = 14, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0, ApplyNumberFormat = true },
                new CellFormat { NumberFormatId = 22, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0, ApplyNumberFormat = true },
                new CellFormat { NumberFormatId = 9, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0, ApplyNumberFormat = true },
                new CellFormat { NumberFormatId = 10, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0, ApplyNumberFormat = true }) { Count = 5 },
            new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 });
        styles.Stylesheet.Save();

        var shared = workbook.AddNewPart<SharedStringTablePart>();
        var strings = new Dictionary<string, int>(StringComparer.Ordinal);
        var stringList = new List<string>();

        var sheet = workbook.AddNewPart<WorksheetPart>();
        var columns = rows.Count == 0 ? 0 : rows.Max(r => r.Length);
        var widths = new double[columns];

        using (var writer = OpenXmlWriter.Create(sheet))
        {
            writer.WriteStartElement(new Worksheet());
            // Ширина столбцов — по самому длинному значению (в символах), 8…60.
            foreach (var row in rows)
                for (var c = 0; c < row.Length; c++)
                    widths[c] = Math.Max(widths[c], LongestLine(row[c]));
            if (columns > 0)
            {
                writer.WriteStartElement(new Columns());
                for (var c = 0; c < columns; c++)
                {
                    var width = Math.Clamp(widths[c] + 2, 8, 60);
                    writer.WriteElement(new Column { Min = (uint)c + 1, Max = (uint)c + 1, Width = width, CustomWidth = true });
                }
                writer.WriteEndElement();
            }

            writer.WriteStartElement(new SheetData());
            for (var r = 0; r < rows.Count; r++)
            {
                if ((r & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
                var row = rows[r];
                writer.WriteStartElement(new Row { RowIndex = (uint)r + 1 });
                for (var c = 0; c < row.Length; c++)
                {
                    var raw = row[c];
                    if (raw.Length == 0) continue;
                    var reference = ColumnName(c) + (r + 1).ToString(CultureInfo.InvariantCulture);
                    var (kind, value) = Classify(raw, commaDecimal);
                    if (kind == Kind.Text)
                    {
                        var text = ControlChars().Replace(raw, "");
                        if (text.Length > MaxCellText) text = text[..MaxCellText];
                        if (!strings.TryGetValue(text, out var index))
                        {
                            index = stringList.Count;
                            strings[text] = index;
                            stringList.Add(text);
                        }
                        writer.WriteElement(new Cell
                        {
                            CellReference = reference,
                            DataType = CellValues.SharedString,
                            CellValue = new CellValue(index.ToString(CultureInfo.InvariantCulture)),
                        });
                    }
                    else
                    {
                        var cell = new Cell { CellReference = reference, CellValue = new CellValue(value.ToString("R", CultureInfo.InvariantCulture)) };
                        cell.StyleIndex = kind switch
                        {
                            Kind.Date => StyleDate,
                            Kind.DateTime => StyleDateTime,
                            Kind.Percent => raw.Contains('.') || raw.Contains(',') ? StylePercentDecimals : StylePercent,
                            _ => null,
                        };
                        writer.WriteElement(cell);
                    }
                }
                writer.WriteEndElement(); // row
            }
            writer.WriteEndElement(); // sheetData
            writer.WriteEndElement(); // worksheet
        }

        using (var writer = OpenXmlWriter.Create(shared))
        {
            writer.WriteStartElement(new SharedStringTable { Count = (uint)stringList.Count, UniqueCount = (uint)stringList.Count });
            foreach (var s in stringList)
            {
                var t = new Text(s);
                if (s.Length > 0 && (char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[^1]) || s.Contains('\n')))
                    t.Space = SpaceProcessingModeValues.Preserve;
                writer.WriteElement(new SharedStringItem(t));
            }
            writer.WriteEndElement();
        }

        workbook.Workbook.AppendChild(new Sheets(new Sheet { Id = workbook.GetIdOfPart(sheet), SheetId = 1, Name = sheetName }));
        workbook.Workbook.Save();
    }

    private static double LongestLine(string text)
    {
        var longest = 0;
        foreach (var line in text.AsSpan().EnumerateLines()) longest = Math.Max(longest, line.Length);
        return longest;
    }

    /// <summary>0 → A, 25 → Z, 26 → AA.</summary>
    internal static string ColumnName(int index)
    {
        var name = "";
        for (index++; index > 0; index = (index - 1) / 26)
            name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }
}
