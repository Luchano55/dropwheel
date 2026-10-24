using System.Globalization;

namespace DropWheel.Annotate;

/// <summary>Инструмент окна «Пометки» (решение D-ANNOTATE).</summary>
public enum AnnotationTool
{
    Arrow,
    Rectangle,
    Ellipse,
    Pen,
    Marker,
    Text,
    Blur,
    Number,
}

/// <summary>Толщина линии: три размера (от размера картинки, см. <see cref="AnnotationMetrics"/>).</summary>
public enum StrokeSize
{
    Thin,
    Medium,
    Thick,
}

/// <summary>Точка в пикселях картинки (дробная: мышь на уменьшенной картинке попадает между пикселями).</summary>
public readonly record struct ImagePoint(double X, double Y)
{
    public double DistanceTo(ImagePoint other) => Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{X:0.#},{Y:0.#}");
}

/// <summary>
/// Одна пометка — векторная, в пикселях исходной картинки (не экрана): так её можно нарисовать и на уменьшенной
/// картинке в окне, и на полной при сохранении. Points: у стрелки, рамки, эллипса и размытия — начало и конец;
/// у карандаша и маркера — вся линия; у текста — левый верхний угол; у номера — центр кружка.
/// </summary>
public sealed record Annotation(AnnotationTool Tool, IReadOnlyList<ImagePoint> Points, string Color, StrokeSize Size)
{
    /// <summary>Текст пометки «Текст» (может быть в несколько строк).</summary>
    public string? Text { get; init; }

    /// <summary>Номер кружка «Номер»: 1, 2, 3…</summary>
    public int Number { get; init; }

    public ImagePoint Start => Points[0];
    public ImagePoint End => Points[^1];

    /// <summary>Двухточечная фигура (стрелка, рамка, эллипс, размытие).</summary>
    public bool IsTwoPoint => Tool is AnnotationTool.Arrow or AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.Blur;

    /// <summary>
    /// Стоит ли сохранять только что нарисованную фигуру: щелчок без движения не даёт ни стрелки, ни рамки
    /// (порог — в пикселях картинки), пустой текст не сохраняется.
    /// </summary>
    public bool IsMeaningful(double minSize = 3) => Tool switch
    {
        AnnotationTool.Text => !string.IsNullOrWhiteSpace(Text),
        AnnotationTool.Number => Number > 0,
        AnnotationTool.Pen or AnnotationTool.Marker => Points.Count >= 1,
        AnnotationTool.Arrow => Start.DistanceTo(End) >= minSize,
        _ => Math.Abs(End.X - Start.X) >= minSize && Math.Abs(End.Y - Start.Y) >= minSize,
    };

    /// <summary>Та же пометка, сдвинутая на (dx, dy) пикселей картинки.</summary>
    public Annotation Offset(double dx, double dy) =>
        this with { Points = Points.Select(p => new ImagePoint(p.X + dx, p.Y + dy)).ToList() };
}

/// <summary>Цвета палитры: восемь — яркие для пометок, чёрный и белый.</summary>
public static class AnnotationColors
{
    public const string Red = "#E81123";
    public const string Orange = "#FF8C00";
    public const string Yellow = "#FFD400";
    public const string Green = "#16C60C";
    public const string Blue = "#0078D4";
    public const string Purple = "#8E44EC";
    public const string Black = "#1A1A1A";
    public const string White = "#FFFFFF";

    public static readonly IReadOnlyList<string> All = [Red, Orange, Yellow, Green, Blue, Purple, Black, White];

    /// <summary>Цвет по умолчанию — красный: заметен почти на любом снимке.</summary>
    public const string Default = Red;

    /// <summary>«#RRGGBB» → байты; неверная строка — красный.</summary>
    public static (byte R, byte G, byte B) Parse(string? hex)
    {
        if (hex is { Length: 7 } && hex[0] == '#'
            && int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            return ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return Parse(Default);
    }

    /// <summary>Светлый цвет (жёлтый, белый): обводка текста и цифра в кружке — тёмные, иначе — белые.</summary>
    public static bool IsLight(string? hex)
    {
        var (r, g, b) = Parse(hex);
        return 0.299 * r + 0.587 * g + 0.114 * b > 160;
    }
}

/// <summary>
/// Размеры пометок в пикселях картинки: от её короткой стороны, чтобы стрелка на снимке 4K была так же заметна,
/// как на снимке окна 800 × 600. Основа — короткая сторона / 250, но не меньше 2 и не больше 40 px.
/// </summary>
public static class AnnotationMetrics
{
    public static double Base(int imageW, int imageH) => Math.Clamp(Math.Min(imageW, imageH) / 250.0, 2, 40);

    private static double Factor(StrokeSize size) => size switch
    {
        StrokeSize.Thin => 0.6,
        StrokeSize.Thick => 1.8,
        _ => 1,
    };

    /// <summary>Толщина линии стрелки, рамки, эллипса, карандаша.</summary>
    public static double Stroke(StrokeSize size, int imageW, int imageH) => Math.Max(1.5, Base(imageW, imageH) * Factor(size));

    /// <summary>Маркер — широкая полупрозрачная полоса.</summary>
    public static double Marker(StrokeSize size, int imageW, int imageH) => Base(imageW, imageH) * 4 * Factor(size);

    /// <summary>Непрозрачность маркера (0…255): текст под ним остаётся читаемым.</summary>
    public const byte MarkerAlpha = 0x66;

    /// <summary>Размер шрифта текста.</summary>
    public static double FontSize(StrokeSize size, int imageW, int imageH) => Math.Max(12, Base(imageW, imageH) * 5.5 * Factor(size));

    /// <summary>Радиус кружка с номером.</summary>
    public static double NumberRadius(StrokeSize size, int imageW, int imageH) => Math.Max(9, Base(imageW, imageH) * 3.5 * Factor(size));

    /// <summary>Длина наконечника стрелки: от толщины, но не длиннее 40 % самой стрелки.</summary>
    public static double ArrowHead(double stroke, double length) => Math.Min(Math.Max(stroke * 4, 10), length * 0.4);
}
