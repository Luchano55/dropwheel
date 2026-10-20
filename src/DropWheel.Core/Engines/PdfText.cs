using System.Text;

namespace DropWheel.Engines;

/// <summary>PDF → TXT (pdftotext): проверка результата.</summary>
public static class PdfText
{
    public const string ScanMessage = "В PDF нет текстового слоя (это скан)";

    /// <summary>
    /// У скана страницы — картинки, и pdftotext успешно пишет «пустой» текст: только разрывы страниц (\f)
    /// и пробелы. Такой TXT бесполезен — сообщаем причину. Есть хоть одна буква или цифра — текст есть.
    /// </summary>
    public static void EnsureHasText(string txtPath)
    {
        using var reader = new StreamReader(txtPath, Encoding.UTF8);
        var buffer = new char[8192];
        int read;
        while ((read = reader.Read(buffer)) > 0)
            foreach (var c in buffer.AsSpan(0, read))
                if (char.IsLetterOrDigit(c)) return;
        throw new ConversionException(ScanMessage, txtPath);
    }
}
