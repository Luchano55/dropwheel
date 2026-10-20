using System.Globalization;

namespace DropWheel.Text;

/// <summary>Русские подписи: склонение по числу, размер файла («1,1 МБ»). Не зависит от языка системы.</summary>
public static class Russian
{
    private static readonly NumberFormatInfo Numbers = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = "\u00A0" };
    private static readonly string[] Units = ["КБ", "МБ", "ГБ", "ТБ"];

    /// <summary>1 файл, 2 файла, 5 файлов, 11 файлов, 21 файл.</summary>
    public static string Plural(long n, string one, string few, string many)
    {
        var tens = Math.Abs(n) % 100;
        var last = tens % 10;
        if (tens is >= 11 and <= 14) return many;
        return last switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many,
        };
    }

    /// <summary>«5 файлов» — число и слово в нужной форме.</summary>
    public static string Count(long n, string one, string few, string many) => $"{n} {Plural(n, one, few, many)}";

    /// <summary>
    /// Размер как в проводнике (по 1024): «812 Б», «64 КБ», «1,1 МБ», «12 МБ», «3,4 ГБ».
    /// До 10 — с одним знаком после запятой, дальше — целые. Между числом и единицей неразрывный пробел.
    /// </summary>
    public static string FileSize(long bytes)
    {
        if (bytes < 0) bytes = 0;
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + "\u00A0Б";
        double value = bytes;
        var unit = -1;
        // Считаем по округлённому: 1023,6 КБ показываем как «1 МБ», а не «1 024 КБ».
        do
        {
            value /= 1024;
            unit++;
        } while (Math.Round(value) >= 1024 && unit < Units.Length - 1);

        var text = Math.Round(value, 1) < 10
            ? value.ToString("0.#", Numbers)
            : Math.Round(value).ToString("#,0", Numbers);
        return text + "\u00A0" + Units[unit];
    }
}
