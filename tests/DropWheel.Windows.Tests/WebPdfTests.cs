using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DropWheel.AppEngines;
using DropWheel.Conversions;
using DropWheel.Documents;
using DropWheel.Engines;
using DropWheel.Jobs;

namespace DropWheel.Windows.Tests;

/// <summary>Поток интерфейса WPF с циклом сообщений — как в приложении: там живёт WebView2.</summary>
internal sealed class UiThread : IDisposable
{
    private readonly Thread _thread;

    public UiThread()
    {
        var ready = new TaskCompletionSource<Dispatcher>();
        _thread = new Thread(() =>
        {
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }) { IsBackground = true, Name = "UI" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        Dispatcher = ready.Task.Result;
    }

    public Dispatcher Dispatcher { get; }

    public void Dispose()
    {
        Dispatcher.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(10));
    }
}

/// <summary>
/// HTML → PDF через Edge (WebView2). Печать проверяется по-настоящему, если на машине есть WebView2 Runtime
/// (в Windows 11 и на windows-latest в CI — есть), иначе такие тесты пропускаются. Скриншоты не нужны.
/// </summary>
public sealed class WebPdfTests : IDisposable
{
    private const string NoRuntime = "Нет Microsoft Edge WebView2 Runtime";

    private static readonly Operation TextToPdf = OperationCatalog.Find("text-to-pdf")!;
    // Операция Office; без Office «Word → PDF» печатает Edge (в конвертере теста движка Office нет).
    private static readonly Operation WordToPdf = OperationCatalog.Find(WebPdfEngine.WordToPdfId)!;

    private readonly UiThread _ui = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dropwheel-тесты", Guid.NewGuid().ToString("N")[..8]);
    private readonly string _dir;
    private readonly WebPdfEngine _engine;
    private readonly Converter _converter;

    public WebPdfTests()
    {
        _dir = Path.Combine(_root, "Мои документы");
        Directory.CreateDirectory(_dir);
        _engine = new WebPdfEngine(_ui.Dispatcher, Path.Combine(_root, "WebView2"));
        _converter = new Converter(new ToolLocator(), ConversionSettings.Default, [new DocLibsEngine(), _engine]);
    }

