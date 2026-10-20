using System.Text;
using DropWheel.Documents;

namespace DropWheel.Tests.Documents;

/// <summary>HTML-страницы для печати в PDF через Edge (сама печать — в Windows-тестах).</summary>
public class HtmlPagesTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void File_uri_escapes_every_part_of_the_path()
    {
        var path = _dir.File("тест #1 50%.md", "x");

        var uri = HtmlPages.FileUri(path);

        Assert.StartsWith("file:///", uri);
        Assert.DoesNotContain(" ", uri);
        Assert.Contains("%D0%9C%D0%BE%D0%B8%20%D1%84%D0%BE%D1%82%D0%BE", uri); // «Мои фото»
        Assert.EndsWith("%D1%82%D0%B5%D1%81%D1%82%20%231%2050%25.md", uri);    // # и % — не якорь и не escape
        Assert.Equal(Path.GetFullPath(path), new Uri(uri).LocalPath);
        Assert.EndsWith("/", HtmlPages.FileUri(_dir.Path, addTrailingSlash: true));
    }

    [Fact]
    public void Plain_text_is_escaped_and_keeps_layout()
    {
        var html = HtmlPages.FromPlainText("<script>alert(1)</script> & \"кавычки\"\n\tотступ", "заметка <1>");

        Assert.Contains("<pre>&lt;script&gt;alert(1)&lt;/script&gt; &amp; &quot;кавычки&quot;\n\tотступ</pre>", html);
        Assert.Contains("<title>заметка &lt;1&gt;</title>", html);
        Assert.Contains("white-space: pre-wrap", html);
        Assert.Contains("monospace", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void Markdown_page_for_printing_has_base_for_relative_images()
    {
        var baseUri = HtmlPages.FileUri(_dir.Path, addTrailingSlash: true);

        var html = HtmlPages.FromMarkdown("![схема](картинки/схема.png)\n\n# Раздел", "doc", baseUri);

        Assert.Contains($"<base href=\"{baseUri}\">", html);
        Assert.Contains("<img src=\"%D0%BA%D0%B0%D1%80%D1%82%D0%B8%D0%BD%D0%BA%D0%B8/", html); // относительный путь — от <base>
        Assert.Contains("alt=\"схема\"", html);
        Assert.Contains("@page { size: 210mm 297mm; margin: 18mm 18mm 18mm 18mm; }", html);
    }

    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0xD0, 0xAF }, "Я")]       // UTF-8 с BOM
    [InlineData(new byte[] { 0xD0, 0xAF }, "Я")]                         // UTF-8 без BOM
    [InlineData(new byte[] { 0xDF }, "Я")]                               // Windows-1251
    [InlineData(new byte[] { 0xFF, 0xFE, 0x2F, 0x04 }, "Я")]             // UTF-16 LE
    public void Text_encoding_is_guessed(byte[] bytes, string expected) =>
        Assert.Equal(expected, TextFiles.Decode(bytes, out _));

    [Fact]
    public void Text_file_open_elsewhere_can_be_read()
    {
        var path = _dir.File("открыт.txt", "текст");
        using var other = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Equal("текст", TextFiles.Read(path));
        Assert.Equal("текст", Encoding.UTF8.GetString(TextFiles.ReadAllBytes(path)));
    }
}
