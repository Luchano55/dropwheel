using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DropWheel.Conversions;
using DropWheel.Localization;

namespace DropWheel.Shell;

/// <summary>Что принесло перетаскивание на колесо (решение D-BROWSER).</summary>
public enum DragContentKind
{
    /// <summary>Файлы на диске (CF_HDROP) — как всегда.</summary>
    Files,
    /// <summary>Картинка байтами (с сайта, из письма): сохранить в папку картинок и сконвертировать.</summary>
    Image,
    /// <summary>Только ссылка http(s) на картинку — без сети её не получить: подсказка на колесе.</summary>
    ImageLink,
    /// <summary>Ничего подходящего (текст, ссылка на страницу) — колесо прячется.</summary>
    Nothing,
}

/// <summary>Картинка из перетаскивания: байты, настоящий формат (по содержимому) и имя, предложенное источником.</summary>
/// <param name="Source">Откуда взяли — для журнала («FileContents», «PNG», «data:», …).</param>
public sealed record DraggedImage(byte[] Bytes, string Extension, string? SuggestedName, string Source);

public sealed record DragContent(DragContentKind Kind, IReadOnlyList<string> Files, DraggedImage? Image)
{
    public static DragContent Nothing { get; } = new(DragContentKind.Nothing, [], null);
}

/// <summary>
/// Форматы перетаскивания, которые прочитал хост (окно колеса). Чтение ленивое: дорогие форматы
/// (байты файла) запрашиваются, только если более важные ничего не дали.
/// </summary>
public sealed class DragSource
{
    /// <summary>CF_HDROP — пути файлов.</summary>
    public IReadOnlyList<string>? Files { get; init; }
    /// <summary>Есть ли признаки браузера/виртуального файла (FileGroupDescriptorW, ссылка) — без чтения данных.</summary>
    public bool LooksLikeBrowser { get; init; }
    /// <summary>Виртуальный файл: имя из FileGroupDescriptorW и байты FileContents (первый файл).</summary>
    public Func<(string? Name, byte[]? Bytes)>? VirtualFile { get; init; }
    /// <summary>Готовая картинка: «PNG», «image/png», CF_DIB(V5) → BMP. Имя формата — для журнала.</summary>
    public Func<(byte[]? Bytes, string Format)>? RawImage { get; init; }
    /// <summary>Ссылки: UniformResourceLocatorW, text/x-moz-url, &lt;img src&gt; из HTML.</summary>
    public Func<IReadOnlyList<string>>? Urls { get; init; }
    /// <summary>Чтение файла с диска (временный файл браузера); null — File.ReadAllBytes.</summary>
    public Func<string, byte[]?>? ReadFile { get; init; }
    /// <summary>Папка временных файлов (Path.GetTempPath()).</summary>
    public string? TempFolder { get; init; }
}

/// <summary>
/// Картинки из браузера (решение D-BROWSER): что взять из перетаскивания, формат по сигнатуре,
/// безопасное имя, data:-ссылки, FileGroupDescriptorW, CF_DIB. Без доступа к OLE — чистая логика с тестами.
/// Сеть не используется: картинку, от которой есть только http-ссылка, не скачиваем.
/// </summary>
public static partial class BrowserDrag
{
    /// <summary>Больше не читаем: картинка с сайта таких размеров не бывает, а память не резиновая.</summary>
    public const int MaxBytes = 200 * 1024 * 1024;

    /// <summary>Самая длинная основа имени (без расширения): путь в Windows ограничен 260 символами.</summary>
    public const int MaxStemLength = 80;

