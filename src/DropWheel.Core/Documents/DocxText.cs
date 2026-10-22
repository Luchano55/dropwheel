using DropWheel.Localization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DropWheel.Engines;
using MathText = DocumentFormat.OpenXml.Math.Text;

namespace DropWheel.Documents;

/// <summary>
/// DOCX → простой текст или Markdown (без Office). Переносится основной текст: абзацы, заголовки,
/// списки с номерами, таблицы, ссылки, жирный/курсив/зачёркнутый, текст из надписей.
/// Колонтитулы, сноски и примечания не переносятся; картинка в Markdown — пометка «*[Рисунок]*».
/// </summary>
public static partial class DocxText
{
    public static string ToPlainText(string path, CancellationToken ct = default) => Convert(path, markdown: false, ct);

    public static string ToMarkdown(string path, CancellationToken ct = default) => Convert(path, markdown: true, ct);

    private static string Convert(string path, bool markdown, CancellationToken ct)
    {
        using var stream = new MemoryStream(TextFiles.ReadAllBytes(path), writable: false);
        using var doc = DocxHtml.OpenWord(stream, editable: false);
        var main = doc.MainDocumentPart;
        var body = main?.Document?.Body ?? throw new ConversionException(Loc.T.ErrWordNoBody, path);
        var styles = new StyleBook(main!);
        var writer = new Writer(main!, styles, new ListNumbering(main!, styles), markdown, ct);
        writer.Blocks(body);
        return writer.Result();
    }

    // ---------- Блоки: абзацы, списки, таблицы ----------

    private sealed record Block(string Text, bool ListItem, bool Empty, int ListDepth);

    private sealed class Writer(MainDocumentPart main, StyleBook styles, ListNumbering numbering, bool markdown, CancellationToken ct)
    {
        private readonly List<Block> _blocks = [];

        public IReadOnlyList<Block> Output => _blocks;

        public void Blocks(OpenXmlElement container)
        {
            foreach (var child in container.ChildElements)
            {
                ct.ThrowIfCancellationRequested();
                switch (child)
                {
                    case Paragraph p: Paragraph(p); break;
                    case Table t: Table(t); break;
                    case SdtBlock { SdtContentBlock: { } content }: Blocks(content); break;
                    case CustomXmlBlock x: Blocks(x); break;
                }
            }
        }

        public string Result()
        {
            if (!markdown)
                return string.Join("\r\n", _blocks.Select(b => b.Text.Replace("\n", "\r\n"))) + "\r\n";

            var sb = new StringBuilder();
            Block? previous = null;
            foreach (var block in _blocks.Where(b => !b.Empty))
            {
                if (previous is not null) sb.Append(previous.ListItem && block.ListItem ? "\n" : "\n\n");
                sb.Append(block.Text);
                previous = block;
            }
            return sb.Append('\n').ToString();
        }

        private void Add(string text, bool listItem = false, int depth = -1) =>
            _blocks.Add(new Block(text, listItem, string.IsNullOrWhiteSpace(text), depth));

        private int PreviousListDepth =>
            _blocks.Count > 0 && _blocks[^1].ListItem ? _blocks[^1].ListDepth : -1;

        private void Paragraph(Paragraph p)
        {
            var inline = new Inline(main, styles, markdown);
            inline.Walk(p);
            var text = (markdown ? inline.Markdown() : inline.Plain()).Trim('\n');
            var list = ListItem(p);
            var heading = markdown ? styles.HeadingLevel(p) : 0;

            if (heading > 0 && !string.IsNullOrWhiteSpace(text))
            {
                var number = list is { Ordered: true } ? list.Value.Marker + " " : "";
                Add(new string('#', heading) + " " + number + text.Replace("\\\n", " ").Trim());
            }
            else if (list is { } item && !(markdown && string.IsNullOrWhiteSpace(text)))
            {
                // Вложенность не больше чем на уровень глубже предыдущего пункта (иначе Markdown увидит код).
                var depth = Math.Min(item.Level, PreviousListDepth + 1);
                var marker = markdown ? (item.Ordered ? $"{item.Value}." : "-") : item.Marker;
                Add(new string(' ', depth * 4) + marker + " " + (markdown ? text.TrimStart() : text), listItem: true, depth);
            }
            else
            {
                Add(markdown ? EscapeLineStart(text.TrimStart()) : text);
            }

            foreach (var box in inline.TextBoxes) Blocks(box);
        }

