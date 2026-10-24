using System.Globalization;
using DropWheel.Localization;

namespace DropWheel.Capture;

/// <summary>Где на оверлее снимка показать подпись размера и панель кнопок (всё в DIP окна одного монитора).</summary>
public static class CaptureLayout
{
    /// <summary>Отступ подписи и панели от рамки и от края экрана, DIP.</summary>
    public const double Gap = 8;

    /// <summary>
    /// Панель кнопок у выделения: под ним, по правому краю; не помещается снизу — над ним;
    /// нигде — внутри выделения у нижнего края (выделение во весь экран). По горизонтали — не за краем экрана.
    /// </summary>
    public static (double X, double Y) Toolbar(DipRect selection, double width, double height, DipRect area)
    {
        var x = Clamp(selection.Right - width, area.X + Gap, area.Right - width - Gap);
        double y;
        if (selection.Bottom + Gap + height <= area.Bottom - Gap) y = selection.Bottom + Gap;
        else if (selection.Y - Gap - height >= area.Y + Gap) y = selection.Y - Gap - height;
        else y = Clamp(selection.Bottom - Gap - height, area.Y + Gap, area.Bottom - height - Gap);
        return (x, y);
    }

    /// <summary>Подпись «1920 × 1080»: над левым верхним углом выделения; нет места сверху — внутри, у угла.</summary>
    public static (double X, double Y) SizeLabel(DipRect selection, double width, double height, DipRect area)
    {
        var x = Clamp(selection.X, area.X + Gap, area.Right - width - Gap);
        var above = selection.Y - 6 - height;
        var y = above >= area.Y + Gap / 2 ? above : Clamp(selection.Y + 6, area.Y + Gap / 2, area.Bottom - height - Gap);
        return (x, y);
    }

    /// <summary>Размер в пикселях для подписи: «1920 × 1080».</summary>
    public static string SizeText(PixelRect rect) =>
        string.Create(CultureInfo.InvariantCulture, $"{rect.Width} × {rect.Height}");

    private static double Clamp(double v, double min, double max) => max < min ? min : Math.Clamp(v, min, max);
}

/// <summary>Имя файла снимка: «Снимок 2026-10-24 15-30-05.png» / «Screenshot 2026-10-24 15-30-05.png».</summary>
public static class CaptureNaming
{
    /// <summary>Без двоеточий — их нет в именах Windows.</summary>
    public static string FileName(DateTime now) =>
        Loc.F(Loc.T.CaptureFile, now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture)) + ".png";

    /// <summary>Свободный путь: два снимка за секунду — «… (1).png».</summary>
    public static string FilePath(string folder, DateTime now, Func<string, bool>? exists = null) =>
        Jobs.OutputNamer.FirstFree(Path.Combine(folder, FileName(now)), exists);
}
