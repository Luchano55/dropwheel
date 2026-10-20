using DropWheel.Conversions;

namespace DropWheel.Tests;

public class CatalogTests
{
    private static IEnumerable<string> Ids(IEnumerable<Operation> ops) => ops.Select(o => o.Id);

    [Fact]
    public void Ids_are_unique()
    {
        var ids = Ids(OperationCatalog.All).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Wheel_has_at_most_8_sectors_per_mode()
    {
        // Считаем по каждому расширению: операции «для любых файлов» (В ZIP) добавляются к операциям типа.
        foreach (var ext in Extensions.All.Append("txt"))
            foreach (var group in OperationCatalog.ForFile("file." + ext).GroupBy(o => o.Mode))
                Assert.True(group.Count() <= 8, $"{ext} {group.Key}: {group.Count()}");
    }

    [Fact]
    public void Wheel_has_at_most_8_sectors_per_mode_for_every_extension()
    {
        foreach (var ext in Extensions.All)
            foreach (var group in OperationCatalog.ForFile("file." + ext).GroupBy(o => o.Mode))
                Assert.True(group.Count() <= 8, $"{ext} {group.Key}: {group.Count()}");
    }

    [Fact]
    public void Heic_gets_all_image_conversions()
    {
        var ids = Ids(OperationCatalog.ForFile("photo.heic"));
        Assert.Equal(["to-jpg", "to-png", "to-webp", "to-avif", "to-pdf"],
            ids.Where(i => i.StartsWith("to-")));
        Assert.Contains("img-compress", ids);
    }

    [Theory]
    [InlineData("photo.jpg", "to-jpg")]
    [InlineData("photo.JPEG", "to-jpg")]
    [InlineData("song.mp3", "to-mp3")]
    [InlineData("clip.mp4", "to-mp4")]
    [InlineData("clip.webm", "to-webm")]
    public void Same_format_conversion_is_not_offered(string file, string hiddenOp)
    {
        Assert.DoesNotContain(hiddenOp, Ids(OperationCatalog.ForFile(file)));
    }

    [Fact]
    public void Same_format_tools_are_offered()
    {
        Assert.Contains("img-compress", Ids(OperationCatalog.ForFile("photo.jpg")));
        Assert.Contains("video-mute", Ids(OperationCatalog.ForFile("clip.mp4")));
    }

    [Theory]
    [InlineData("photo.HEIC", MediaKind.Image)]
    [InlineData("clip.MoV", MediaKind.Video)]
    [InlineData("song.opus", MediaKind.Audio)]
    public void Extension_case_does_not_matter(string file, MediaKind kind)
    {
        var ops = OperationCatalog.ForFile(file);
        Assert.Contains(ops, o => o.Kind == kind);
        Assert.All(ops, o => Assert.True(o.Kind == kind || o.Kind == MediaKind.Any, o.Id));
    }

    [Theory]
    [InlineData("data.bin")]
    [InlineData("noextension")]
    [InlineData(".gitignore")]
    public void Unknown_file_gets_only_zip(string file)
    {
        Assert.Equal(["zip-pack", "pack-7z"], Ids(OperationCatalog.ForFile(file)));
    }

    [Fact]
    public void Zip_archive_can_be_extracted()
    {
        var ids = Ids(OperationCatalog.ForFile("photos.ZIP")).ToList();
        Assert.Contains("zip-extract", ids);
        Assert.Contains("zip-pack", ids);
        Assert.DoesNotContain("zip-extract", Ids(OperationCatalog.ForFile("photo.jpg")));
    }

    [Fact]
    public void Zip_operations_shape()
    {
        var pack = OperationCatalog.Find("zip-pack")!;
        Assert.Equal((OperationMode.Tool, MediaKind.Any, InputMode.Combine, ResultKind.File, 1),
            (pack.Mode, pack.Kind, pack.Inputs, pack.Result, pack.MinFiles));
        var extract = OperationCatalog.Find("zip-extract")!;
        Assert.Equal((OperationMode.Tool, MediaKind.Archive, InputMode.PerFile, ResultKind.Folder, ""),
            (extract.Mode, extract.Kind, extract.Inputs, extract.Result, extract.Suffix));
    }

    [Fact]
    public void Several_images_get_common_operations()
    {
        var ids = Ids(OperationCatalog.ForFiles(["a.png", "b.heic"])).ToList();
        Assert.Contains("to-jpg", ids);
        Assert.DoesNotContain("to-png", ids); // a.png уже PNG

        Assert.DoesNotContain("to-jpg", Ids(OperationCatalog.ForFiles(["a.jpg", "b.heic"])));
    }

    [Fact]
    public void Mixed_types_have_only_zip_in_common()
    {
        Assert.Equal(["zip-pack", "pack-7z"], Ids(OperationCatalog.ForFiles(["a.png", "b.mp4", "c.txt"])));
        Assert.Empty(OperationCatalog.ForFiles(Array.Empty<string>()));
    }

    [Fact]
    public void Find_is_case_insensitive()
    {
        Assert.Equal("to-jpg", OperationCatalog.Find("TO-JPG")?.Id);
        Assert.Null(OperationCatalog.Find("to-nothing"));
    }

    [Theory]
    [InlineData("img-compress", "photo.png", "jpg")]
    [InlineData("img-compress", "photo.webp", "webp")]
    [InlineData("img-half", "photo.png", "png")]
    [InlineData("img-half", "photo.heic", "jpg")]
    [InlineData("video-mute", "clip.mkv", "mkv")]
    [InlineData("video-compress", "clip.mov", "mp4")]
    [InlineData("audio-normalize", "song.flac", "flac")]
    [InlineData("to-avif", "photo.png", "avif")]
    public void Output_extension(string opId, string input, string expected)
    {
        var op = OperationCatalog.Find(opId)!;
        Assert.Equal(expected, op.Output.Resolve(Path.GetExtension(input)));
    }

    // --- PDF ---

    [Fact]
    public void Single_pdf_gets_pages_split_optimize_but_not_merge()
    {
        var ids = Ids(OperationCatalog.ForFiles(["/docs/отчёт 1.PDF"])).ToList();
        Assert.Equal(["pdf-to-png", "pdf-to-jpg", "pdf-split", "pdf-optimize", "zip-pack", "pack-7z", "pdf-to-txt", "pdf-rotate", "pdf-to-docx"], ids);
    }

    [Fact]
    public void Two_pdfs_can_be_merged()
    {
        Assert.Contains("pdf-merge", Ids(OperationCatalog.ForFiles(["a.pdf", "b.pdf"])));
        Assert.Equal(["zip-pack", "pack-7z"], Ids(OperationCatalog.ForFiles(["a.pdf", "b.png"])));
    }

    [Fact]
    public void Several_images_can_become_one_pdf()
    {
        Assert.DoesNotContain("images-to-pdf", Ids(OperationCatalog.ForFiles(["a.jpg"])));
        Assert.Contains("images-to-pdf", Ids(OperationCatalog.ForFiles(["a.jpg", "b.heic", "c.png"])));
        Assert.Contains("to-pdf", Ids(OperationCatalog.ForFiles(["a.jpg"]))); // по одному — как раньше
    }

    [Theory]
    [InlineData("pdf-to-png", OperationMode.Convert, ResultKind.Folder, InputMode.PerFile, 1, " (страницы)")]
    [InlineData("pdf-to-jpg", OperationMode.Convert, ResultKind.Folder, InputMode.PerFile, 1, " (страницы)")]
    [InlineData("pdf-split", OperationMode.Tool, ResultKind.Folder, InputMode.PerFile, 1, " (по страницам)")]
    [InlineData("pdf-merge", OperationMode.Tool, ResultKind.File, InputMode.Combine, 2, " (объединено)")]
    [InlineData("pdf-optimize", OperationMode.Tool, ResultKind.File, InputMode.PerFile, 1, " (оптим.)")]
    [InlineData("images-to-pdf", OperationMode.Tool, ResultKind.File, InputMode.Combine, 2, " (все)")]
    [InlineData("pdf-to-txt", OperationMode.Convert, ResultKind.File, InputMode.PerFile, 1, "")]
    [InlineData("pdf-rotate", OperationMode.Tool, ResultKind.File, InputMode.PerFile, 1, " (повёрнуто)")]
    public void Pdf_operations_shape(string id, OperationMode mode, ResultKind result, InputMode inputs, int minFiles, string suffix)
    {
        var op = OperationCatalog.Find(id)!;
        Assert.Equal(mode, op.Mode);
        Assert.Equal(result, op.Result);
        Assert.Equal(inputs, op.Inputs);
        Assert.Equal(minFiles, op.MinFiles);
        Assert.Equal(suffix, op.Suffix);
        Assert.True(op.Label.Length <= 16, op.Label); // помещается в сектор колеса
    }

    // --- 7-Zip ---

    [Theory]
    [InlineData("архив.7z")]
    [InlineData("архив.RAR")]
    [InlineData("архив.tar")]
    [InlineData("архив.tar.gz")]
    [InlineData("архив.tgz")]
    [InlineData("архив.tar.bz2")]
    [InlineData("архив.tar.xz")]
    public void Archives_are_extracted_by_7zip_not_zip(string file)
    {
        var ids = Ids(OperationCatalog.ForFile(file)).ToList();
        Assert.Equal(["zip-pack", "archive-extract", "pack-7z"], ids);
    }

    [Fact]
    public void Zip_is_still_extracted_by_zip_engine()
    {
        var ops = OperationCatalog.ForFile("photos.zip");
        Assert.Contains(ops, o => o.Id == "zip-extract" && o.Engine == EngineKind.Zip);
        Assert.DoesNotContain(ops, o => o.Id == "archive-extract");
    }

    [Fact]
    public void Seven_zip_operations_shape()
    {
        var extract = OperationCatalog.Find("archive-extract")!;
        Assert.Equal((OperationMode.Tool, MediaKind.Archive, EngineKind.SevenZip, InputMode.PerFile, ResultKind.Folder, ""),
            (extract.Mode, extract.Kind, extract.Engine, extract.Inputs, extract.Result, extract.Suffix));
        Assert.Equal(["7z", "rar", "tar", "gz", "tgz", "bz2", "xz"], extract.InputExtensions!);

        var pack = OperationCatalog.Find("pack-7z")!;
        Assert.Equal((OperationMode.Tool, MediaKind.Any, EngineKind.SevenZip, InputMode.Combine, ResultKind.File, 1),
            (pack.Mode, pack.Kind, pack.Engine, pack.Inputs, pack.Result, pack.MinFiles));
        Assert.Equal("7z", pack.Output.Resolve(".txt"));
        // Имя архива — как у «В ZIP»: несколько файлов → по папке.
        Assert.Equal("Отпуск", pack.ResultStem!(["/фото/Отпуск/1.jpg", "/фото/Отпуск/2.jpg"]));
        Assert.Equal("1", pack.ResultStem!(["/фото/Отпуск/1.jpg"]));
    }

    [Fact]
    public void Labels_fit_the_wheel_sector()
    {
        Assert.All(OperationCatalog.All, o => Assert.True(o.Label.Length <= 16, o.Label));
    }

    // --- Документы без Office (DOCLIBS) ---

    [Theory]
    [InlineData("word-to-html", "HTML", MediaKind.Document, EngineKind.DocLibs, "html")]
    [InlineData("word-to-md", "Markdown", MediaKind.Document, EngineKind.DocLibs, "md")]
    [InlineData("csv-to-xlsx", "XLSX", MediaKind.Document, EngineKind.DocLibs, "xlsx")]
    [InlineData("md-to-html", "HTML", MediaKind.Text, EngineKind.DocLibs, "html")]
    [InlineData("text-to-pdf", "PDF", MediaKind.Text, EngineKind.WebPdf, "pdf")]
    public void Doclibs_operations_shape(string id, string label, MediaKind kind, EngineKind engine, string output)
    {
        var op = OperationCatalog.Find(id)!;
        Assert.Equal((label, OperationMode.Convert, kind, engine, output, ResultKind.File, InputMode.PerFile),
            (op.Label, op.Mode, op.Kind, op.Engine, op.Output.Fallback, op.Result, op.Inputs));
        Assert.True(op.Label.Length <= 16, op.Label);
    }

    [Fact]
    public void Docx_gets_html_and_markdown_but_old_doc_does_not()
    {
        var docx = Ids(OperationCatalog.ForFile("отчёт.DOCX")).ToList();
        Assert.Contains("word-to-html", docx);
        Assert.Contains("word-to-md", docx);
        foreach (var file in new[] { "old.doc", "letter.rtf", "text.odt" })
            Assert.DoesNotContain("word-to-md", Ids(OperationCatalog.ForFile(file)));
    }

    [Fact]
    public void Only_csv_gets_xlsx_from_csv()
    {
        Assert.Contains("csv-to-xlsx", Ids(OperationCatalog.ForFile("data.csv")));
        Assert.DoesNotContain("csv-to-xlsx", Ids(OperationCatalog.ForFile("data.xlsx")));
        // Один сектор «XLSX» для CSV, даже когда есть Office (его excel-to-xlsx CSV не берёт).
        Assert.Single(OperationCatalog.ForFile("data.csv"), o => o.Label == "XLSX");
    }

    [Theory]
    [InlineData("readme.md", new[] { "md-to-html", "text-to-pdf" })]
    [InlineData("readme.markdown", new[] { "md-to-html", "text-to-pdf" })]
    [InlineData("notes.txt", new[] { "text-to-pdf" })]
    [InlineData("page.html", new[] { "text-to-pdf" })]
    [InlineData("page.HTM", new[] { "text-to-pdf" })]
    public void Text_files_get_html_and_pdf(string file, string[] expected)
    {
        Assert.Equal(expected, Ids(OperationCatalog.ForFile(file).Where(o => o.Mode == OperationMode.Convert)));
    }

    [Fact]
    public void Pdf_from_text_is_hidden_without_edge()
    {
        // Конвертер без WebPdf (Linux, CLI): «PDF» для Markdown не предлагается, «HTML» — да.
        var converter = new Jobs.Converter(new Engines.ToolLocator(), ConversionSettings.Default, [new Engines.DocLibsEngine()]);
        var ids = Ids(OperationCatalog.ForFile("readme.md", converter.CanRun)).ToList();
        Assert.Contains("md-to-html", ids);
        Assert.DoesNotContain("text-to-pdf", ids);
    }
}
