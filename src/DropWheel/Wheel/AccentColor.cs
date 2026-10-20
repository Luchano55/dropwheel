using System.Windows.Media;

namespace DropWheel.Wheel;

/// <summary>Цвет акцента Windows для подсветки сектора; при ошибке — стандартный синий.</summary>
internal static class AccentColor
{
    private static readonly Color Fallback = Color.FromRgb(0x00, 0x78, 0xD4);

    public static Brush Brush
    {
        get
        {
            var b = new SolidColorBrush(Read());
            b.Freeze();
            return b;
        }
    }

    private static Color Read()
    {
        try
        {
            var c = new Windows.UI.ViewManagement.UISettings()
                .GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            return Color.FromRgb(c.R, c.G, c.B);
        }
        catch (Exception)
        {
            return Fallback;
        }
    }
}
