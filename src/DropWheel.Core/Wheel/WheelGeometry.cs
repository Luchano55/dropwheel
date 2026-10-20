namespace DropWheel.Wheel;

/// <summary>
/// Геометрия колеса без UI. Углы — в градусах по часовой стрелке от 12 часов;
/// смещения — от центра, ось Y направлена вниз (экранные координаты).
/// </summary>
public static class WheelGeometry
{
    /// <summary>Больше секторов колесо не показывает.</summary>
    public const int MaxSectors = 8;

    /// <summary>
    /// Границы сектора. Сектор 0 — сверху (центр на 12 часах), дальше по часовой.
    /// Start у сектора 0 отрицательный (−180/count), End = Start + 360/count.
    /// </summary>
    public static (double StartDeg, double EndDeg) SectorAngles(int index, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count);

        var span = 360.0 / count;
        var start = index * span - span / 2;
        return (start, start + span);
    }

    /// <summary>Угол середины сектора (там пишется подпись).</summary>
    public static double MidAngle(int index, int count) => index * (360.0 / count);

    /// <summary>
    /// Номер сектора под точкой. null — центр («Отмена», r &lt; innerRadius),
    /// за внешним краем (r &gt; outerRadius) или секторов нет.
    /// Точка ровно на границе секторов относится к следующему по часовой.
    /// </summary>
    public static int? HitTest(double dx, double dy, int count, double innerRadius, double outerRadius)
    {
        if (count <= 0) return null;
        var r = Math.Sqrt(dx * dx + dy * dy);
        if (r < innerRadius || r > outerRadius) return null;

        var span = 360.0 / count;
        var index = (int)Math.Floor((AngleOf(dx, dy) + span / 2) / span);
        return index % count;
    }

    /// <summary>Точка в центральном круге («Отмена»).</summary>
    public static bool IsCenter(double dx, double dy, double innerRadius) =>
        dx * dx + dy * dy < innerRadius * innerRadius;

    /// <summary>Угол точки: 0 — вверх, 90 — вправо, 180 — вниз, 270 — влево; диапазон [0, 360).</summary>
    public static double AngleOf(double dx, double dy)
    {
        var deg = Math.Atan2(dx, -dy) * 180 / Math.PI;
        return deg < 0 ? deg + 360 : deg;
    }

    /// <summary>Точка на окружности радиуса radius под углом angleDeg (смещение от центра, Y вниз).</summary>
    public static (double X, double Y) PointAt(double angleDeg, double radius)
    {
        var rad = angleDeg * Math.PI / 180;
        return (radius * Math.Sin(rad), -radius * Math.Cos(rad));
    }

    /// <summary>Размер в физических пикселях для размера в DIP при данном DPI монитора (96 = 100%).</summary>
    public static int ToPixels(double dip, double dpi) => (int)Math.Round(dip * dpi / 96.0);

    /// <summary>
    /// Левый верхний угол квадратного окна size×size с центром в (x, y),
    /// сдвинутого так, чтобы целиком влезть в рабочую область монитора (всё в физических пикселях).
    /// Если окно больше области — прижимается к левому/верхнему краю.
    /// </summary>
    public static (int Left, int Top) PlaceInside(int x, int y, int size,
        int areaLeft, int areaTop, int areaRight, int areaBottom)
    {
        var left = x - size / 2;
        var top = y - size / 2;
        left = Math.Max(areaLeft, Math.Min(left, areaRight - size));
        top = Math.Max(areaTop, Math.Min(top, areaBottom - size));
        return (left, top);
    }
}
