using System.Globalization;

namespace DropWheel.Documents;

/// <summary>Размер листа и поля (мм) — для CSS @page и для печати в PDF через Edge.</summary>
public sealed record PageSetup(double WidthMm, double HeightMm, double TopMm, double RightMm, double BottomMm, double LeftMm)
{
    public static PageSetup A4(double margin) => new(210, 297, margin, margin, margin, margin);

    public double ContentWidthMm => WidthMm - LeftMm - RightMm;

    public string CssPageRule => string.Create(CultureInfo.InvariantCulture,
        $"@page {{ size: {WidthMm:0.#}mm {HeightMm:0.#}mm; margin: {TopMm:0.#}mm {RightMm:0.#}mm {BottomMm:0.#}mm {LeftMm:0.#}mm; }}");

    public static double Inches(double mm) => mm / 25.4;

    /// <summary>Защита от странных значений в файле: лист 50…2000 мм, поля не больше трети листа.</summary>
    public PageSetup Sane()
    {
        static double Clamp(double v, double min, double max) => double.IsFinite(v) ? Math.Clamp(v, min, max) : min;
        var w = Clamp(WidthMm, 50, 2000);
        var h = Clamp(HeightMm, 50, 2000);
        return new(w, h, Clamp(TopMm, 0, h / 3), Clamp(RightMm, 0, w / 3), Clamp(BottomMm, 0, h / 3), Clamp(LeftMm, 0, w / 3));
    }
}
