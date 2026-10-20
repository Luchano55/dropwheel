using System.Windows;
using System.Windows.Media;
using DropWheel.Wheel;

namespace DropWheel.Ui;

/// <summary>
/// Значок DropWheel, нарисованный векторно (как docs/assets/icon.svg): четыре плитки кольца,
/// верхняя правая — тёплая и приподнята. Для мелких размеров — упрощённый вариант (icon-small.svg).
/// </summary>
public sealed class LogoMark : FrameworkElement
{
    private static readonly Brush Cool = Gradient(30, 30, 226, 226,
        (0, Color.FromRgb(0x52, 0xB4, 0xFF)), (0.55, Color.FromRgb(0x2F, 0x6C, 0xF5)), (1, Color.FromRgb(0x4B, 0x3C, 0xD6)));
    private static readonly Brush Warm = Gradient(150, 20, 240, 110,
        (0, Color.FromRgb(0xFF, 0xC9, 0x4D)), (1, Color.FromRgb(0xFF, 0x74, 0x33)));
    private static readonly Brush Hub = Gradient(103, 103, 153, 153,
        (0, Color.FromRgb(0x4F, 0x95, 0xFF)), (1, Color.FromRgb(0x2B, 0x4F, 0xD6)));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(LogoMark),
        new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Сторона значка, DIP.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize) => new(Size, Size);

    protected override void OnRender(DrawingContext dc)
    {
        var size = Size;
        var small = size < 28;
        // Чертёж — в сетке 256×256 (мелкий вариант — 16×16, растянутой до 256).
        dc.PushTransform(new ScaleTransform(size / 256, size / 256));
        double inner, outer, gap, corner, lift, hub;
        if (small) (inner, outer, gap, corner, lift, hub) = (56, 128, 32, 14.4, 0, 28);
        else (inner, outer, gap, corner, lift, hub) = (60, 112, 16, 13, 10, 25);

        for (var i = 0; i < 4; i++)
        {
            var hot = i == 0;
            var (lx, ly) = hot ? WheelGeometry.PointAt(WheelGeometry.MidAngle(i, 4) + 45, lift) : (0, 0);
            // Сдвигаем саму фигуру, а не холст: градиенты заданы в координатах всего значка.
            var tile = new GeometryGroup { Transform = new TranslateTransform(128 + lx, 128 + ly) };
            tile.Children.Add(TileGeometry.Build(i, 4, inner, outer, gap, corner, 45));
            dc.DrawGeometry(hot ? Warm : Cool, null, tile);
        }
        dc.DrawEllipse(Hub, null, new Point(128, 128), hub, hub);
        dc.Pop();
    }

    private static Brush Gradient(double x1, double y1, double x2, double y2, params (double Offset, Color Color)[] stops)
    {
        var b = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(x1, y1),
            EndPoint = new Point(x2, y2),
        };
        foreach (var (offset, color) in stops) b.GradientStops.Add(new GradientStop(color, offset));
        b.Freeze();
        return b;
    }
}
