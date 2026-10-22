using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Media.Imaging;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using WpfDataObject = System.Windows.IDataObject;

namespace DropWheel.Shell;

/// <summary>
/// Чтение перетаскивания над колесом (решение D-BROWSER): файлы (CF_HDROP), «виртуальный файл»
/// (FileGroupDescriptorW + FileContents — так Chrome и Edge отдают картинку с сайта), готовая картинка
/// (PNG, image/png, CF_DIBV5, CF_DIB), ссылки (UniformResourceLocatorW, text/x-moz-url, &lt;img src&gt; из HTML).
/// Что из этого взять — решает ядро (<see cref="BrowserDrag.Resolve"/>). Только поток UI (внутри цикла OLE).
/// <para>
/// FileContents читается напрямую через COM IDataObject с lindex = номер файла: обёртка WPF
/// просит lindex = -1, и многие источники на это отвечают ошибкой. Данные приходят в HGLOBAL или IStream.
/// </para>
/// </summary>
internal static class DragDataReader
{
    private const string FileGroupDescriptorW = "FileGroupDescriptorW";
    private const string FileGroupDescriptorA = "FileGroupDescriptor";
    private const string FileContents = "FileContents";
    private const string UrlW = "UniformResourceLocatorW";
    private const string UrlA = "UniformResourceLocator";
    private const string MozUrl = "text/x-moz-url";
    private const string HtmlFormat = "HTML Format";
    private const string TextHtml = "text/html";
    private const short CfDib = 8;
    private const short CfDibV5 = 17;

    /// <summary>Что принесло перетаскивание. Ошибки чтения не бросаются — такого формата «нет».</summary>
    public static DragContent Read(WpfDataObject data)
    {
        var com = data as ComDataObject;
        var source = new DragSource
        {
            Files = ReadFiles(data),
            LooksLikeBrowser = Present(data, FileGroupDescriptorW) || Present(data, FileGroupDescriptorA)
                || Present(data, UrlW) || Present(data, UrlA) || Present(data, MozUrl),
            VirtualFile = com is null ? null : () => ReadVirtualFile(com),
            RawImage = com is null ? null : () => ReadRawImage(com),
            Urls = com is null ? null : () => ReadUrls(com),
            TempFolder = Path.GetTempPath(),
        };
        return BrowserDrag.Resolve(source);
    }

    private static string[]? ReadFiles(WpfDataObject data)
    {
        try
        {
            return data.GetDataPresent(System.Windows.DataFormats.FileDrop)
                ? data.GetData(System.Windows.DataFormats.FileDrop) as string[]
                : null;
        }
        catch (Exception)
        {
            // Некоторые источники бросают COMException на чужих форматах.
            return null;
        }
    }

    private static bool Present(WpfDataObject data, string format)
    {
        try { return data.GetDataPresent(format, autoConvert: false); }
        catch (Exception) { return false; }
    }

    // ---- виртуальный файл ----

    private static (string? Name, byte[]? Bytes) ReadVirtualFile(ComDataObject com)
    {
        var unicode = true;
        var descriptor = GetBytes(com, FileGroupDescriptorW);
        if (descriptor is null)
        {
            unicode = false;
            descriptor = GetBytes(com, FileGroupDescriptorA);
        }
        if (descriptor is null) return (null, null);

        var files = BrowserDrag.ParseFileGroupDescriptor(descriptor, unicode);
        for (var i = 0; i < files.Count; i++)
        {
            if (files[i].IsFolder) continue;
            // Берём первый файл: браузер кладёт ровно один (картинку или ярлык .url).
            var bytes = GetBytes(com, Format(FileContents), i);
            if (bytes is not null && files[i].Size is { } size && size >= 0 && size < bytes.Length)
                bytes = bytes[..(int)size]; // HGLOBAL бывает больше данных (округление выделения)
            return (files[i].Name, bytes);
        }
        return (null, null);
    }

    // ---- готовая картинка ----

