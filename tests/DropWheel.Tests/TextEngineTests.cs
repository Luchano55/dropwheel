using System.Text;
using DropWheel.Cli;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;

namespace DropWheel.Tests;

public class TextEngineTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly Converter _converter = new(new ToolLocator(), ConversionSettings.Default, [new TextEngine()]);
    private static readonly Operation ToUtf8 = OperationCatalog.Find(TextEngine.ToUtf8Id)!;

    private static readonly Encoding Cp1251 = CodePagesEncodingProvider.Instance.GetEncoding(1251)!;
    private static readonly Encoding Cp866 = CodePagesEncodingProvider.Instance.GetEncoding(866)!;
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private const string Pangram = "Съешь же ещё этих мягких французских булок, да выпей чаю.\r\nВторая строка — №1.";

    public void Dispose() => _dir.Dispose();

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir.Path, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private async Task<byte[]> Convert(string path)
    {
        var source = File.ReadAllBytes(path);
        var result = await _converter.ConvertAsync(path, ToUtf8);
        Assert.Equal(source, File.ReadAllBytes(path)); // исходник не тронут
        Assert.Empty(Directory.GetFiles(_dir.Path, ".dropwheel-*"));
        return File.ReadAllBytes(result);
    }

    private static string Utf8WithBom(byte[] bytes)
    {
        Assert.Equal(Bom, bytes.Take(3));
        return new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3);
    }

    [Theory]
    [InlineData(1251, Pangram)]
    [InlineData(866, Pangram)]
    [InlineData(1251, "Привет, мир")]
    [InlineData(866, "Привет, мир")]
    [InlineData(1251, "ПРИВЕТ МИР, ЗАГЛАВНЫЕ БУКВЫ")]
    [InlineData(866, "ПРИВЕТ МИР, ЗАГЛАВНЫЕ БУКВЫ")]
    [InlineData(1251, "Отчёт")]
    [InlineData(866, "Отчёт")]
    [InlineData(1251, "user=Иван\r\npath=C:\\Документы\\отчёт.docx\r\n")]
    [InlineData(866, "@echo off\r\necho Копирование завершено\r\npause\r\n")]
    public async Task Single_byte_russian_is_detected_and_converted(int codePage, string text)
    {
        var encoding = codePage == 866 ? Cp866 : Cp1251;
        var src = Write("заметки 1.txt", encoding.GetBytes(text));
        var bytes = await Convert(src);
        // В 866 нет «—»: кодировка сама заменила его на «-».
        Assert.Equal(encoding.GetString(encoding.GetBytes(text)), Utf8WithBom(bytes));
        Assert.True(File.Exists(Path.Combine(_dir.Path, "заметки 1 (UTF-8).txt")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Utf16_with_bom_is_converted(bool bigEndian)
    {
        var encoding = new UnicodeEncoding(bigEndian, byteOrderMark: true);
        var src = Write("субтитры.srt", [.. encoding.GetPreamble(), .. encoding.GetBytes(Pangram)]);
        var bytes = await Convert(src);
        Assert.Equal(Pangram, Utf8WithBom(bytes));
        Assert.True(File.Exists(Path.Combine(_dir.Path, "субтитры (UTF-8).srt")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Already_utf8_is_refused(bool bom)
    {
        var body = Encoding.UTF8.GetBytes(Pangram);
        var src = Write("уже.txt", bom ? [.. Bom, .. body] : body);
        var ex = await Assert.ThrowsAsync<ConversionException>(() => _converter.ConvertAsync(src, ToUtf8));
        Assert.Equal("Файл уже в UTF-8", ex.Message);
        Assert.Single(Directory.GetFiles(_dir.Path)); // ничего не создано
    }

    [Fact]
    public async Task Plain_ascii_is_already_utf8()
    {
        var src = Write("english.log", Encoding.ASCII.GetBytes("plain text\r\n"));
        var ex = await Assert.ThrowsAsync<ConversionException>(() => _converter.ConvertAsync(src, ToUtf8));
        Assert.Equal("Файл уже в UTF-8", ex.Message);
    }

    [Fact]
    public async Task Binary_file_is_refused()
    {
        var src = Write("данные.log", [0x00, 0x01, 0xC0, 0xFF, 0x00, 0x10]);
        var ex = await Assert.ThrowsAsync<ConversionException>(() => _converter.ConvertAsync(src, ToUtf8));
        Assert.Contains("не текст", ex.Message);
        Assert.Single(Directory.GetFiles(_dir.Path));
    }

    [Fact]
    public async Task Csv_keeps_extension_and_gets_bom_for_excel()
    {
        var src = Write("таблица.csv", Cp1251.GetBytes("Имя;Город\r\nИван;Москва\r\n"));
        var bytes = await Convert(src);
        Assert.Equal("Имя;Город\r\nИван;Москва\r\n", Utf8WithBom(bytes));
        Assert.True(File.Exists(Path.Combine(_dir.Path, "таблица (UTF-8).csv")));
    }

    [Fact]
    public async Task Json_gets_no_bom()
    {
        var src = Write("data.json", Cp1251.GetBytes("{\"город\": \"Москва\"}"));
        var bytes = await Convert(src);
        Assert.Equal("{\"город\": \"Москва\"}", Encoding.UTF8.GetString(bytes));
        Assert.NotEqual(0xEF, bytes[0]);
    }

    [Fact]
    public async Task Xml_declaration_is_updated()
    {
        var src = Write("книга.xml", Cp1251.GetBytes("<?xml version=\"1.0\" encoding=\"windows-1251\"?>\n<книга>Война и мир</книга>"));
        var text = Utf8WithBom(await Convert(src));
        Assert.Equal("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<книга>Война и мир</книга>", text);
    }

    [Theory]
    [InlineData("<?xml version='1.0' encoding='cp1251'?><a/>", "<?xml version='1.0' encoding='utf-8'?><a/>")]
    [InlineData("<?xml version=\"1.0\"?><a encoding=\"x\"/>", "<?xml version=\"1.0\"?><a encoding=\"x\"/>")]
    [InlineData("<a/><?xml encoding=\"x\"?>", "<a/><?xml encoding=\"x\"?>")]
    public void Xml_declaration_only_in_prolog(string input, string expected)
    {
        Assert.Equal(expected, TextEngine.FixXmlDeclaration(input));
    }

    [Fact]
    public void Score_prefers_the_right_code_page()
    {
        var in1251 = Cp1251.GetBytes(Pangram);
        var in866 = Cp866.GetBytes(Pangram);
        Assert.True(TextEngine.Score(in1251, Cp1251) > TextEngine.Score(in1251, Cp866));
        Assert.True(TextEngine.Score(in866, Cp866) > TextEngine.Score(in866, Cp1251));
    }

    // --- Каталог ---

    [Theory]
    [InlineData("заметки.txt")]
    [InlineData("table.CSV")]
    [InlineData("фильм.srt")]
    [InlineData("readme.md")]
    [InlineData("settings.ini")]
    [InlineData("app.log")]
    [InlineData("data.xml")]
    [InlineData("data.json")]
    public void Offered_for_text_files(string file)
    {
        Assert.Contains(TextEngine.ToUtf8Id, OperationCatalog.ForFile(file).Select(o => o.Id));
    }

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("report.docx")]
    [InlineData("page.html")]
    [InlineData("data.bin")]
    public void Not_offered_for_other_files(string file)
    {
        Assert.DoesNotContain(TextEngine.ToUtf8Id, OperationCatalog.ForFile(file).Select(o => o.Id));
    }

    [Fact]
    public void Operation_shape()
    {
        Assert.Equal((OperationMode.Tool, MediaKind.Any, EngineKind.Text, " (UTF-8)"),
            (ToUtf8.Mode, ToUtf8.Kind, ToUtf8.Engine, ToUtf8.Suffix));
        Assert.Equal("srt", ToUtf8.Output.Resolve(".srt"));
        Assert.Contains(TextEngine.ToUtf8Id, OperationCatalog.ForFiles(["a.txt", "b.srt"]).Select(o => o.Id));
        Assert.True(new Converter(new ToolLocator(), ConversionSettings.Default, [new TextEngine()]).CanRun(ToUtf8, "txt"));
        Assert.False(new Converter(new ToolLocator(), ConversionSettings.Default).CanRun(ToUtf8, "txt"));
    }

    [Fact]
    public async Task Cli_has_text_engine()
    {
        var src = Write("тест 1.txt", Cp866.GetBytes("Привет"));
        var stdout = new StringWriter();
        var code = await Program.RunAsync(["convert", src, TextEngine.ToUtf8Id], stdout, new StringWriter());
        Assert.Equal(0, code);
        Assert.Equal("Привет", Utf8WithBom(File.ReadAllBytes(Path.Combine(_dir.Path, "тест 1 (UTF-8).txt"))));
    }
}
