using System.Windows;
using System.Windows.Media;

namespace DropWheel.Ui;

/// <summary>
/// Шрифты интерфейса. Windows 11 — Segoe UI Variable (если WPF видит у него настоящие начертания),
/// иначе Segoe UI. Значки — Segoe Fluent Icons (Windows 11) или Segoe MDL2 Assets (Windows 10):
/// коды значков у них общие, WPF сам берёт первый найденный шрифт из списка.
/// </summary>
public static class UiFonts
{
    private static FontFamily? _text;
    private static FontFamily? _display;

    /// <summary>Значки (коды — <see cref="Glyphs"/>).</summary>
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>Обычный текст (до ~14 px).</summary>
    public static FontFamily Text => _text ??= Pick("Segoe UI Variable Text");

    /// <summary>Крупные подписи (от ~16 px).</summary>
    public static FontFamily Display => _display ??= Pick("Segoe UI Variable Display");

    public static Typeface Face(FontFamily family, FontWeight weight) =>
        new(family, FontStyles.Normal, weight, FontStretches.Normal);

    /// <summary>
    /// Segoe UI Variable — шрифт с переменной толщиной. Берём его, только если у семейства есть
    /// настоящие Regular и SemiBold (без «нарисованного» жирного), иначе — проверенный Segoe UI.
    /// </summary>
    private static FontFamily Pick(string variable)
    {
        const string fallback = "Segoe UI";
        try
        {
            var family = new FontFamily(variable);
            if (IsReal(family, FontWeights.Normal) && IsReal(family, FontWeights.SemiBold))
                return new FontFamily($"{variable}, {fallback}");
        }
        catch (Exception)
        {
        }
        return new FontFamily(fallback);
    }

    private static bool IsReal(FontFamily family, FontWeight weight)
    {
        var face = Face(family, weight);
        return face.TryGetGlyphTypeface(out var glyphs)
               && glyphs.StyleSimulations == StyleSimulations.None
               && glyphs.Weight == weight
               // Нет шрифта — WPF молча подставит Segoe UI; такой результат не считается.
               && glyphs.FamilyNames.Values.Any(n => n.StartsWith("Segoe UI Variable", StringComparison.OrdinalIgnoreCase));
    }
}