    public void Dispose()
    {
        _engine.Dispose();
        _ui.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* профиль Edge может ещё быть занят */ }
    }

    private static string AssertPdf(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 1000, $"PDF слишком маленький: {bytes.Length} байт");
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        return Encoding.Latin1.GetString(bytes);
    }

    private void AssertNoTempLeft() => Assert.Empty(Directory.GetFileSystemEntries(_dir, ".dropwheel-*"));

    [Fact]
    public void Serves_only_docx_and_text_files()
    {
        // Без WebView2 Runtime не предлагает ничего; с ним — DOCX и текст, но не DOC/RTF/ODT (их без Office не прочитать).
        foreach (var ext in new[] { "doc", "rtf", "odt" })
        {
            Assert.False(_engine.CanRun(WordToPdf, ext), ext);
            Assert.Null(_converter.EngineFor(WordToPdf, ext));
        }
        Assert.False(_engine.CanRun(OperationCatalog.Find("to-jpg")!));
        Assert.False(_engine.CanRun(TextToPdf, "docx"));
        Assert.Equal(_engine.IsAvailable, _engine.CanRun(WordToPdf, "docx"));
        foreach (var ext in new[] { "txt", "md", "markdown", "html", "htm" })
            Assert.Equal(_engine.IsAvailable, _engine.CanRun(TextToPdf, ext));
        Assert.Equal(_engine.IsAvailable ? EngineKind.WebPdf : null, _converter.EngineFor(WordToPdf, "docx"));
        Assert.Equal(_engine.IsAvailable, _engine.RuntimeVersion is not null);
    }

    [SkippableFact]
    public async Task Markdown_to_pdf_with_relative_image()
    {
        Skip.IfNot(_engine.IsAvailable, NoRuntime);
        Directory.CreateDirectory(Path.Combine(_dir, "картинки"));
        File.WriteAllBytes(Path.Combine(_dir, "картинки", "квадрат.png"), Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGP8z8DAwMDAxMDAwMDAAAANHQEDasKb6QAAAABJRU5ErkJggg=="));
        var src = Path.Combine(_dir, "заметки 1.md");
        File.WriteAllText(src, """
            # Заголовок

            Текст с **жирным** и `кодом`.

            ![квадрат](картинки/квадрат.png)

            | Имя | Возраст |
            |-----|---------|
            | Анна | 30 |

            ```
            длинная строка кода, которая не должна вылезти за край страницы и должна перенестись на следующую строку внутри блока
            ```
            """);

        var result = await _converter.ConvertAsync(src, TextToPdf);

        Assert.Equal(Path.Combine(_dir, "заметки 1.pdf"), result);
        AssertPdf(result);
        AssertNoTempLeft();
    }

    [SkippableFact]
    public async Task Docx_to_pdf_without_office()
    {
        Skip.IfNot(_engine.IsAvailable, NoRuntime);
        var src = Path.Combine(_dir, "отчёт.docx");
        Docx(src);

        var result = await _converter.ConvertAsync(src, WordToPdf);

        Assert.Equal(Path.Combine(_dir, "отчёт.pdf"), result);
        var pdf = AssertPdf(result);
        // Разрыв страницы в документе → две страницы в PDF (дерево страниц Chromium пишет без сжатия).
        Assert.Matches(new Regex(@"/Count 2\b"), pdf);
        AssertNoTempLeft();
    }

    [SkippableFact]
    public async Task Html_and_txt_to_pdf_at_the_same_time()
    {
        Skip.IfNot(_engine.IsAvailable, NoRuntime);
        File.WriteAllText(Path.Combine(_dir, "стиль.css"), "h1 { color: #c00; }");
        var html = Path.Combine(_dir, "страница.html");
        File.WriteAllText(html, """
            <!DOCTYPE html><html><head><meta charset="utf-8"><link rel="stylesheet" href="стиль.css">
            <script>window.open('about:blank'); alert('не должно мешать');</script></head>
            <body><h1>Привет</h1><p>Страница с относительным стилем.</p></body></html>
            """);
        var txt = Path.Combine(_dir, "старый.txt");
        File.WriteAllBytes(txt, TextFiles.Windows1251.GetBytes("Текст в Windows-1251\r\n\tс табуляцией <и угловыми скобками>"));

        // Две задачи сразу (как очередь на 2 потока): Edge печатает их по очереди.
        var results = await Task.WhenAll(_converter.ConvertAsync(html, TextToPdf), _converter.ConvertAsync(txt, TextToPdf));

        Assert.Equal([Path.Combine(_dir, "страница.pdf"), Path.Combine(_dir, "старый.pdf")], results);
        foreach (var result in results) AssertPdf(result);
        AssertNoTempLeft();
    }

    [SkippableFact]
    public async Task Cancelled_job_leaves_nothing()
    {
        Skip.IfNot(_engine.IsAvailable, NoRuntime);
        var src = Path.Combine(_dir, "отмена.txt");
        File.WriteAllText(src, "текст");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _converter.ConvertAsync(src, TextToPdf, ct: cts.Token));

        Assert.False(File.Exists(Path.Combine(_dir, "отмена.pdf")));
        AssertNoTempLeft();
        // Движок после отмены работает дальше.
        AssertPdf(await _converter.ConvertAsync(src, TextToPdf));
    }

    [SkippableFact]
    public async Task Prepare_does_not_block_and_warms_up()
    {
        Skip.IfNot(_engine.IsAvailable, NoRuntime);
        var started = DateTime.UtcNow;
        await _ui.Dispatcher.InvokeAsync(() => _engine.Prepare([TextToPdf]));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1), "Prepare не должен ждать запуска Edge");

        var src = Path.Combine(_dir, "после прогрева.txt");
        File.WriteAllText(src, "текст");
        AssertPdf(await _converter.ConvertAsync(src, TextToPdf));
    }

    /// <summary>Заголовок, абзац, разрыв страницы, таблица — с кириллицей.</summary>
    private static void Docx(string path)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var styles = main.AddNewPart<StyleDefinitionsPart>();
        styles.Styles = new Styles(
            new Style(new StyleName { Val = "Normal" }) { Type = StyleValues.Paragraph, StyleId = "a", Default = true },
            new Style(new StyleName { Val = "heading 1" }, new StyleParagraphProperties(new OutlineLevel { Val = 0 }),
                new StyleRunProperties(new Bold(), new FontSize { Val = "32" })) { Type = StyleValues.Paragraph, StyleId = "1" });
        Run R(string text) => new(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        main.Document = new Document(new Body(
            new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "1" }), R("Отчёт")),
            new Paragraph(R("Первая страница: "), new Run(new RunProperties(new Bold()), new Text("жирный")), R(" текст.")),
            new Paragraph(new Run(new Break { Type = BreakValues.Page })),
            new Paragraph(R("Вторая страница.")),
            new Table(
                new TableProperties(new TableBorders(new TopBorder { Val = BorderValues.Single, Size = 4 },
                    new BottomBorder { Val = BorderValues.Single, Size = 4 })),
                new TableRow(new TableCell(new Paragraph(R("Имя"))), new TableCell(new Paragraph(R("Возраст")))),
                new TableRow(new TableCell(new Paragraph(R("Анна"))), new TableCell(new Paragraph(R("30"))))),
            new SectionProperties(new PageSize { Width = 11906, Height = 16838 },
                new PageMargin { Top = 1134, Right = 850, Bottom = 1134, Left = 1701 })));
    }
}
