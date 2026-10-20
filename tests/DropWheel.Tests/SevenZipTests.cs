using System.Formats.Tar;
using DropWheel.Conversions;
using DropWheel.Engines;

namespace DropWheel.Tests;

/// <summary>7-Zip и PDF → TXT без запуска утилит: имена, прогресс, сообщения об ошибках.</summary>
public class SevenZipTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData("/a/фото архив.7z", "фото архив")]
    [InlineData("/a/фото.архив.rar", "фото.архив")]
    [InlineData("/a/photos.tar", "photos")]
    [InlineData("/a/photos.tar.gz", "photos")]
    [InlineData("/a/photos.TAR.GZ", "photos")]
    [InlineData("/a/photos.tar.bz2", "photos")]
    [InlineData("/a/photos.tar.xz", "photos")]
    [InlineData("/a/photos.tgz", "photos")]
    [InlineData("/a/заметка.txt.gz", "заметка.txt")]
    [InlineData("/a/.tar.gz", ".tar")]
    [InlineData("/a/.7z", ".7z")]
    public void Folder_name_drops_archive_extensions(string archive, string expected)
    {
        Assert.Equal(expected, SevenZip.FolderName([archive]));
    }

    [Fact]
    public void Progress_is_read_between_backspaces()
    {
        var p = new SevenZipProgress();
        // Так 7-Zip пишет в stdout с -bsp1: строка, затем \b и пробелы, чтобы её стереть.
        Assert.Null(p.Read("  0M Scan\b\b\b\b\b\b\b\b\b         \b\b\b\b\b\b\b\b\b"));
        Assert.Equal(0.0, p.Read("  0%\b\b\b\b    \b\b\b\b"));
        Assert.Equal(0.43, p.Read(" 43% + zero.bin\b\b\b\b\b\b\b\b\b\b\b\b\b\b\b"));
        Assert.Equal(0.52, p.Read(" 52% 3 - zero.bin\b\b"));
    }

    [Fact]
    public void Progress_survives_chunk_split_inside_a_line()
    {
        var p = new SevenZipProgress();
        Assert.Null(p.Read(" 1"));
        Assert.Null(p.Read("7% + файл"));
        Assert.Equal(0.17, p.Read(".txt\b\b\b"));
        Assert.Equal(0.999, p.Read("100%\b\b\b\b")); // 1.0 — только когда 7-Zip завершился
        Assert.Null(p.Read("Everything is Ok\n"));
    }

    [Theory]
    [InlineData("ERROR: Data Error in encrypted file. Wrong password? : s.txt", "Архив с паролем не поддерживается")]
    [InlineData("ERROR: /x/pw.7z\nCannot open encrypted archive. Wrong password?\n\nERRORS:\nHeaders Error", "Архив с паролем не поддерживается")]
    [InlineData("ERROR: Dangerous link path was ignored : up : ..\nERROR: Cannot open output file : errno=2 : No such file or directory : /x/up/a",
        "Архив небезопасен: в нём ссылки за пределы папки")]
    [InlineData("Open ERROR: Cannot open the file as [7z] archive\n\nERRORS:\nIs not archive", "Файл повреждён или это не архив")]
    [InlineData("Open ERROR: Cannot open the file as [7z] archive\n\nERRORS:\nUnexpected end of archive", "Архив повреждён или не докачан")]
    [InlineData("ERROR:\nDuplicate filename on disk:\nc.txt\nc.txt", "В 7Z нельзя положить файлы с одинаковыми именами из разных папок")]
    [InlineData("ERROR: CRC Failed : a.txt", "Архив повреждён")]
    [InlineData("something new", "7-Zip не смог обработать архив")]
    public void Seven_zip_errors_are_translated(string stderr, string message)
    {
        Assert.Equal(message, ErrorTranslator.Translate(EngineKind.SevenZip, stderr));
    }

    [Fact]
    public void Seven_zip_phrases_do_not_leak_into_other_tools()
    {
        Assert.Equal("ffmpeg не смог обработать файл", ErrorTranslator.Translate(EngineKind.FFmpeg, "Data Error"));
        Assert.Equal("Не удалось прочитать PDF", ErrorTranslator.Translate(EngineKind.Pdftotext, "Syntax Error"));
        Assert.Equal("Автор PDF запретил копировать из него текст", ErrorTranslator.Translate(EngineKind.Pdftotext,
            "Permission Error: Copying of text from this document is not allowed."));
    }

    [Fact]
    public void Single_tar_is_recognized_by_ustar_signature()
    {
        var folder = Path.Combine(_dir.Path, "out");
        Directory.CreateDirectory(folder);
        var tar = Path.Combine(folder, "фото.tar");
        using (var stream = File.Create(tar))
        using (var writer = new TarWriter(stream, TarEntryFormat.Pax))
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "папка/"));
        Assert.Equal(tar, SevenZip.SingleTar(folder));

        File.WriteAllText(Path.Combine(folder, "ещё.txt"), "второй файл");
        Assert.Null(SevenZip.SingleTar(folder)); // не один файл

        File.Delete(tar);
        Assert.Null(SevenZip.SingleTar(folder)); // один, но не tar
    }

    [Theory]
    [InlineData("\f\f\f", false)]
    [InlineData("  \n\r\n\t\f ", false)]
    [InlineData("", false)]
    [InlineData("\f  1\f", true)]
    [InlineData("Привет", true)]
    public void Scanned_pdf_text_is_rejected(string text, bool hasText)
    {
        var path = _dir.File("текст.txt", text);
        if (hasText)
            PdfText.EnsureHasText(path);
        else
            Assert.Equal(PdfText.ScanMessage, Assert.Throws<ConversionException>(() => PdfText.EnsureHasText(path)).Message);
    }
}
