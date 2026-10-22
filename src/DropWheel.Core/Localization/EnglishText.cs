using System.Globalization;

namespace DropWheel.Localization;

/// <summary>Английский размер файла: «812 B», «64 KB», «1.1 MB», «3.4 GB» — по тем же правилам, что и русский.</summary>
public static class EnglishText
{
    private static readonly NumberFormatInfo Numbers = new() { NumberDecimalSeparator = ".", NumberGroupSeparator = "," };
    private static readonly string[] Units = ["KB", "MB", "GB", "TB"];

    public static string FileSize(long bytes)
    {
        if (bytes < 0) bytes = 0;
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        double value = bytes;
        var unit = -1;
        do
        {
            value /= 1024;
            unit++;
        } while (Math.Round(value) >= 1024 && unit < Units.Length - 1);

        var text = Math.Round(value, 1) < 10
            ? value.ToString("0.#", Numbers)
            : Math.Round(value).ToString("#,0", Numbers);
        return text + " " + Units[unit];
    }
}
