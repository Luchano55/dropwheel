using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace DropWheel.Tests.Documents;

/// <summary>Документы Word и Excel для тестов — собираются OpenXML SDK прямо в тесте (кириллица, списки, таблицы…).</summary>
internal static class TestDocs
{
    /// <summary>Красный квадрат 2×2 PNG.</summary>
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGP8z8DAwMDAxMDAwMDAAAANHQEDasKb6QAAAABJRU5ErkJggg==");

    /// <summary>
    /// Как в русском Word: у стилей заголовков идентификаторы «1», «2», а имена — «heading 1», «heading 2».
    /// Заголовок, абзац с жирным/курсивом, маркированный и нумерованный (с вложенным уровнем) списки, ссылка,
    /// картинка, разрыв страницы, таблица 2×2, абзац «1. …», набранный вручную, и спецсимволы Markdown.
    /// </summary>
    public static void Docx(string path, bool landscape = false)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;

        var styles = main.AddNewPart<StyleDefinitionsPart>();
        styles.Styles = new Styles(
            new DocDefaults(new RunPropertiesDefault(new RunPropertiesBaseStyle(
                new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman", ComplexScript = "Times New Roman" },
                new FontSize { Val = "28" }))),
            new Style(new StyleName { Val = "Normal" }) { Type = StyleValues.Paragraph, StyleId = "a", Default = true },
            new Style(new StyleName { Val = "heading 1" }, new BasedOn { Val = "a" },
                new StyleParagraphProperties(new KeepNext(), new OutlineLevel { Val = 0 }),
                new StyleRunProperties(new Bold(), new FontSize { Val = "36" })) { Type = StyleValues.Paragraph, StyleId = "1" },
            new Style(new StyleName { Val = "heading 2" }, new BasedOn { Val = "a" },
                new StyleParagraphProperties(new KeepNext(), new OutlineLevel { Val = 1 }),
                new StyleRunProperties(new Bold())) { Type = StyleValues.Paragraph, StyleId = "2" },
            new Style(new StyleName { Val = "Strong" }, new StyleRunProperties(new Bold())) { Type = StyleValues.Character, StyleId = "a3" });