    /// <summary>
    /// Порядок: файлы (кроме временного файла браузера) → виртуальный файл → готовая картинка →
    /// временный файл браузера → data:-ссылка → http-ссылка на картинку (подсказка) → ничего.
    /// В каждом случае картинкой считается только то, что по содержимому — картинка
    /// (у ссылки Chrome тоже есть «виртуальный файл» — ярлык .url).
    /// </summary>
    public static DragContent Resolve(DragSource source)
    {
        var files = (source.Files ?? []).Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        string? browserTemp = null;
        if (files.Count > 0)
        {
            // Firefox кладёт картинку во временный файл и отдаёт его как обычный файл. Результат рядом с ним
            // оказался бы во %TEMP% — поэтому такой файл считается картинкой из браузера.
            if (files.Count == 1 && source.LooksLikeBrowser && IsInFolder(files[0], source.TempFolder))
                browserTemp = files[0];
            else
                return new DragContent(DragContentKind.Files, files, null);
        }

        if (source.VirtualFile?.Invoke() is { Bytes: { Length: > 0 } vbytes } virt && FromBytes(vbytes, virt.Name, "FileContents") is { } vimg)
            return Found(vimg);

        if (source.RawImage?.Invoke() is { Bytes: { Length: > 0 } rbytes } raw && FromBytes(rbytes, null, raw.Format) is { } rimg)
            return Found(rimg);

        if (browserTemp is not null)
        {
            var read = source.ReadFile ?? ReadAllBytesOrNull;
            if (read(browserTemp) is { Length: > 0 } tbytes && FromBytes(tbytes, Path.GetFileName(browserTemp), "CF_HDROP (temp)") is { } timg)
                return Found(timg);
            // Не картинка — пусть будет обычный файл, как раньше.
            return new DragContent(DragContentKind.Files, files, null);
        }

        var urls = source.Urls?.Invoke() ?? [];
        foreach (var url in urls)
        {
            if (TryDecodeDataUrl(url, out var data) && FromBytes(data, null, "data:") is { } dimg)
                return Found(dimg);
        }
        if (urls.Any(IsRemoteImageUrl)) return new DragContent(DragContentKind.ImageLink, [], null);
        return DragContent.Nothing;

        static DragContent Found(DraggedImage image) => new(DragContentKind.Image, [], image);
    }

    /// <summary>Картинка из байтов, если по сигнатуре это поддерживаемая картинка; имя — подсказка источника.</summary>
    public static DraggedImage? FromBytes(byte[] bytes, string? suggestedName, string source)
    {
        if (bytes.Length == 0 || bytes.Length > MaxBytes) return null;
        return SniffExtension(bytes) is { } ext ? new DraggedImage(bytes, ext, suggestedName, source) : null;
    }

    private static byte[]? ReadAllBytesOrNull(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length <= MaxBytes ? File.ReadAllBytes(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ---------- формат по содержимому ----------

    /// <summary>
    /// Расширение по первым байтам: jpg, png, gif, webp, bmp, tif, avif, heic, svg; иначе null
    /// (в том числе ярлык .url и HTML-страница). Сайт может отдать WebP под именем «photo.jpg» — верим байтам.
    /// </summary>
    public static string? SniffExtension(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpg";
        if (b.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return "png";
        if (b.StartsWith("GIF87a"u8) || b.StartsWith("GIF89a"u8)) return "gif";
        if (b.Length >= 12 && b.StartsWith("RIFF"u8) && b.Slice(8, 4).SequenceEqual("WEBP"u8)) return "webp";
        if (b.Length >= 26 && b.StartsWith("BM"u8)) return "bmp";
        if (b.StartsWith((ReadOnlySpan<byte>)[0x49, 0x49, 0x2A, 0x00]) || b.StartsWith((ReadOnlySpan<byte>)[0x4D, 0x4D, 0x00, 0x2A])) return "tif";
        if (b.Length >= 12 && b.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            var brand = Encoding.ASCII.GetString(b.Slice(8, 4));
            if (brand is "avif" or "avis") return "avif";
            if (brand is "heic" or "heix" or "heim" or "heis" or "hevc" or "hevx" or "mif1" or "msf1") return "heic";
            return null;
        }
        return LooksLikeSvg(b) ? "svg" : null;
    }

    private static bool LooksLikeSvg(ReadOnlySpan<byte> b)
    {
        // Текст в UTF-8 (с BOM или без): «<svg» в первых 1000 байтах, до него — только пролог и комментарии.
        var head = b[..Math.Min(b.Length, 1024)];
        if (head.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) head = head[3..];
        var text = Encoding.UTF8.GetString(head).TrimStart();
        if (!text.StartsWith('<')) return false;
        var svg = text.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
        if (svg < 0) return false;
        var html = text.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
        return html < 0 || html > svg;
    }

    // ---------- имя файла ----------

    /// <summary>
    /// Имя для сохранения: подсказанное источником (имя из FileGroupDescriptorW или из ссылки), очищенное
    /// от запрещённых в Windows символов, с расширением настоящего формата (photo.jpg с WebP внутри → photo.webp);
    /// синоним сохраняется (photo.jpeg с JPEG → photo.jpeg). Нечего взять — «Браузер 2026-10-22 10-31-05.webp».
    /// </summary>
    public static string FileName(string? suggested, string extension, DateTime now)
    {
        var ext = Extensions.Normalize(extension);
        var stem = SanitizeStem(suggested, out var suggestedExt);
        if (stem is null)
            return Loc.F(Loc.T.BrowserFile, now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture)) + "." + ext;
        if (suggestedExt is { Length: > 0 } s && Extensions.SameFormat(s, ext)) ext = s;
        return stem + "." + ext;
    }

    /// <summary>Свободный путь в папке картинок: занято — «photo (1).webp».</summary>
    public static string ImagePath(string folder, DraggedImage image, DateTime now, Func<string, bool>? exists = null) =>
        Jobs.OutputNamer.FirstFree(Path.Combine(folder, FileName(image.SuggestedName, image.Extension, now)), exists);

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Основа имени без расширения (или null, если от имени ничего не осталось) и расширение подсказки.</summary>
    private static string? SanitizeStem(string? name, out string? extension)
    {
        extension = null;
        if (string.IsNullOrWhiteSpace(name)) return null;
        // Только последняя часть пути: «../../x.jpg», «C:\x.jpg».
        var slash = name.LastIndexOfAny(['/', '\\']);
        if (slash >= 0) name = name[(slash + 1)..];

        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(ch < 32 || "<>:\"/\\|?*".Contains(ch) ? '_' : ch);
        var clean = Spaces().Replace(sb.ToString(), " ").Trim().TrimEnd('.', ' ');

        var dot = clean.LastIndexOf('.');
        var stem = clean;
        if (dot > 0 && clean.Length - dot - 1 is > 0 and <= 5)
        {
            extension = clean[(dot + 1)..].ToLowerInvariant();
            stem = clean[..dot];
        }
        stem = stem.Trim().TrimEnd('.', ' ');
        if (stem.Length > MaxStemLength)
        {
            stem = stem[..MaxStemLength];
            // Не резать суррогатную пару пополам.
            if (char.IsHighSurrogate(stem[^1])) stem = stem[..^1];
            stem = stem.TrimEnd('.', ' ');
        }
        if (stem.Length == 0 || stem.All(c => c is '_' or '.' or ' ' or '-')) return null;
        if (ReservedNames.Contains(stem)) stem = "_" + stem;
        return stem;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>Имя файла из ссылки: последний кусок пути без ?query и #якоря, %XX раскодированы; null — нет имени.</summary>
    public static string? NameFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "file")) return null;
        var path = uri.AbsolutePath;
        var last = path[(path.LastIndexOf('/') + 1)..];
        if (last.Length == 0) return null;
        try { last = Uri.UnescapeDataString(last); }
        catch (Exception) { /* как есть */ }
        return string.IsNullOrWhiteSpace(last) ? null : last;
    }