        private (string Marker, bool Ordered, int Value, int Level)? ListItem(Paragraph p)
        {
            var direct = p.ParagraphProperties?.NumberingProperties;
            var numId = direct?.NumberingId?.Val?.Value;
            var level = direct?.NumberingLevelReference?.Val?.Value;
            if (numId is null)
            {
                var fromStyle = styles.Numbering(p);
                numId = fromStyle?.NumId;
                level ??= fromStyle?.Level;
            }
            if (numId is not { } id || id == 0) return null;
            var lvl = Math.Clamp(level ?? 0, 0, 8);
            return numbering.Next(id, lvl) is { } n ? (n.Marker, n.Ordered, n.Value, lvl) : null;
        }

        private void Table(Table table)
        {
            var rows = new List<List<string>>();
            var cells = new List<TableCell>();
            foreach (var row in RowsOf(table))
            {
                var texts = new List<string>();
                foreach (var cell in CellsOf(row))
                {
                    cells.Add(cell);
                    texts.Add(CellText(cell));
                    var span = cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1;
                    for (var i = 1; i < span; i++) texts.Add("");
                }
                rows.Add(texts);
            }
            if (rows.All(r => r.All(string.IsNullOrWhiteSpace))) return; // пустая сетка (например, для вёрстки)

            // Таблица из одной ячейки — обычно рамка вокруг текста: выводим её содержимое как есть.
            if (cells.Count == 1)
            {
                Blocks(cells[0]);
                return;
            }

            if (!markdown)
            {
                Add(string.Join("\n", rows.Select(r => string.Join("\t", r))));
                return;
            }

            var columns = rows.Max(r => r.Count);
            var sb = new StringBuilder();
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                while (row.Count < columns) row.Add("");
                sb.Append("| ").Append(string.Join(" | ", row)).Append(" |\n");
                if (i == 0) sb.Append('|').Append(string.Concat(Enumerable.Repeat(" --- |", columns))).Append('\n');
            }
            Add(sb.ToString().TrimEnd('\n'));
        }

        private static IEnumerable<TableRow> RowsOf(OpenXmlElement container)
        {
            foreach (var child in container.ChildElements)
            {
                if (child is TableRow row) yield return row;
                else if (child is SdtRow or CustomXmlRow or SdtContentRow)
                    foreach (var nested in RowsOf(child)) yield return nested;
            }
        }

        private static IEnumerable<TableCell> CellsOf(OpenXmlElement container)
        {
            foreach (var child in container.ChildElements)
            {
                if (child is TableCell cell) yield return cell;
                else if (child is SdtCell or CustomXmlCell or SdtContentCell)
                    foreach (var nested in CellsOf(child)) yield return nested;
            }
        }

