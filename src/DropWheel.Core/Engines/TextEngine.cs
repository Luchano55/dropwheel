using System.Text;
using System.Text.RegularExpressions;
using DropWheel.Conversions;

namespace DropWheel.Engines;

/// <summary>
/// «В UTF-8»: перекодировать текстовый файл (старые «русские» кодировки, UTF-16) в UTF-8 с BOM.
/// Встроенный движок, без внешних утилит; работает везде (CLI на Linux тоже).
/// </summary>
/// <remarks>
/// Определение кодировки:
/// 1. BOM UTF-8 или корректный UTF-8 без BOM (в том числе чистый ASCII) — «Файл уже в UTF-8».
/// 2. BOM UTF-32 / UTF-16 (LE, BE) — читаем по BOM.
/// 3. Есть нулевые байты без BOM — это не текст в однобайтовой кодировке: отказ.
/// 4. Иначе однобайтовая кодировка: Windows-1251 или DOS-866 (OEM русской Windows: консоль, старые .bat и .txt
///    из DOS). Выбираем ту, при чтении в которой байты ≥ 0x80 больше похожи на русский текст (Score): каждая русская
///    буква даёт свою частоту в языке (о — 11 %, е — 8,5 % … ъ — 0,04 %), псевдографика ┤╛ — штраф.
///    1251 в 866 читается как «рёшхЄ» (редкие буквы, Є ї ў, псевдографика), 866 в 1251 — как «Ї ў Ґ ¬» и Ђ ‚ „ …,
///    так что у правильной кодировки счёт заметно больше. При равенстве — 1251. На тексте из 2–3 букв («Ёж»)
///    возможна ошибка — у настоящих файлов букв достаточно.
/// Текст считается русским: западноевропейский 1252 («café») будет прочитан как 1251 («cafй»).
/// </remarks>
public sealed class TextEngine : IInternalEngine
{
    public const string ToUtf8Id = "text-to-utf8";

    /// <summary>Только текстовые форматы: перекодировка бинарного файла его испортит.</summary>
    public static readonly IReadOnlyList<string> InputExtensions = ["txt", "csv", "srt", "md", "ini", "log", "xml", "json"];

    /// <summary>Файл читается в память целиком — больше не берём.</summary>
    public const long MaxBytes = 256L * 1024 * 1024;

    public EngineKind Kind => EngineKind.Text;
    public bool IsAvailable => true;

