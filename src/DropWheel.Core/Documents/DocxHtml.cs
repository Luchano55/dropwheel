using System.Globalization;
using System.Xml.Linq;
using Codeuctivity.OpenXmlPowerTools.OpenXMLWordprocessingMLToHtmlConverter;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DropWheel.Engines;

namespace DropWheel.Documents;

public sealed record DocxHtmlResult(string Html, PageSetup Page);

/// <summary>
/// DOCX → самодостаточный HTML (картинки внутри, data: URI) через OpenXmlPowerTools (WmlToHtmlConverter):
/// стили, шрифты, списки, таблицы, картинки. Плюс то, чего конвертер не делает:
/// размер листа и поля из документа (CSS @page), разрывы страниц и разделов.
/// Не переносятся: колонтитулы и номера страниц, сноски, надписи и фигуры, диаграммы, картинки EMF/WMF.
/// </summary>
public static class DocxHtml
{
    private static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string BreakMarker = "data-dropwheel-page-break";
    private static readonly HashSet<string> VoidElements = ["area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "wbr"];

    public static DocxHtmlResult Convert(string path, CancellationToken ct = default)
    {
        // Конвертер правит документ (принимает исправления и т. п.) — работаем с копией в памяти.
        using var stream = new MemoryStream();
        using (var source = TextFiles.OpenRead(path)) source.CopyTo(stream);
        stream.Position = 0;
        ct.ThrowIfCancellationRequested();

        using var doc = OpenWord(stream, editable: true);
        var main = doc.MainDocumentPart;
        if (main?.Document?.Body is null) throw new ConversionException("В файле Word нет текста документа", path);

        var page = ReadPageSetup(main.Document.Body);
        PrepareForHtml(main);
        ct.ThrowIfCancellationRequested();

        var title = Path.GetFileNameWithoutExtension(path);
        var settings = new WmlToHtmlConverterSettings(title,
            new ImageHandler(), new TextDummyHandler(), new SymbolHandler(), new PageBreakHandler(), new FontHandler(),
            fabricateCssClasses: true,
            generalCss: "span { white-space: pre-wrap; }",
            additionalCss: PageCss(page),
            cssClassPrefix: "pt-");

        XElement html;
        try
        {
            html = WmlToHtmlConverter.ConvertToHtml(doc, settings);
        }
        catch (Exception ex) when (ex is not ConversionException and not OperationCanceledException)
        {
            throw new ConversionException("Не удалось разобрать документ Word", ex.Message, ex);
        }

        ApplyPageBreaks(html);
        Tidy(html);
        return new DocxHtmlResult("<!DOCTYPE html>\n" + html.ToString(SaveOptions.DisableFormatting), page);
    }

    internal static WordprocessingDocument OpenWord(Stream stream, bool editable)
    {
        try
        {
            return WordprocessingDocument.Open(stream, editable, new OpenSettings { AutoSave = false });
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or IOException
                                       or FileFormatException or ArgumentException)
        {
            throw new ConversionException("Не удалось открыть документ Word: файл повреждён, защищён паролем или это не DOCX", ex.Message, ex);
        }
    }

    // ---------- Страница ----------

    private const double TwipsPerMm = 1440 / 25.4;

    /// <summary>Лист и поля первого раздела. Нет данных — A4 с полями 20 мм.</summary>
    public static PageSetup ReadPageSetup(Body body)
    {
        var section = body.Descendants<SectionProperties>().FirstOrDefault();
        var size = section?.GetFirstChild<PageSize>();
        var margin = section?.GetFirstChild<PageMargin>();
        double Mm(double? twips, double fallback) => twips is { } t ? Math.Abs(t) / TwipsPerMm : fallback;

        var width = Mm(size?.Width?.Value, 210);
        var height = Mm(size?.Height?.Value, 297);
        if (size?.Orient?.Value == PageOrientationValues.Landscape && width < height) (width, height) = (height, width);
        var gutter = Mm(margin?.Gutter?.Value, 0);
        return new PageSetup(width, height,
            Mm(margin?.Top?.Value, 20), Mm(margin?.Right?.Value, 20),
            Mm(margin?.Bottom?.Value, 20), Mm(margin?.Left?.Value, 20) + gutter).Sane();
    }

    /// <summary>На экране — «лист» шириной как в Word, при печати — поля из @page.</summary>
    private static string PageCss(PageSetup page) => page.CssPageRule + string.Create(CultureInfo.InvariantCulture, $$"""

        html { background: #fff; color-scheme: light; }
        body { margin: 0; padding: 0; }
        @media screen {
          html { background: #eceff1; }
          body { box-sizing: border-box; width: {{page.WidthMm:0.#}}mm; max-width: 100%; min-height: {{page.HeightMm:0.#}}mm;
                 margin: 16px auto; padding: {{page.TopMm:0.#}}mm {{page.RightMm:0.#}}mm {{page.BottomMm:0.#}}mm {{page.LeftMm:0.#}}mm;
                 background: #fff; box-shadow: 0 1px 4px rgba(0, 0, 0, .25); }
        }
        h1, h2, h3, h4, h5, h6 { break-after: avoid; }
        tr { break-inside: avoid; }
        """);

    // ---------- Подготовка документа ----------

    private static void PrepareForHtml(MainDocumentPart main)
    {
        // Конвертеру нужен раздел стилей, а его нет у документов из некоторых программ.
        if (main.StyleDefinitionsPart is null)
        {
            var part = main.AddNewPart<StyleDefinitionsPart>();
            part.Styles = new Styles();
            part.Styles.Save();
        }

        UnicodeBullets(main);

        var body = main.Document!.Body!;
        var styles = new StyleChain(main);

        // «С новой страницы» у абзаца (в т. ч. из стиля, как у заголовков по ГОСТу) → разрыв страницы перед ним.
        var first = true;
        foreach (var p in body.Elements<Paragraph>().ToList())
        {
            if (!first && styles.PageBreakBefore(p))
            {
                var pageBreak = new Run(new Break { Type = BreakValues.Page });
                if (p.ParagraphProperties is { } props) props.InsertAfterSelf(pageBreak);
                else p.PrependChild(pageBreak);
            }
            first = false;
        }

        // Разрыв раздела «со следующей страницы» → разрыв страницы в конце последнего абзаца раздела.
        var sections = body.Descendants<SectionProperties>().ToList();
        for (var i = 0; i + 1 < sections.Count; i++)
        {
            if (sections[i].Parent is not ParagraphProperties { Parent: Paragraph paragraph }) continue;
            var next = sections[i + 1].GetFirstChild<SectionType>()?.Val?.Value;
            if (next == SectionMarkValues.Continuous || next == SectionMarkValues.NextColumn) continue;
            paragraph.AppendChild(new Run(new Break { Type = BreakValues.Page }));
        }

        main.Document.Save();
    }

    // Маркеры списков Word — символы шрифтов Symbol/Wingdings (U+F0B7 и т. п.). Браузер их не покажет:
    // заменяем обычными символами Юникода, а шрифт маркера — обычным шрифтом абзаца.
    private static readonly HashSet<string> SymbolFonts =
        new(["Symbol", "Wingdings", "Wingdings 2", "Wingdings 3", "Webdings", "OpenSymbol"], StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, string> WingdingsBullets = new()
    {
        [0x6C] = "●", [0x6E] = "■", [0x71] = "❑", [0x76] = "❖", [0xA7] = "▪", [0xD8] = "➢", [0xFB] = "✗", [0xFC] = "✓",
    };

    private static void UnicodeBullets(MainDocumentPart main)
    {
        if (main.NumberingDefinitionsPart?.Numbering is not { } numbering) return;
        foreach (var level in numbering.Descendants<Level>())
        {
            if (level.NumberingFormat?.Val?.InnerText != "bullet" || level.LevelText?.Val?.Value is not { Length: 1 } text) continue;
            var fonts = level.NumberingSymbolRunProperties?.RunFonts;
            var font = fonts?.Ascii?.Value ?? fonts?.HighAnsi?.Value ?? "";
            var c = text[0];
            var symbolic = SymbolFonts.Contains(font);
            if (!symbolic && c is < '' or > '') continue;

            var code = c >= '' ? c - 0xF000 : c;
            level.LevelText.Val = font.StartsWith("Wingdings", StringComparison.OrdinalIgnoreCase)
                && WingdingsBullets.TryGetValue(code, out var bullet) ? bullet
                : font.Equals("Symbol", StringComparison.OrdinalIgnoreCase) && code == 0x2D ? "−"
                : "•";
            fonts?.Remove();
        }
        numbering.Save();
    }

    /// <summary>Стили абзацев: свойство «с новой страницы» по цепочке basedOn.</summary>
    private sealed class StyleChain(MainDocumentPart main)
    {
        private readonly Dictionary<string, Style> _styles =
            (main.StyleDefinitionsPart?.Styles?.Elements<Style>() ?? [])
            .Where(s => s.StyleId?.Value is not null)
            .GroupBy(s => s.StyleId!.Value!).ToDictionary(g => g.Key, g => g.First());
        private readonly Style? _default = main.StyleDefinitionsPart?.Styles?.Elements<Style>()
            .FirstOrDefault(s => s.Type?.Value == StyleValues.Paragraph && s.Default?.Value == true);

        public bool PageBreakBefore(Paragraph p)
        {
            if (p.ParagraphProperties?.PageBreakBefore is { } direct) return direct.Val?.Value ?? true;
            var style = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value is { } id && _styles.TryGetValue(id, out var s) ? s : _default;
            for (var depth = 0; style is not null && depth < 20; depth++)
            {
                if (style.StyleParagraphProperties?.PageBreakBefore is { } fromStyle) return fromStyle.Val?.Value ?? true;
                style = style.BasedOn?.Val?.Value is { } basedOn && _styles.TryGetValue(basedOn, out var b) ? b : null;
            }
            return false;
        }
    }

    /// <summary>Разрыв страницы → метка; остальные разрывы (строки, колонки) — как обычно.</summary>
    private sealed class PageBreakHandler : IBreakHandler
    {
        private static readonly BreakHandler Default = new();

        public IEnumerable<XNode> TransformBreak(XElement element) =>
            (string?)element.Attribute(W + "type") == "page"
                ? [new XElement(Xhtml + "span", new XAttribute(BreakMarker, "page"))]
                : Default.TransformBreak(element);
    }

    // ---------- Разрывы страниц в HTML ----------

    /// <summary>
    /// Абзац с меткой разрыва делится на два: всё после метки — в новый абзац с break-before: page.
    /// Внутри таблиц разрыв не ставится. Пустые половинки не остаются.
    /// </summary>
    internal static void ApplyPageBreaks(XElement html)
    {
        foreach (var marker in html.Descendants().Where(e => e.Attribute(BreakMarker) is not null).ToList())
        {
            var block = marker.Ancestors().FirstOrDefault(e => e.Name.LocalName is "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6");
            if (block is null || block.Ancestors().Any(a => a.Name.LocalName is "td" or "th"))
            {
                marker.Remove();
                continue;
            }

            var after = SplitAfter(block, marker);
            marker.Remove();
            if (HasContent(after))
            {
                AddStyle(after, "break-before: page");
                block.AddAfterSelf(after);
            }
            else if (NextBlock(block) is { } next)
            {
                // Разрыв в конце абзаца: с новой страницы начинается то, что идёт дальше (абзац, таблица, список).
                AddStyle(next, "break-before: page");
            }
            if (!HasContent(block)) block.Remove();
        }
    }

    /// <summary>Следующий элемент после block — его сосед или сосед ближайшего предка (до body).</summary>
    private static XElement? NextBlock(XElement block)
    {
        for (var e = block; e is not null && e.Name.LocalName != "body"; e = e.Parent)
            if (e.ElementsAfterSelf().FirstOrDefault() is { } next) return next;
        return null;
    }

    /// <summary>Перенести всё, что после node внутри block, в копию block (с копиями промежуточных элементов).</summary>
    private static XElement SplitAfter(XElement block, XElement node)
    {
        XElement? carried = null;
        while (true)
        {
            var parent = node.Parent!;
            var shell = new XElement(parent.Name, parent.Attributes());
            if (carried is not null) shell.Add(carried);
            foreach (var next in node.NodesAfterSelf().ToList())
            {
                next.Remove();
                shell.Add(next);
            }
            if (parent == block) return shell;
            carried = shell;
            node = parent;
        }
    }

    private static bool HasContent(XElement e) =>
        e.Descendants().Any(d => d.Name.LocalName is "img" or "svg" or "table")
        || e.Value.Any(c => !char.IsWhiteSpace(c) && c != '\u200e' && c != '\u200f');

    private static void AddStyle(XElement e, string css)
    {
        var current = (string?)e.Attribute("style");
        e.SetAttributeValue("style", string.IsNullOrEmpty(current) ? css : current.TrimEnd(';', ' ') + "; " + css);
    }

    /// <summary>
    /// Для HTML-разборщика браузера: &lt;span/&gt; — это открытый тег, а не пустой элемент.
    /// Пустым неодиночным элементам — явный закрывающий тег; xml:space убираем.
    /// </summary>
    private static void Tidy(XElement html)
    {
        foreach (var e in html.DescendantsAndSelf())
        {
            e.Attribute(XNamespace.Xml + "space")?.Remove();
            if (e.IsEmpty && !VoidElements.Contains(e.Name.LocalName)) e.Value = string.Empty;
        }
    }
}