        /// <summary>Ячейка — в одну строку: абзацы через &lt;br&gt; (Markdown) или пробел (текст).</summary>
        private string CellText(TableCell cell)
        {
            var sub = new Writer(main, styles, numbering, markdown, ct);
            sub.Blocks(cell);
            var parts = sub.Output.Where(b => !b.Empty).Select(b => markdown
                ? b.Text.Trim().Replace("\\\n", "<br>").Replace("\n", "<br>").Replace("|", "\\|")
                : b.Text.Replace('\n', ' ').Replace('\t', ' ').Trim());
            return string.Join(markdown ? "<br>" : " ", parts);
        }
    }

    // ---------- Текст внутри абзаца ----------

    [Flags]
    private enum Emphasis { None = 0, Bold = 1, Italic = 2, Strike = 4 }

    private sealed record Segment(string Text, Emphasis Style, string? Link, bool Raw = false);

    private sealed class Inline(MainDocumentPart main, StyleBook styles, bool markdown)
    {
        // Не текст документа: свойства, закладки, удалённое при правке, запасные варианты (mc:Fallback).
        private static readonly HashSet<string> Skip =
        [
            "pPr", "rPr", "del", "moveFrom", "bookmarkStart", "bookmarkEnd", "commentRangeStart", "commentRangeEnd",
            "proofErr", "permStart", "permEnd", "sdtPr", "sdtEndPr", "Fallback", "ctrlPr", "fldData", "rt",
        ];

        private readonly List<Segment> _segments = [];
        public List<OpenXmlElement> TextBoxes { get; } = [];

        public void Walk(OpenXmlElement element, string? link = null)
        {
            foreach (var child in element.ChildElements)
            {
                switch (child)
                {
                    case Run run: RunContent(run, link); break;
                    case Hyperlink h: Walk(h, LinkTarget(h) ?? link); break;
                    case MathText t: Add(t.Text, Emphasis.None, link); break;
                    default:
                        if (!Skip.Contains(child.LocalName)) Walk(child, link);
                        break;
                }
            }
        }

        private string? LinkTarget(Hyperlink h) =>
            h.Id?.Value is { } id ? main.HyperlinkRelationships.FirstOrDefault(r => r.Id == id)?.Uri.OriginalString : null;

        private void Add(string text, Emphasis style, string? link, bool raw = false)
        {
            if (text.Length > 0) _segments.Add(new Segment(text, style, link, raw));
        }

        private void RunContent(Run run, string? link)
        {
            var style = RunStyle(run);
            foreach (var c in run.ChildElements)
            {
                switch (c)
                {
                    case Text t: Add(t.Text, style, link); break;
                    case TabChar or PositionalTab: Add("\t", style, link); break;
                    case Break or CarriageReturn: Add("\n", Emphasis.None, null); break;
                    case NoBreakHyphen: Add("-", style, link); break;
                    case SymbolChar sym:
                        if (int.TryParse(sym.Char?.Value, System.Globalization.NumberStyles.HexNumber, null, out var code)
                            && code is > 0x1F and < 0xE000)
                            Add(char.ConvertFromUtf32(code), style, link);
                        break;
                    case Drawing or Picture or EmbeddedObject or AlternateContent: Graphic(c); break;
                }
            }
        }

        private Emphasis RunStyle(Run run)
        {
            var props = run.RunProperties;
            var style = Emphasis.None;
            if (On(props?.Bold) ?? styles.RunFlag(props, s => s.Bold) ?? false) style |= Emphasis.Bold;
            if (On(props?.Italic) ?? styles.RunFlag(props, s => s.Italic) ?? false) style |= Emphasis.Italic;
            if ((On(props?.Strike) ?? false) || (On(props?.DoubleStrike) ?? false)) style |= Emphasis.Strike;
            return style;
        }

        /// <summary>Картинки и надписи: текст надписей — отдельными абзацами после текущего.</summary>
        private void Graphic(OpenXmlElement graphic)
        {
            foreach (var box in graphic.Descendants().Where(d => d.LocalName == "txbxContent"))
            {
                var path = box.Ancestors().TakeWhile(a => a != graphic).ToList();
                if (path.Any(a => a.LocalName is "Fallback" or "txbxContent")) continue;
                TextBoxes.Add(box);
            }

            if (!markdown) return;
            var images = graphic.Descendants().Where(d => d.LocalName is "blip" or "imagedata"
                && !d.Ancestors().TakeWhile(a => a != graphic).Any(a => a.LocalName == "Fallback")).ToList();
            if (images.Count == 0) return;
            var alt = graphic.Descendants().Where(d => d.LocalName == "docPr")
                .Select(d => d.GetAttributes().FirstOrDefault(a => a.LocalName == "descr").Value)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            Add(alt is null ? $"*[{Loc.T.MdPicture}]*" : $"*[{Loc.T.MdPicture}: {EscapeInline(alt.Replace('\n', ' ').Trim())}]*", Emphasis.None, null, raw: true);
        }

        public string Plain() => string.Concat(Trimmed().Where(s => !s.Raw).Select(s => s.Text));

        /// <summary>
        /// Пустые куски по краям абзаца долой, если среди них разрыв строки: разрыв страницы перед текстом —
        /// не пустая строка. В Markdown пробелы по краям не нужны вовсе, в тексте отступ пробелами сохраняется.
        /// </summary>
        private List<Segment> Trimmed()
        {
            static bool Blank(Segment s) => !s.Raw && string.IsNullOrWhiteSpace(s.Text);
            var start = 0;
            while (start < _segments.Count && Blank(_segments[start])) start++;
            if (!markdown && !_segments.Take(start).Any(s => s.Text.Contains('\n'))) start = 0;
            var end = _segments.Count;
            while (end > start && Blank(_segments[end - 1])) end--;
            if (!markdown && !_segments.Skip(end).Any(s => s.Text.Contains('\n'))) end = _segments.Count;
            return _segments.GetRange(start, end - start);
        }

        public string Markdown()
        {
            var segments = Trimmed();
            var sb = new StringBuilder();
            var i = 0;
            while (i < segments.Count)
            {
                var first = segments[i];
                var text = new StringBuilder(first.Text);
                var j = i + 1;
                // Склеиваем соседние куски с одинаковым оформлением; пробелы оформления не имеют.
                while (j < segments.Count && !first.Raw && !segments[j].Raw && segments[j].Link == first.Link
                       && (segments[j].Style == first.Style || string.IsNullOrWhiteSpace(segments[j].Text)))
                    text.Append(segments[j++].Text);
                sb.Append(first.Raw ? first.Text : Format(text.ToString(), first.Style, first.Link));
                i = j;
            }
            return sb.ToString();
        }

        private static string Format(string text, Emphasis style, string? link)
        {
            var escaped = EscapeInline(text).Replace('\t', ' ').Replace("\n", "\\\n");
            var core = escaped.Trim(' ', '\u00A0');
            if (core.Length == 0) return escaped;
            var lead = escaped[..escaped.IndexOf(core, StringComparison.Ordinal)];
            var trail = escaped[(lead.Length + core.Length)..];

            var open = (style.HasFlag(Emphasis.Strike) ? "~~" : "")
                + ((style & (Emphasis.Bold | Emphasis.Italic)) switch
                {
                    Emphasis.Bold | Emphasis.Italic => "***",
                    Emphasis.Bold => "**",
                    Emphasis.Italic => "*",
                    _ => "",
                });
            var close = new string(open.Reverse().ToArray());
            var inner = open + core + close;
            if (link is not null)
                inner = "[" + inner + "](" + link.Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29") + ")";
            return lead + inner + trail;
        }
    }

    private static bool? On(OnOffType? value) => value is null ? null : value.Val?.Value ?? true;

    // ---------- Экранирование Markdown ----------

    private static string EscapeInline(string text) => InlineSpecial().Replace(text, @"\$0");

    /// <summary>Абзац, который начинается как заголовок/цитата/список, остаётся обычным абзацем.</summary>
    private static string EscapeLineStart(string text)
    {
        if (text.Length == 0) return text;
        if (text[0] is '#' or '>' or '-' or '+' or '=') return "\\" + text;
        return OrderedStart().Replace(text, @"$1\$2", 1);
    }

    [GeneratedRegex(@"[\\`*_\[\]<~]")]
    private static partial Regex InlineSpecial();

    [GeneratedRegex(@"^(\d+)([.)])")]
    private static partial Regex OrderedStart();

    // ---------- Стили ----------

    private sealed record StyleInfo(string? Name, string? BasedOn, int? OutlineLevel, int? NumId, int? NumLevel,
        bool? Bold, bool? Italic);

    /// <summary>Стили документа: заголовки (по имени «heading N» или уровню структуры), списки и жирный/курсив из стиля.</summary>
    private sealed partial class StyleBook
    {
        private readonly Dictionary<string, StyleInfo> _byId = new(StringComparer.Ordinal);
        private readonly string? _defaultParagraph;

        public StyleBook(MainDocumentPart main)
        {
            foreach (var s in main.StyleDefinitionsPart?.Styles?.Elements<Style>() ?? [])
            {
                if (s.StyleId?.Value is not { } id || _byId.ContainsKey(id)) continue;
                var ppr = s.StyleParagraphProperties;
                var rpr = s.StyleRunProperties;
                _byId[id] = new StyleInfo(s.StyleName?.Val?.Value, s.BasedOn?.Val?.Value, ppr?.OutlineLevel?.Val?.Value,
                    ppr?.NumberingProperties?.NumberingId?.Val?.Value, ppr?.NumberingProperties?.NumberingLevelReference?.Val?.Value,
                    On(rpr?.Bold), On(rpr?.Italic));
                if (s.Type?.Value == StyleValues.Paragraph && s.Default?.Value == true) _defaultParagraph = id;
            }
        }

        private IEnumerable<StyleInfo> Chain(string? id)
        {
            var current = id;
            for (var depth = 0; current is not null && depth < 20 && _byId.TryGetValue(current, out var style); depth++)
            {
                yield return style;
                current = style.BasedOn;
            }
        }

        private IEnumerable<StyleInfo> ParagraphChain(Paragraph p) =>
            Chain(p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? _defaultParagraph);

        /// <summary>1…6 — заголовок, 0 — обычный абзац.</summary>
        public int HeadingLevel(Paragraph p)
        {
            if (p.ParagraphProperties?.OutlineLevel?.Val?.Value is { } direct) return direct < 9 ? Math.Min(direct + 1, 6) : 0;
            foreach (var style in ParagraphChain(p))
            {
                var name = style.Name?.Trim().ToLowerInvariant();
                if (name == "title") return 1;
                if (name is not null && HeadingName().Match(name) is { Success: true } m) return Math.Min(int.Parse(m.Groups[1].Value), 6);
                if (style.OutlineLevel is { } level) return level < 9 ? Math.Min(level + 1, 6) : 0;
            }
            return 0;
        }

        public (int? NumId, int? Level)? Numbering(Paragraph p)
        {
            foreach (var style in ParagraphChain(p))
                if (style.NumId is not null) return (style.NumId, style.NumLevel);
            return null;
        }

        public bool? RunFlag(RunProperties? props, Func<StyleInfo, bool?> flag)
        {
            foreach (var style in Chain(props?.RunStyle?.Val?.Value))
                if (flag(style) is { } value) return value;
            return null;
        }

        public int? NumIdOfStyle(string styleId) => Chain(styleId).Select(s => s.NumId).FirstOrDefault(n => n is not null);

        [GeneratedRegex(@"^heading\s*([1-9])$")]
        private static partial Regex HeadingName();
    }

    // ---------- Нумерация списков ----------

    private sealed record LevelDef(string Format, string Text, int Start);

    /// <summary>
    /// Номера пунктов, как их считает Word: счётчик общий для всех списков с одним abstractNum,
    /// startOverride начинает нумерацию заново, пункт верхнего уровня сбрасывает вложенные.
    /// </summary>
    private sealed class ListNumbering
    {
        private readonly Dictionary<int, Dictionary<int, LevelDef>> _abstract = [];
        private readonly Dictionary<int, (int AbstractId, Dictionary<int, int> Starts, Dictionary<int, LevelDef> Levels)> _nums = [];
        private readonly Dictionary<int, int?> _styleLinks = [];
        private readonly Dictionary<int, int?[]> _counters = [];
        private readonly HashSet<int> _restarted = [];

        public ListNumbering(MainDocumentPart main, StyleBook styles)
        {
            var numbering = main.NumberingDefinitionsPart?.Numbering;
            if (numbering is null) return;

            foreach (var a in numbering.Elements<AbstractNum>())
            {
                if (a.AbstractNumberId?.Value is not { } id) continue;
                _abstract[id] = Levels(a.Elements<Level>());
                // Список, заданный стилем списка: уровни — у того abstractNum, на который ссылается стиль.
                if (a.GetFirstChild<NumberingStyleLink>()?.Val?.Value is { } link)
                    _styleLinks[id] = styles.NumIdOfStyle(link);
            }
            foreach (var n in numbering.Elements<NumberingInstance>())
            {
                if (n.NumberID?.Value is not { } id || n.AbstractNumId?.Val?.Value is not { } abstractId) continue;
                var starts = new Dictionary<int, int>();
                var levels = new Dictionary<int, LevelDef>();
                foreach (var o in n.Elements<LevelOverride>())
                {
                    if (o.LevelIndex?.Value is not { } lvl) continue;
                    if (o.StartOverrideNumberingValue?.Val?.Value is { } start) starts[lvl] = start;
                    if (o.Level is { } level && Levels([level]).TryGetValue(lvl, out var def)) levels[lvl] = def;
                }
                _nums[id] = (abstractId, starts, levels);
            }
        }

        private static Dictionary<int, LevelDef> Levels(IEnumerable<Level> levels)
        {
            var result = new Dictionary<int, LevelDef>();
            foreach (var l in levels)
                if (l.LevelIndex?.Value is { } i)
                    result[i] = new LevelDef(l.NumberingFormat?.Val?.InnerText ?? "decimal", l.LevelText?.Val?.Value ?? "",
                        l.StartNumberingValue?.Val?.Value ?? 1);
            return result;
        }

        private Dictionary<int, LevelDef>? AbstractLevels(int abstractId, int depth = 0)
        {
            if (_styleLinks.TryGetValue(abstractId, out var linkedNum) && linkedNum is { } numId && depth < 3
                && _nums.TryGetValue(numId, out var linked) && linked.AbstractId != abstractId)
                return AbstractLevels(linked.AbstractId, depth + 1);
            return _abstract.GetValueOrDefault(abstractId);
        }

        public (string Marker, bool Ordered, int Value)? Next(int numId, int level)
        {
            if (!_nums.TryGetValue(numId, out var num)) return null;
            var abstractLevels = AbstractLevels(num.AbstractId) ?? [];
            LevelDef? Def(int l) => num.Levels.GetValueOrDefault(l) ?? abstractLevels.GetValueOrDefault(l);
            int Start(int l) => num.Starts.TryGetValue(l, out var s) ? s : Def(l)?.Start ?? 1;

            if (Def(level) is not { } def || def.Format == "none") return null;

            var counters = _counters.TryGetValue(num.AbstractId, out var c) ? c : _counters[num.AbstractId] = new int?[9];
            if (num.Starts.Count > 0 && _restarted.Add(numId))
                foreach (var l in num.Starts.Keys.Where(l => l is >= 0 and < 9)) counters[l] = null;

            counters[level] = counters[level] is { } current ? current + 1 : Start(level);
            for (var deeper = level + 1; deeper < 9; deeper++) counters[deeper] = null;

            if (def.Format == "bullet")
            {
                var bullet = def.Text.Length == 1 && def.Text[0] < '' && !char.IsControl(def.Text[0]) ? def.Text : "•";
                return (bullet, false, 0);
            }

            var marker = Regex.Replace(def.Text, "%([1-9])", m =>
            {
                var l = m.Groups[1].Value[0] - '1';
                return FormatNumber(counters[l] ?? Start(l), Def(l)?.Format ?? "decimal");
            });
            return (marker, true, counters[level]!.Value);
        }
    }

    private const string RussianLetters = "абвгдежзиклмнопрстуфхцчшщэюя";

    internal static string FormatNumber(int value, string format) => format switch
    {
        "decimalZero" => value is >= 0 and < 10 ? "0" + value : value.ToString(),
        "upperRoman" => Roman(value),
        "lowerRoman" => Roman(value).ToLowerInvariant(),
        "upperLetter" => Letters(value, "abcdefghijklmnopqrstuvwxyz").ToUpperInvariant(),
        "lowerLetter" => Letters(value, "abcdefghijklmnopqrstuvwxyz"),
        "russianUpper" => Letters(value, RussianLetters).ToUpperInvariant(),
        "russianLower" => Letters(value, RussianLetters),
        _ => value.ToString(),
    };

    /// <summary>Как в Word: a…z, затем aa…zz, aaa…</summary>
    private static string Letters(int value, string alphabet)
    {
        if (value < 1) return value.ToString();
        var index = (value - 1) % alphabet.Length;
        var repeat = (value - 1) / alphabet.Length + 1;
        return new string(alphabet[index], repeat);
    }

    private static string Roman(int value)
    {
        if (value is < 1 or > 3999) return value.ToString();
        ReadOnlySpan<int> numbers = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] symbols = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
        var sb = new StringBuilder();
        for (var i = 0; i < numbers.Length; i++)
            while (value >= numbers[i]) { sb.Append(symbols[i]); value -= numbers[i]; }
        return sb.ToString();
    }
}