    private static readonly Encoding Cp1251 = CodePagesEncodingProvider.Instance.GetEncoding(1251)!;
    private static readonly Encoding Cp866 = CodePagesEncodingProvider.Instance.GetEncoding(866)!;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public async Task RunAsync(Operation op, IReadOnlyList<string> inputs, string output, ConversionSettings settings,
        IProgress<double>? progress, CancellationToken ct)
    {
        if (op.Id != ToUtf8Id) throw new ConversionException($"Нет такой операции с текстом: «{op.Label}»", op.Id);

        var input = inputs[0];
        if (new FileInfo(input).Length > MaxBytes)
            throw new ConversionException("Файл слишком большой для перекодировки", $"{input}: больше {MaxBytes} байт");

        var bytes = await File.ReadAllBytesAsync(input, ct);
        var (text, _) = Decode(bytes);
        progress?.Report(0.5);

        var ext = Extensions.Normalize(Path.GetExtension(input));
        if (ext == "xml") text = FixXmlDeclaration(text);
        // JSON по RFC 8259 — без BOM (многие парсеры на нём спотыкаются); остальным BOM нужен,
        // чтобы Excel и старые программы поняли UTF-8.
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: ext != "json");
        await File.WriteAllTextAsync(output, text, encoding, ct);
    }

    /// <summary>Исходная кодировка и текст. Бросает ConversionException, если перекодировать нечего или нельзя.</summary>
    public static (string Text, string Source) Decode(byte[] bytes)
    {
        ReadOnlySpan<byte> b = bytes;
        if (b.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            throw new ConversionException("Файл уже в UTF-8", "BOM UTF-8");
        // UTF-32 LE проверяем раньше UTF-16 LE: у них общее начало FF FE.
        if (b.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE, 0x00, 0x00]))
            return (Strict(new UTF32Encoding(bigEndian: false, byteOrderMark: true, throwOnInvalidCharacters: true), b[4..], "UTF-32 LE"), "UTF-32 LE");
        if (b.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]))
            return (Strict(new UTF32Encoding(bigEndian: true, byteOrderMark: true, throwOnInvalidCharacters: true), b[4..], "UTF-32 BE"), "UTF-32 BE");
        if (b.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
            return (Strict(new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true), b[2..], "UTF-16 LE"), "UTF-16 LE");
        if (b.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
            return (Strict(new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true), b[2..], "UTF-16 BE"), "UTF-16 BE");

        if (IsUtf8(b)) throw new ConversionException("Файл уже в UTF-8", "корректный UTF-8 без BOM");
        if (b.Contains((byte)0))
            throw new ConversionException("Это не текст или кодировка не распознана", "нулевые байты без BOM");

        return Score(b, Cp866) > Score(b, Cp1251)
            ? (Cp866.GetString(b), "CP866")
            : (Cp1251.GetString(b), "Windows-1251");
    }

    private static string Strict(Encoding encoding, ReadOnlySpan<byte> body, string name)
    {
        try { return encoding.GetString(body); }
        catch (DecoderFallbackException ex) { throw new ConversionException("Файл повреждён: ошибка в тексте " + name, ex.Message, ex); }
    }

    private static bool IsUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            StrictUtf8.GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>Частота букв в русских текстах, % (о, е, а … ъ, ё).</summary>
    private static readonly Dictionary<char, double> RussianFrequency = new()
    {
        ['о'] = 10.97, ['е'] = 8.45, ['а'] = 8.01, ['и'] = 7.35, ['н'] = 6.70, ['т'] = 6.26, ['с'] = 5.47,
        ['р'] = 4.73, ['в'] = 4.54, ['л'] = 4.40, ['к'] = 3.49, ['м'] = 3.21, ['д'] = 2.98, ['п'] = 2.81,
        ['у'] = 2.62, ['я'] = 2.01, ['ы'] = 1.90, ['ь'] = 1.74, ['г'] = 1.70, ['з'] = 1.65, ['б'] = 1.59,
        ['ч'] = 1.44, ['й'] = 1.21, ['х'] = 0.97, ['ж'] = 0.94, ['ш'] = 0.73, ['ю'] = 0.64, ['ц'] = 0.48,
        ['щ'] = 0.36, ['э'] = 0.32, ['ф'] = 0.26, ['ъ'] = 0.04, ['ё'] = 0.04,
    };

    /// <summary>Насколько байты ≥ 0x80, прочитанные в этой кодировке, похожи на русский текст (больше — лучше).</summary>
    internal static double Score(ReadOnlySpan<byte> bytes, Encoding encoding)
    {
        Span<int> counts = stackalloc int[128];
        foreach (var x in bytes)
            if (x >= 0x80) counts[x - 0x80]++;

        var score = 0.0;
        Span<byte> one = stackalloc byte[1];
        Span<char> ch = stackalloc char[2];
        for (var i = 0; i < 128; i++)
        {
            if (counts[i] == 0) continue;
            one[0] = (byte)(i + 0x80);
            encoding.GetChars(one, ch);
            score += counts[i] * Weight(ch[0]);
        }
        return score;
    }

    private static double Weight(char c)
    {
        if (RussianFrequency.TryGetValue(char.ToLowerInvariant(c), out var frequency)) return frequency;
        // Псевдографика DOS (┤ ╛ ░ ■): в 866 так выглядят заглавные буквы из 1251.
        if (c is >= '─' and <= '◿') return -5;
        return 0; // знаки препинания, Є ї ў и прочее — нейтрально
    }

    /// <summary>&lt;?xml … encoding="windows-1251"?&gt; → encoding="utf-8", иначе XML-парсеры прочтут файл неверно.</summary>
    internal static string FixXmlDeclaration(string text) =>
        XmlEncoding.Replace(text, m => m.Groups[1].Value + m.Groups[2].Value + "utf-8" + m.Groups[2].Value, 1);

    private static readonly Regex XmlEncoding =
        new(@"\A(\s*<\?xml[^>]*?\bencoding\s*=\s*)([""'])[^""']*\2", RegexOptions.CultureInvariant);
}
