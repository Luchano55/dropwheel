using System.Text;

namespace DropWheel.Documents;

/// <summary>
/// Чтение текстовых файлов (TXT, Markdown, CSV) с угадыванием кодировки:
/// метка BOM (UTF-8 / UTF-16) → корректный UTF-8 → иначе Windows-1251 (старые русские файлы).
/// </summary>
public static class TextFiles
{
    static TextFiles() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>UTF-8 с меткой BOM: Блокнот и Excel по ней понимают, что это UTF-8.</summary>
    public static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    public static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    /// <summary>Кириллица Windows (ANSI). Провайдер кодовых страниц регистрируется здесь же.</summary>
    public static Encoding Windows1251 => Encoding.GetEncoding(1251);

    /// <summary>Нужен ExcelDataReader для старых .xls — вызвать до чтения.</summary>
    public static void EnsureCodePages() => _ = Windows1251;

    public static string Read(string path) => Decode(ReadAllBytes(path), out _);

    /// <summary>Прочитать файл, даже если он открыт в Word/Excel/Блокноте.</summary>
    public static byte[] ReadAllBytes(string path)
    {
        using var stream = OpenRead(path);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    public static string Decode(byte[] bytes, out Encoding encoding)
    {
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
        {
            encoding = Encoding.UTF8;
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        if (bytes is [0xFF, 0xFE, ..])
        {
            encoding = Encoding.Unicode;
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes is [0xFE, 0xFF, ..])
        {
            encoding = Encoding.BigEndianUnicode;
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }
        try
        {
            var text = StrictUtf8.GetString(bytes);
            encoding = Encoding.UTF8;
            return text;
        }
        catch (DecoderFallbackException)
        {
            encoding = Windows1251;
            return Windows1251.GetString(bytes);
        }
    }
}
