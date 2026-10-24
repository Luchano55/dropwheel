namespace DropWheel.Capture;

/// <summary>
/// Прямоугольник в физических пикселях экрана (координаты виртуального экрана: у мониторов левее или выше
/// основного они отрицательные). Правый и нижний края не входят: ширина = Right − X.
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public long Area => IsEmpty ? 0 : (long)Width * Height;

    public static PixelRect FromEdges(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>Общая часть; не пересекаются — пустой прямоугольник (Width = Height = 0).</summary>
    public PixelRect Intersect(PixelRect other)
    {
        var l = Math.Max(X, other.X);
        var t = Math.Max(Y, other.Y);
        var r = Math.Min(Right, other.Right);
        var b = Math.Min(Bottom, other.Bottom);
        return r > l && b > t ? FromEdges(l, t, r, b) : default;
    }

    /// <summary>Наименьший прямоугольник, содержащий оба (пустые не учитываются).</summary>
    public PixelRect Union(PixelRect other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        return FromEdges(Math.Min(X, other.X), Math.Min(Y, other.Y), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
    }

    /// <summary>Квадрат расстояния от точки до прямоугольника (0 — точка внутри).</summary>
    public long DistanceSquared(int x, int y)
    {
        long dx = x < X ? X - x : x >= Right ? x - (Right - 1) : 0;
        long dy = y < Y ? Y - y : y >= Bottom ? y - (Bottom - 1) : 0;
        return dx * dx + dy * dy;
    }

    public override string ToString() => $"{Width}×{Height}+{X}+{Y}";
}

/// <summary>Прямоугольник в DIP (единицы WPF) внутри окна одного монитора.</summary>
public readonly record struct DipRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>Монитор: границы в физических пикселях и его эффективный DPI (96 = 100 %, 144 = 150 %).</summary>
public sealed record CaptureMonitor(PixelRect Bounds, uint Dpi)
{
    /// <summary>Сколько физических пикселей в одном DIP (1.5 при 150 %).</summary>
    public double Scale => (Dpi == 0 ? 96 : Dpi) / 96.0;

    /// <summary>Размер окна монитора в DIP.</summary>
    public double DipWidth => Bounds.Width / Scale;
    public double DipHeight => Bounds.Height / Scale;

    /// <summary>Точка экрана (физические пиксели) → DIP внутри окна этого монитора (0,0 — его левый верхний угол).</summary>
    public (double X, double Y) ToLocalDip(int x, int y) => ((x - Bounds.X) / Scale, (y - Bounds.Y) / Scale);

    /// <summary>Прямоугольник экрана → DIP внутри окна этого монитора (может выходить за окно — WPF обрежет).</summary>
    public DipRect ToLocalDip(PixelRect r) =>
        new((r.X - Bounds.X) / Scale, (r.Y - Bounds.Y) / Scale, r.Width / Scale, r.Height / Scale);

    /// <summary>Точка окна монитора в DIP → физические пиксели экрана (с округлением до пикселя).</summary>
    public (int X, int Y) ToScreen(double dipX, double dipY) =>
        (Bounds.X + (int)Math.Round(dipX * Scale), Bounds.Y + (int)Math.Round(dipY * Scale));
}
