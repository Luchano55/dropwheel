using System.Buffers.Binary;
using System.Text;
using DropWheel.Shell;

namespace DropWheel.Tests.Shell;

/// <summary>Картинки из браузера (D-BROWSER): формат по байтам, имя, data:-ссылки, FileGroupDescriptorW, CF_DIB, выбор.</summary>
public class BrowserDragTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 10, 31, 5);

    // Начала настоящих файлов (дальше — что угодно).
    private static readonly byte[] Jpg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];
    private static readonly byte[] Webp = [.. "RIFF"u8, 0x24, 0, 0, 0, .. "WEBPVP8 "u8];
    private static readonly byte[] UrlShortcut = "[InternetShortcut]\r\nURL=https://example.com/\r\n"u8.ToArray();

    private static byte[] Ftyp(string brand) => [0, 0, 0, 0x1C, .. "ftyp"u8, .. Encoding.ASCII.GetBytes(brand), 0, 0, 0, 0];

    // ---------- формат ----------

    public static TheoryData<byte[], string?> Signatures() => new()
    {
        { Jpg, "jpg" },
        { Png, "png" },
        { "GIF89a\x01\0\x01\0"u8.ToArray(), "gif" },
        { "GIF87a\x01\0\x01\0"u8.ToArray(), "gif" },
        { Webp, "webp" },
        { [.. "BM"u8, .. new byte[30]], "bmp" },
        { [0x49, 0x49, 0x2A, 0x00, 8, 0, 0, 0], "tif" },
        { [0x4D, 0x4D, 0x00, 0x2A, 0, 0, 0, 8], "tif" },
        { Ftyp("avif"), "avif" },
        { Ftyp("heic"), "heic" },
        { Ftyp("mif1"), "heic" },
        { Ftyp("isom"), null }, // MP4 — не картинка
        { "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray(), "svg" },
        { [0xEF, 0xBB, 0xBF, .. "<?xml version=\"1.0\"?>\n<!-- c -->\n<svg/>"u8], "svg" },
        { "<!DOCTYPE html><html><body><svg/></body></html>"u8.ToArray(), null },
        { UrlShortcut, null },
        { "RIFF\0\0\0\0WAVEfmt "u8.ToArray(), null },
        { [], null },
        { [0x00, 0x00, 0x01, 0x00], null }, // ICO: на входе не поддерживается
    };

    [Theory]
    [MemberData(nameof(Signatures))]
    public void Format_is_detected_by_content(byte[] bytes, string? expected) =>
        Assert.Equal(expected, BrowserDrag.SniffExtension(bytes));

    // ---------- имя ----------

    [Theory]
    [InlineData("photo.jpg", "webp", "photo.webp")]            // сайт отдал WebP под именем .jpg — верим байтам
    [InlineData("photo.jpeg", "jpg", "photo.jpeg")]            // синоним остаётся
    [InlineData("images.jfif", "jpg", "images.jfif")]
    [InlineData("Мои фото: лето?.png", "png", "Мои фото_ лето_.png")]
    [InlineData("тест 1.webp", "webp", "тест 1.webp")]
    [InlineData(@"..\..\Windows\evil.jpg", "jpg", "evil.jpg")]
    [InlineData("a/b/c.gif", "gif", "c.gif")]
    [InlineData("CON.png", "png", "_CON.png")]
    [InlineData("image", "webp", "image.webp")]
    [InlineData("photo...", "png", "photo.png")]
    [InlineData("  много   пробелов  .jpg ", "jpg", "много пробелов.jpg")]
    [InlineData("tab\there.png", "png", "tab_here.png")]
    public void File_name_keeps_the_source_name_when_sane(string suggested, string ext, string expected) =>
        Assert.Equal(expected, BrowserDrag.FileName(suggested, ext, Now));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("___.png")]
    [InlineData("..")]
    [InlineData("/")]
    public void File_name_falls_back_to_date(string? suggested) =>
        Assert.Equal("Браузер 2026-10-22 10-31-05.webp", BrowserDrag.FileName(suggested, "webp", Now));

    [Fact]
    public void Long_names_are_cut()
    {
        var name = BrowserDrag.FileName(new string('я', 300) + ".png", "png", Now);
        Assert.Equal(BrowserDrag.MaxStemLength + 4, name.Length);
        Assert.EndsWith(".png", name);
    }

    [Fact]
    public void Busy_name_gets_a_number()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Мои фото");
        HashSet<string> busy = [Path.Combine(folder, "фото.webp"), Path.Combine(folder, "фото (1).webp")];
        var image = new DraggedImage(Webp, "webp", "фото.jpg", "FileContents");
        Assert.Equal(Path.Combine(folder, "фото (2).webp"), BrowserDrag.ImagePath(folder, image, Now, busy.Contains));
    }

    [Theory]
    [InlineData("https://site.ru/img/%D1%84%D0%BE%D1%82%D0%BE%201.webp?w=300#top", "фото 1.webp")]
    [InlineData("https://cdn.example.com/a/b/photo.JPG", "photo.JPG")]
    [InlineData("https://site.ru/", null)]
    [InlineData("data:image/png;base64,AAAA", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("не ссылка", null)]
    [InlineData(null, null)]
    public void Name_from_url(string? url, string? expected) =>
        Assert.Equal(expected, BrowserDrag.NameFromUrl(url));

    // ---------- ссылки ----------

    [Fact]
    public void Base64_data_url_is_decoded()
    {
        var url = "data:image/png;base64," + Convert.ToBase64String(Png);
        Assert.True(BrowserDrag.TryDecodeDataUrl(url, out var bytes));
        Assert.Equal(Png, bytes);
    }

    [Fact]
    public void Base64_with_line_breaks_and_escapes_is_decoded()
    {
        var b64 = Convert.ToBase64String(Jpg);
        var url = "data:image/jpeg;charset=utf-8;base64," + b64[..4] + "\r\n " + b64[4..].Replace("+", "%2B");
        Assert.True(BrowserDrag.TryDecodeDataUrl(url, out var bytes));
        Assert.Equal(Jpg, bytes);
    }

    [Fact]
    public void Percent_encoded_svg_data_url_is_decoded()
    {
        Assert.True(BrowserDrag.TryDecodeDataUrl("data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%2F%3E", out var bytes));
        Assert.Equal("svg", BrowserDrag.SniffExtension(bytes));
    }

    [Theory]
    [InlineData("data:text/plain;base64,SGVsbG8=")]
    [InlineData("data:image/png;base64,***")]
    [InlineData("data:image/png;base64")]
    [InlineData("data:image/png;base64,")]
    [InlineData("https://site.ru/a.png")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_urls_are_not_decoded(string? url) =>
        Assert.False(BrowserDrag.TryDecodeDataUrl(url, out _));

    [Theory]
    [InlineData("https://site.ru/a.jpg", true)]
    [InlineData("http://site.ru/путь/фото.webp?w=300", true)]
    [InlineData("https://site.ru/a.AVIF", true)]
    [InlineData("https://site.ru/favicon.ico", true)]
    [InlineData("https://site.ru/page.html", false)]
    [InlineData("https://site.ru/article/123", false)]
    [InlineData("data:image/png;base64,AAAA", false)]
    [InlineData("file:///C:/a.jpg", false)]
    [InlineData("", false)]
    public void Remote_image_url(string url, bool expected) =>
        Assert.Equal(expected, BrowserDrag.IsRemoteImageUrl(url));

    [Fact]
    public void Urls_come_from_html_img_then_moz_url_then_url()
    {
        const string html = "Version:0.9\r\nStartHTML:00000097\r\n<html><body><!--StartFragment-->"
            + "<a href=\"https://site.ru/page\"><IMG alt='x' class=a SRC=\"https://site.ru/a.png?x=1&amp;y=2\"></a>"
            + "<img src=data:image/gif;base64,R0lGODlh>"
            + "<!--EndFragment--></body></html>";
        var urls = BrowserDrag.CollectUrls(html, "https://site.ru/a.png?x=1&y=2\nЗаголовок", "https://site.ru/page\0");
        Assert.Equal(["https://site.ru/a.png?x=1&y=2", "data:image/gif;base64,R0lGODlh", "https://site.ru/page"], urls);
    }

    [Fact]
    public void No_urls_from_nothing() => Assert.Empty(BrowserDrag.CollectUrls(null, "", null));

    // ---------- FileGroupDescriptorW ----------

    private static byte[] Descriptor(bool unicode, params (string Name, long? Size, bool Folder)[] files)
    {
        var size = unicode ? 592 : 332;
        var data = new byte[4 + files.Length * size];
        BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)files.Length);
        for (var i = 0; i < files.Length; i++)
        {
            var item = data.AsSpan(4 + i * size, size);
            uint flags = 0;
            if (files[i].Size is { } s)
            {
                flags |= 0x40;
                BinaryPrimitives.WriteUInt32LittleEndian(item[64..], (uint)(s >> 32));
                BinaryPrimitives.WriteUInt32LittleEndian(item[68..], (uint)s);
            }
            if (files[i].Folder)
            {
                flags |= 0x04;
                BinaryPrimitives.WriteUInt32LittleEndian(item[36..], 0x10);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(item, flags);
            var name = unicode ? Encoding.Unicode.GetBytes(files[i].Name) : Encoding.Latin1.GetBytes(files[i].Name);
            name.CopyTo(item[72..]);
        }
        return data;
    }

    [Fact]
    public void Unicode_descriptor_is_parsed()
    {
        var files = BrowserDrag.ParseFileGroupDescriptor(Descriptor(true, ("фото с сайта.webp", 5_000_000_000, false), ("папка", null, true)));
        Assert.Equal(2, files.Count);
        Assert.Equal(new BrowserDrag.VirtualFileInfo("фото с сайта.webp", 5_000_000_000, false), files[0]);
        Assert.Equal(new BrowserDrag.VirtualFileInfo("папка", null, true), files[1]);
    }

    [Fact]
    public void Ansi_descriptor_is_parsed()
    {
        var files = BrowserDrag.ParseFileGroupDescriptor(Descriptor(false, ("photo.jpg", 12, false)), unicode: false);
        Assert.Equal([new BrowserDrag.VirtualFileInfo("photo.jpg", 12, false)], files);
    }

    [Fact]
    public void Broken_descriptor_gives_nothing()
    {
        var good = Descriptor(true, ("a.png", null, false));
        Assert.Empty(BrowserDrag.ParseFileGroupDescriptor(good.AsSpan(0, 100)));
        Assert.Empty(BrowserDrag.ParseFileGroupDescriptor([1, 0]));
        Assert.Empty(BrowserDrag.ParseFileGroupDescriptor([0, 0, 0, 0]));
        Assert.Empty(BrowserDrag.ParseFileGroupDescriptor([0xFF, 0xFF, 0xFF, 0xFF, 0, 0]));
    }

    // ---------- CF_DIB ----------

    private static byte[] Dib(int headerSize, ushort bits, uint compression = 0, uint colorsUsed = 0, int extra = 64)
    {
        var dib = new byte[headerSize + extra];
        BinaryPrimitives.WriteInt32LittleEndian(dib, headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), bits);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(16), compression);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(32), colorsUsed);
        return dib;
    }

    [Theory]
    [InlineData(40, 24, 0u, 0u, 54)]
    [InlineData(40, 32, 0u, 0u, 54)]
    [InlineData(40, 32, 3u, 0u, 66)]          // BI_BITFIELDS: три маски после заголовка
    [InlineData(40, 8, 0u, 0u, 54 + 1024)]    // палитра 256 цветов
    [InlineData(40, 8, 0u, 16u, 54 + 64)]     // палитра из biClrUsed
    [InlineData(124, 32, 3u, 0u, 138)]        // BITMAPV5HEADER: маски внутри заголовка
    public void Dib_becomes_bmp_with_pixel_offset(int header, ushort bits, uint compression, uint colors, int offset)
    {
        var dib = Dib(header, bits, compression, colors, extra: 2000);
        var bmp = BrowserDrag.DibToBmp(dib)!;
        Assert.Equal("bmp", BrowserDrag.SniffExtension(bmp));
        Assert.Equal(bmp.Length, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(2)));
        Assert.Equal(offset, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(10)));
        Assert.Equal(dib, bmp[14..]);
    }

    [Fact]
    public void Not_a_dib_gives_null()
    {
        Assert.Null(BrowserDrag.DibToBmp(new byte[10]));
        Assert.Null(BrowserDrag.DibToBmp(Dib(41, 24)));
        Assert.Null(BrowserDrag.DibToBmp(Dib(40, 7)));
        Assert.Null(BrowserDrag.DibToBmp(Dib(40, 8, colorsUsed: 100000)));
    }

    // ---------- папки ----------

    [Fact]
    public void Is_in_folder()
    {
        var temp = Path.GetTempPath();
        Assert.True(BrowserDrag.IsInFolder(Path.Combine(temp, "mozilla", "фото 1.png"), temp));
        Assert.True(BrowserDrag.IsInFolder(Path.Combine(temp, "a.png"), Path.TrimEndingDirectorySeparator(temp)));
        Assert.False(BrowserDrag.IsInFolder(Path.TrimEndingDirectorySeparator(temp) + "2" + Path.DirectorySeparatorChar + "a.png", temp));
        Assert.False(BrowserDrag.IsInFolder(Path.Combine(temp, "a.png"), null));
    }

    // ---------- что взять из перетаскивания ----------

    private static Func<T> Never<T>() => () => throw new InvalidOperationException("не должно читаться");

    [Fact]
    public void Explorer_files_are_files_and_nothing_else_is_read()
    {
        var content = BrowserDrag.Resolve(new DragSource
        {
            Files = [@"C:\Мои фото\тест 1.heic", ""],
            VirtualFile = Never<(string?, byte[]?)>(),
            RawImage = Never<(byte[]?, string)>(),
            Urls = Never<IReadOnlyList<string>>(),
            TempFolder = Path.GetTempPath(),
        });
        Assert.Equal(DragContentKind.Files, content.Kind);
        Assert.Equal([@"C:\Мои фото\тест 1.heic"], content.Files);
    }

    [Fact]
    public void Chrome_virtual_file_wins_and_format_comes_from_bytes()
    {
        var content = BrowserDrag.Resolve(new DragSource
        {
            LooksLikeBrowser = true,
            VirtualFile = () => ("photo.jpg", Webp),
            RawImage = Never<(byte[]?, string)>(),
            Urls = Never<IReadOnlyList<string>>(),
        });
        Assert.Equal(DragContentKind.Image, content.Kind);
        Assert.Equal("webp", content.Image!.Extension);
        Assert.Equal("photo.jpg", content.Image.SuggestedName);
        Assert.Equal("FileContents", content.Image.Source);
        Assert.Same(Webp, content.Image.Bytes);
    }

    [Fact]
    public void Raw_png_when_there_is_no_virtual_file()
    {
        var content = BrowserDrag.Resolve(new DragSource
        {
            VirtualFile = () => (null, null),
            RawImage = () => (Png, "PNG"),
        });
        Assert.Equal(DragContentKind.Image, content.Kind);
        Assert.Equal("png", content.Image!.Extension);
        Assert.Null(content.Image.SuggestedName);
        Assert.Equal("PNG", content.Image.Source);
    }

    [Fact]
    public void Data_url_is_used_offline()
    {
        var content = BrowserDrag.Resolve(new DragSource
        {
            Urls = () => ["https://site.ru/page", "data:image/jpeg;base64," + Convert.ToBase64String(Jpg)],
        });
        Assert.Equal(DragContentKind.Image, content.Kind);
        Assert.Equal("jpg", content.Image!.Extension);
        Assert.Equal(Jpg, content.Image.Bytes);
    }

    [Fact]
    public void Link_drag_with_url_shortcut_is_nothing()
    {
        // Chrome у ссылки тоже кладёт «виртуальный файл» — ярлык .url. Это не картинка.
        var content = BrowserDrag.Resolve(new DragSource
        {
            LooksLikeBrowser = true,
            VirtualFile = () => ("Новости.url", UrlShortcut),
            RawImage = () => (null, ""),
            Urls = () => ["https://site.ru/news/1"],
        });
        Assert.Same(DragContent.Nothing, content);
    }

    [Fact]
    public void Only_http_link_to_picture_is_a_hint_not_a_download()
    {
        var content = BrowserDrag.Resolve(new DragSource
        {
            LooksLikeBrowser = true,
            VirtualFile = () => ("a.url", UrlShortcut),
            Urls = () => ["https://site.ru/photo.webp"],
        });
        Assert.Equal(DragContentKind.ImageLink, content.Kind);
        Assert.Null(content.Image);
    }

    [Fact]
    public void Text_drag_is_nothing() =>
        Assert.Same(DragContent.Nothing, BrowserDrag.Resolve(new DragSource { Urls = () => [] }));

    [Fact]
    public void Empty_source_is_nothing() => Assert.Same(DragContent.Nothing, BrowserDrag.Resolve(new DragSource()));

    [Fact]
    public void Firefox_temp_file_becomes_browser_image()
    {
        var temp = Path.Combine(Path.GetTempPath(), "mozilla-temp", "тест 1.png");
        var content = BrowserDrag.Resolve(new DragSource
        {
            Files = [temp],
            LooksLikeBrowser = true,
            TempFolder = Path.GetTempPath(),
            ReadFile = p => p == temp ? Png : null,
        });
        Assert.Equal(DragContentKind.Image, content.Kind);
        Assert.Equal("тест 1.png", content.Image!.SuggestedName);
        Assert.Equal("png", content.Image.Extension);
    }

    [Fact]
    public void Firefox_temp_file_prefers_virtual_file()
    {
        var temp = Path.Combine(Path.GetTempPath(), "a.bmp");
        var content = BrowserDrag.Resolve(new DragSource
        {
            Files = [temp],
            LooksLikeBrowser = true,
            TempFolder = Path.GetTempPath(),
            VirtualFile = () => ("a.webp", Webp),
            ReadFile = _ => throw new InvalidOperationException("не должно читаться"),
        });
        Assert.Equal("webp", content.Image!.Extension);
    }

    [Fact]
    public void Temp_file_that_is_not_a_picture_stays_a_file()
    {
        var temp = Path.Combine(Path.GetTempPath(), "doc.pdf");
        var content = BrowserDrag.Resolve(new DragSource
        {
            Files = [temp],
            LooksLikeBrowser = true,
            TempFolder = Path.GetTempPath(),
            ReadFile = _ => "%PDF-1.7"u8.ToArray(),
        });
        Assert.Equal(DragContentKind.Files, content.Kind);
        Assert.Equal([temp], content.Files);
    }

    [Theory]
    [InlineData(false, true)]  // временная папка, но не из браузера (архиватор) — обычный файл
    [InlineData(true, false)]  // из браузера, но не временный файл — обычный файл
    public void Other_files_stay_files(bool browser, bool inTemp)
    {
        var path = inTemp ? Path.Combine(Path.GetTempPath(), "a.png") : Path.Combine("/", "home", "Мои фото", "a.png");
        var content = BrowserDrag.Resolve(new DragSource
        {
            Files = [path],
            LooksLikeBrowser = browser,
            TempFolder = Path.GetTempPath(),
            ReadFile = _ => throw new InvalidOperationException("не должно читаться"),
        });
        Assert.Equal(DragContentKind.Files, content.Kind);
    }
}