    // ---------- ссылки ----------

    /// <summary>
    /// data:image/…;base64,… (или с %XX вместо base64, как у SVG) → байты. Только картинки (тип image/…)
    /// и не больше <see cref="MaxBytes"/>.
    /// </summary>
    public static bool TryDecodeDataUrl(string? url, out byte[] bytes)
    {
        bytes = [];
        if (url is null) return false;
        url = url.Trim();
        if (!url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;
        var comma = url.IndexOf(',');
        if (comma < 0) return false;
        var meta = url[5..comma];
        var payload = url[(comma + 1)..];
        var parts = meta.Split(';');
        if (!parts[0].Trim().StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return false;
        var base64 = parts.Skip(1).Any(p => p.Trim().Equals("base64", StringComparison.OrdinalIgnoreCase));
        if (payload.Length > MaxBytes / 3 * 4 + 4) return false;
        try
        {
            if (base64)
            {
                // Base64 из HTML бывает с переносами строк и %-кодированием.
                var clean = Uri.UnescapeDataString(payload);
                clean = Whitespace().Replace(clean, "");
                bytes = Convert.FromBase64String(clean);
            }
            else
            {
                bytes = Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
            }
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
        return bytes.Length > 0;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>http(s)-ссылка, которая выглядит как картинка: путь кончается на .jpg/.webp/… .</summary>
    public static bool IsRemoteImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        var ext = Path.GetExtension(uri.AbsolutePath);
        return ext.Length > 1 && (Extensions.KindOf(ext) == MediaKind.Image || Extensions.SameFormat(ext, "ico"));
    }

    /// <summary>
    /// Ссылки на картинку из форматов перетаскивания: src у &lt;img&gt; в HTML (CF_HTML или text/html) — первыми,
    /// затем первая строка text/x-moz-url и UniformResourceLocatorW. Пустые и повторы убраны.
    /// </summary>
    public static IReadOnlyList<string> CollectUrls(string? html, string? mozUrl, string? url)
    {
        var list = new List<string>();
        if (!string.IsNullOrEmpty(html))
        {
            foreach (Match m in ImgSrc().Matches(html))
                list.Add(System.Net.WebUtility.HtmlDecode(m.Groups["src"].Value));
        }
        if (!string.IsNullOrEmpty(mozUrl))
            list.Add(mozUrl.Split('\n', 2)[0]);
        if (!string.IsNullOrEmpty(url))
            list.Add(url);
        return list.Select(u => u.Trim().TrimEnd('\0'))
            .Where(u => u.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    [GeneratedRegex("""<img\b[^>]*?\bsrc\s*=\s*(?:"(?<src>[^"]*)"|'(?<src>[^']*)'|(?<src>[^\s>]+))""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ImgSrc();

    // ---------- FileGroupDescriptorW ----------

    private const int DescriptorWSize = 592;
    private const int DescriptorASize = 332;
    private const int NameOffset = 72;
    private const uint FdFileSize = 0x40;
    private const uint FileAttributeDirectory = 0x10;

    /// <summary>Файл из FILEGROUPDESCRIPTOR: имя и размер (если источник его указал, FD_FILESIZE).</summary>
    public sealed record VirtualFileInfo(string Name, long? Size, bool IsFolder);

    /// <summary>
    /// Разбор FILEGROUPDESCRIPTORW (unicode = true) или FILEGROUPDESCRIPTORA: UINT cItems, затем cItems
    /// структур FILEDESCRIPTOR (592 / 332 байта; имя — с 72-го байта, 260 символов). Битые данные — пустой список.
    /// </summary>
    public static IReadOnlyList<VirtualFileInfo> ParseFileGroupDescriptor(ReadOnlySpan<byte> data, bool unicode = true)
    {
        if (data.Length < 4) return [];
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var size = unicode ? DescriptorWSize : DescriptorASize;
        if (count == 0 || count > 10000 || data.Length < 4 + (long)count * size) return [];
        var result = new List<VirtualFileInfo>((int)count);
        for (var i = 0; i < count; i++)
        {
            var item = data.Slice(4 + i * size, size);
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(item);
            var attributes = BinaryPrimitives.ReadUInt32LittleEndian(item[36..]);
            var high = BinaryPrimitives.ReadUInt32LittleEndian(item[64..]);
            var low = BinaryPrimitives.ReadUInt32LittleEndian(item[68..]);
            var nameBytes = item[NameOffset..];
            string name;
            if (unicode)
            {
                var chars = new char[260];
                for (var c = 0; c < 260; c++) chars[c] = (char)BinaryPrimitives.ReadUInt16LittleEndian(nameBytes[(c * 2)..]);
                var end = Array.IndexOf(chars, '\0');
                name = new string(chars, 0, end < 0 ? 260 : end);
            }
            else
            {
                var end = nameBytes[..260].IndexOf((byte)0);
                name = Encoding.Latin1.GetString(nameBytes[..(end < 0 ? 260 : end)]);
            }
            long? length = (flags & FdFileSize) != 0 ? ((long)high << 32) | low : null;
            result.Add(new VirtualFileInfo(name, length, (attributes & FileAttributeDirectory) != 0 && (flags & 0x04) != 0));
        }
        return result;
    }

    // ---------- CF_DIB ----------

    /// <summary>
    /// CF_DIB / CF_DIBV5 (BITMAPINFOHEADER + палитра + пиксели) → файл BMP: спереди 14 байт BITMAPFILEHEADER
    /// со смещением до пикселей. Не похоже на DIB — null.
    /// </summary>
    public static byte[]? DibToBmp(ReadOnlySpan<byte> dib)
    {
        if (dib.Length < 40) return null;
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
        if (headerSize is not (40 or 52 or 56 or 108 or 124) || headerSize > dib.Length) return null;
        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);
        var colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib[32..]);
        if (bitCount is not (1 or 4 or 8 or 16 or 24 or 32)) return null;

        long colors = colorsUsed != 0 ? colorsUsed : bitCount <= 8 ? 1L << bitCount : 0;
        long offset = 14 + headerSize + colors * 4;
        // BI_BITFIELDS (3) / BI_ALPHABITFIELDS (6) с коротким заголовком: маски идут сразу после него.
        if (headerSize == 40 && compression == 3) offset += 12;
        if (headerSize == 40 && compression == 6) offset += 16;
        if (offset > 14 + dib.Length || colors > 256) return null;

        var bmp = new byte[14 + dib.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2), bmp.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), (int)offset);
        dib.CopyTo(bmp.AsSpan(14));
        return bmp;
    }

    // ---------- пути ----------

    /// <summary>Лежит ли файл внутри папки (в любой глубине); сравнение без учёта регистра, как в Windows.</summary>
    public static bool IsInFolder(string path, string? folder)
    {
        if (string.IsNullOrEmpty(folder)) return false;
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