        var numbering = main.AddNewPart<NumberingDefinitionsPart>();
        numbering.Numbering = new Numbering(
            new AbstractNum(
                new Level(new NumberingFormat { Val = NumberFormatValues.Bullet }, new LevelText { Val = "" },
                    new NumberingSymbolRunProperties(new RunFonts { Ascii = "Symbol", HighAnsi = "Symbol" })) { LevelIndex = 0 }) { AbstractNumberId = 1 },
            new AbstractNum(
                new Level(new StartNumberingValue { Val = 1 }, new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1." }) { LevelIndex = 0 },
                new Level(new StartNumberingValue { Val = 1 }, new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1.%2." }) { LevelIndex = 1 })
                { AbstractNumberId = 2 },
            new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 },
            new NumberingInstance(new AbstractNumId { Val = 2 }) { NumberID = 2 });

        Paragraph P(params OpenXmlElement[] content) => new(content);
        Run R(string text, params OpenXmlElement[] props) =>
            new(new RunProperties(props), new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        Paragraph Styled(string style, string text) => new(new ParagraphProperties(new ParagraphStyleId { Val = style }), R(text));
        Paragraph Item(int numId, int level, string text) => new(
            new ParagraphProperties(new NumberingProperties(new NumberingLevelReference { Val = level }, new NumberingId { Val = numId })), R(text));

        body.Append(Styled("1", "Отчёт о работе"));
        body.Append(P(R("Привет, "), R("жирный", new Bold()), R(" и "), R("курсив", new Italic()), R(" текст, "),
            new Run(new RunProperties(new RunStyle { Val = "a3" }), new Text("важное"))));
        body.Append(Styled("2", "Списки"));
        body.Append(Item(1, 0, "яблоко"));
        body.Append(Item(1, 0, "груша"));
        body.Append(Item(2, 0, "первый"));
        body.Append(Item(2, 1, "вложенный"));
        body.Append(Item(2, 0, "второй"));

        var link = main.AddHyperlinkRelationship(new Uri("https://example.com/путь"), isExternal: true);
        body.Append(P(R("Ссылка: "), new Hyperlink(R("сайт")) { Id = link.Id }));

        var image = main.AddImagePart(ImagePartType.Png);
        using (var png = new MemoryStream(Png)) image.FeedData(png);
        body.Append(P(new Run(Picture(main.GetIdOfPart(image), "красный квадрат"))));

        body.Append(P(R("до разрыва"), new Run(new Break { Type = BreakValues.Page }), R("после разрыва")));
        body.Append(P(R("1. Набрано вручную")));
        body.Append(P(R("Спецсимволы: <тег> & \"кавычки\" *звёздочки* [скобки]")));
        body.Append(new Table(
            new TableProperties(new TableBorders(new TopBorder { Val = BorderValues.Single, Size = 4 })),
            new TableRow(new TableCell(P(R("Имя"))), new TableCell(P(R("Возраст")))),
            new TableRow(new TableCell(P(R("Анна | Мария"))), new TableCell(P(R("30"))))));
        body.Append(P(R("Конец.")));

        body.Append(landscape
            ? new SectionProperties(new PageSize { Width = 16838, Height = 11906, Orient = PageOrientationValues.Landscape },
                new PageMargin { Top = 1134, Right = 1134, Bottom = 1134, Left = 1134 })
            : new SectionProperties(new PageSize { Width = 11906, Height = 16838 },
                new PageMargin { Top = 1134, Right = 850, Bottom = 1134, Left = 1701 }));
    }

    /// <summary>
    /// Разрывы: заголовок со стилем «с новой страницы» (как главы по ГОСТу; первая глава — в начале документа),
    /// разрыв раздела «со следующей страницы» и раздел «на текущей странице».
    /// </summary>
    public static void DocxWithBreaks(string path)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var styles = main.AddNewPart<StyleDefinitionsPart>();
        styles.Styles = new Styles(
            new Style(new StyleName { Val = "heading 1" },
                new StyleParagraphProperties(new PageBreakBefore(), new OutlineLevel { Val = 0 })) { Type = StyleValues.Paragraph, StyleId = "1" });
        Paragraph P(string text, params OpenXmlElement[] props) =>
            new(new ParagraphProperties(props), new Run(new Text(text)));
        main.Document = new Document(new Body(
            P("Глава 1", new ParagraphStyleId { Val = "1" }),
            P("текст 1"),
            P("Глава 2", new ParagraphStyleId { Val = "1" }),
            P("конец раздела 1", new SectionProperties()),
            P("Раздел 2"),
            P("конец раздела 2", new SectionProperties()),
            P("Раздел 3"),
            new SectionProperties(new SectionType { Val = SectionMarkValues.Continuous })));
    }

    private static Drawing Picture(string relationshipId, string description)
    {
        const long size = 914400; // 1 дюйм
        return new Drawing(new DW.Inline(
            new DW.Extent { Cx = size, Cy = size },
            new DW.DocProperties { Id = 1U, Name = "Рисунок 1", Description = description },
            new A.Graphic(new A.GraphicData(new PIC.Picture(
                new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = 0U, Name = "image.png" },
                    new PIC.NonVisualPictureDrawingProperties()),
                new PIC.BlipFill(new A.Blip { Embed = relationshipId }, new A.Stretch(new A.FillRectangle())),
                new PIC.ShapeProperties(new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = size, Cy = size }),
                    new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U });
    }

    /// <summary>
    /// Книга из двух листов: первый — скрытый (Excel не сохраняет его в CSV), второй — данные:
    /// текст с «;», кавычками и переводом строки, числа, дата (формат 14), проценты, деньги, логическое, ошибка, формула.
    /// </summary>
    public static void Xlsx(string path)
    {
        using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var wb = doc.AddWorkbookPart();
        wb.Workbook = new S.Workbook();
        var styles = wb.AddNewPart<WorkbookStylesPart>();
        styles.Stylesheet = new S.Stylesheet(
            new S.NumberingFormats(new S.NumberingFormat { NumberFormatId = 164, FormatCode = "#,##0.00\\ \"₽\"" }) { Count = 1 },
            new S.Fonts(new S.Font()) { Count = 1 },
            new S.Fills(new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }),
                new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 })) { Count = 2 },
            new S.Borders(new S.Border()) { Count = 1 },
            new S.CellFormats(
                new S.CellFormat(),
                new S.CellFormat { NumberFormatId = 14, ApplyNumberFormat = true },
                new S.CellFormat { NumberFormatId = 9, ApplyNumberFormat = true },
                new S.CellFormat { NumberFormatId = 164, ApplyNumberFormat = true },
                new S.CellFormat { NumberFormatId = 22, ApplyNumberFormat = true }) { Count = 5 });

        static S.Cell Str(string r, string s) => new() { CellReference = r, DataType = S.CellValues.InlineString, InlineString = new S.InlineString(new S.Text(s)) };
        static S.Cell Num(string r, string v, uint style = 0) => new() { CellReference = r, CellValue = new S.CellValue(v), StyleIndex = style };

        var hidden = wb.AddNewPart<WorksheetPart>();
        hidden.Worksheet = new S.Worksheet(new S.SheetData(new S.Row(Str("A1", "скрытый лист")) { RowIndex = 1 }));

        var data = wb.AddNewPart<WorksheetPart>();
        data.Worksheet = new S.Worksheet(new S.SheetData(
            new S.Row(Str("A1", "Имя"), Str("B1", "Сумма"), Str("C1", "Дата"), Str("D1", "Доля"), Str("E1", "Деньги")) { RowIndex = 1 },
            new S.Row(Str("A2", "Анна; \"Ш\"\nвторая строка"), Num("B2", "1234.5"), Num("C2", "45000", 1), Num("D2", "0.15", 2),
                Num("E2", "1234.5", 3), Num("F2", "45000.75", 4)) { RowIndex = 2 },
            new S.Row(Num("A3", "0.30000000000000004"),
                new S.Cell { CellReference = "B3", DataType = S.CellValues.Boolean, CellValue = new S.CellValue("1") },
                new S.Cell { CellReference = "C3", DataType = S.CellValues.Error, CellFormula = new S.CellFormula("1/0"), CellValue = new S.CellValue("#DIV/0!") },
                new S.Cell { CellReference = "D3", CellFormula = new S.CellFormula("B2*2"), CellValue = new S.CellValue("2469") }) { RowIndex = 3 },
            new S.Row { RowIndex = 6 }));

        wb.Workbook.AppendChild(new S.Sheets(
            new S.Sheet { Id = wb.GetIdOfPart(hidden), SheetId = 1, Name = "Служебный", State = S.SheetStateValues.Hidden },
            new S.Sheet { Id = wb.GetIdOfPart(data), SheetId = 2, Name = "Данные" }));
    }
}