    private static (byte[]? Bytes, string Format) ReadRawImage(ComDataObject com)
    {
        foreach (var name in new[] { "PNG", "image/png" })
        {
            if (GetBytes(com, name) is { Length: > 0 } png && BrowserDrag.SniffExtension(png) == "png") return (png, name);
        }
        foreach (var (cf, name) in new[] { (CfDibV5, "CF_DIBV5"), (CfDib, "CF_DIB") })
        {
            if (GetBytes(com, cf) is { Length: > 0 } dib && BrowserDrag.DibToBmp(dib) is { } bmp)
                return (BmpToPng(bmp) ?? bmp, name);
        }
        return (null, "");
    }

    /// <summary>BMP → PNG средствами WPF (меньше файл, привычный формат). Не вышло — null, останется BMP.</summary>
    private static byte[]? BmpToPng(byte[] bmp)
    {
        try
        {
            using var input = new MemoryStream(bmp);
            var decoder = new BmpBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(decoder.Frames[0]);
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---- ссылки ----

    private static IReadOnlyList<string> ReadUrls(ComDataObject com)
    {
        var html = Text(GetBytes(com, HtmlFormat), utf16: false) ?? Text(GetBytes(com, TextHtml), utf16: null);
        var moz = Text(GetBytes(com, MozUrl), utf16: true);
        var url = Text(GetBytes(com, UrlW), utf16: true) ?? Text(GetBytes(com, UrlA), utf16: false);
        return BrowserDrag.CollectUrls(html, moz, url);
    }

    /// <summary>utf16: true — UTF-16, false — UTF-8, null — угадать (у UTF-16 второй байт ASCII-текста нулевой).</summary>
    private static string? Text(byte[]? bytes, bool? utf16)
    {
        if (bytes is not { Length: > 0 }) return null;
        var unicode = utf16 ?? (bytes.Length >= 2 && bytes[1] == 0);
        var text = unicode ? System.Text.Encoding.Unicode.GetString(bytes) : System.Text.Encoding.UTF8.GetString(bytes);
        var end = text.IndexOf('\0');
        return end >= 0 ? text[..end] : text;
    }

    // ---- COM ----

    private static short Format(string name) =>
        unchecked((short)System.Windows.DataFormats.GetDataFormat(name).Id);

    private static byte[]? GetBytes(ComDataObject com, string format, int index = -1) => GetBytes(com, Format(format), index);

    /// <summary>Данные формата как байты (HGLOBAL или IStream); нет формата или ошибка — null.</summary>
    private static byte[]? GetBytes(ComDataObject com, short cf, int index = -1)
    {
        var format = new FORMATETC
        {
            cfFormat = cf,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = index,
            ptd = IntPtr.Zero,
            tymed = TYMED.TYMED_HGLOBAL | TYMED.TYMED_ISTREAM,
        };
        STGMEDIUM medium;
        try
        {
            com.GetData(ref format, out medium);
        }
        catch (Exception)
        {
            return null;
        }
        try
        {
            return medium.tymed switch
            {
                TYMED.TYMED_HGLOBAL => FromHGlobal(medium.unionmember),
                TYMED.TYMED_ISTREAM => FromStream(medium.unionmember),
                _ => null,
            };
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    private static byte[]? FromHGlobal(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return null;
        var size = (long)GlobalSize(handle);
        if (size <= 0 || size > BrowserDrag.MaxBytes) return null;
        var ptr = GlobalLock(handle);
        if (ptr == IntPtr.Zero) return null;
        try
        {
            var bytes = new byte[size];
            Marshal.Copy(ptr, bytes, 0, (int)size);
            return bytes;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private static byte[]? FromStream(IntPtr unknown)
    {
        if (unknown == IntPtr.Zero) return null;
        var stream = (IStream)Marshal.GetObjectForIUnknown(unknown);
        var read = Marshal.AllocCoTaskMem(sizeof(int));
        try
        {
            try { stream.Seek(0, 0 /* STREAM_SEEK_SET */, IntPtr.Zero); }
            catch (Exception) { /* поток без перемотки — читаем с текущего места */ }
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                Marshal.WriteInt32(read, 0);
                stream.Read(buffer, buffer.Length, read);
                var n = Marshal.ReadInt32(read);
                if (n <= 0) break;
                output.Write(buffer, 0, n);
                if (output.Length > BrowserDrag.MaxBytes) return null;
            }
            return output.Length > 0 ? output.ToArray() : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(read);
            Marshal.ReleaseComObject(stream);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
}
