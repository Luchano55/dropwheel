using System.Windows;
using System.Windows.Media;

namespace DropWheel.Wheel;

/// <summary>
/// «Плитка» колеса: кольцевой сектор с зазором до соседей и скруглёнными углами, центр — (0, 0).
/// Та же форма у секторов колеса, у значка программы и у логотипа в окне задач.
/// </summary>
public static class TileGeometry
{
    /// <param name="index">Номер сектора (0 — сверху, дальше по часовой).</param>
    /// <param name="count">Сколько секторов; 1 — целое кольцо.</param>
    /// <param name="gap">Ширина зазора между соседними плитками (одинаковая по всей длине).</param>
    /// <param name="corner">Радиус скругления углов.</param>
    /// <param name="rotateDeg">Поворот всего кольца по часовой стрелке.</param>
    public static Geometry Build(int index, int count, double inner, double outer, double gap, double corner,
        double rotateDeg = 0)
    {
        if (count == 1)
        {
            var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
            ring.Children.Add(new EllipseGeometry(new Point(), outer, outer));
            ring.Children.Add(new EllipseGeometry(new Point(), inner, inner));
            ring.Freeze();
            return ring;
        }

        if (corner <= 0)
        {
            var plain = Sector(index, count, inner, outer, gap / 2, rotateDeg);
            plain.Freeze();
            return plain;
        }

        // Скругление: строим плитку меньше на радиус скругления и обводим пером толщиной 2×радиус
        // с круглыми углами; объединение — плитка нужного размера со скруглёнными углами.
        var k = corner;
        var core = Sector(index, count, inner + k, outer - k, gap / 2 + k, rotateDeg);
        var pen = new Pen(Brushes.Black, 2 * k) { LineJoin = PenLineJoin.Round };
        var tile = Geometry.Combine(core, core.GetWidenedPathGeometry(pen), GeometryCombineMode.Union, null);
        tile.Freeze();
        return tile;
    }

    /// <summary>Кольцевой сектор между радиусами ri и ro; боковые края параллельны границе сектора на расстоянии halfGap.</summary>
    private static Geometry Sector(int index, int count, double ri, double ro, double halfGap, double rotateDeg)
    {
        var (start, end) = WheelGeometry.SectorAngles(index, count);
        start += rotateDeg;
        end += rotateDeg;
        double Offset(double r) => Math.Asin(Math.Min(1, halfGap / r)) * 180 / Math.PI;
        var so = start + Offset(ro);
        var eo = end - Offset(ro);
        var si = start + Offset(ri);
        var ei = end - Offset(ri);

        static Point At(double deg, double r)
        {
            var (x, y) = WheelGeometry.PointAt(deg, r);
            return new Point(x, y);
        }

        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(At(so, ro), isFilled: true, isClosed: true);
            ctx.ArcTo(At(eo, ro), new Size(ro, ro), 0, eo - so > 180, SweepDirection.Clockwise, true, true);
            ctx.LineTo(At(ei, ri), true, true);
            ctx.ArcTo(At(si, ri), new Size(ri, ri), 0, ei - si > 180, SweepDirection.Counterclockwise, true, true);
        }
        return g;
    }
}
