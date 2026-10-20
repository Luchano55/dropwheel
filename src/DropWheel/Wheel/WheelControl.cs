using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DropWheel.Conversions;

namespace DropWheel.Wheel;

/// <summary>
/// Круговое меню: кольцо секторов, «Отмена» в центре, подсветка сектора под курсором.
/// Рисуется целиком в OnRender; центр колеса — центр элемента.
/// </summary>
public sealed class WheelControl : FrameworkElement
{
    /// <summary>Радиус центрального круга («Отмена»), DIP.</summary>
    public const double InnerRadius = 55;
    /// <summary>Внешний радиус кольца секторов, DIP.</summary>
    public const double OuterRadius = 130;
    private const double DiscRadius = OuterRadius + 6;

    private const string NothingText = "Нечего предложить";
    private const string CancelText = "Отмена";

    private static readonly Typeface Regular = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface SemiBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static readonly Brush DiscBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xE6, 0x1E, 0x1E, 0x1E)));
    private static readonly Brush CenterBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xFF, 0x2B, 0x2B, 0x2B)));
    private static readonly Brush CenterHoverBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xFF, 0x45, 0x45, 0x45)));
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Colors.White));
    private static readonly Brush DimTextBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)));
    private static readonly Pen RimPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), 1));
    private static readonly Pen SeparatorPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), 1));

    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IReadOnlyList<WheelItem>), typeof(WheelControl),
        new FrameworkPropertyMetadata(Array.Empty<WheelItem>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(OperationMode), typeof(WheelControl),
        new FrameworkPropertyMetadata(OperationMode.Convert, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HoverIndexProperty = DependencyProperty.Register(
        nameof(HoverIndex), typeof(int?), typeof(WheelControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsCenterHoveredProperty = DependencyProperty.Register(
        nameof(IsCenterHovered), typeof(bool), typeof(WheelControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsPendingProperty = DependencyProperty.Register(
        nameof(IsPending), typeof(bool), typeof(WheelControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private Brush _accent = AccentColor.Brush;

    /// <summary>Сектора (не больше 8). Пусто — «Нечего предложить».</summary>
    public IReadOnlyList<WheelItem> Items
    {
        get => (IReadOnlyList<WheelItem>)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value ?? Array.Empty<WheelItem>());
    }

    /// <summary>Режим — для подписи «Конвертация» / «Инструменты».</summary>
    public OperationMode Mode
    {
        get => (OperationMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    /// <summary>Подсвеченный сектор; null или −1 — нет.</summary>
    public int? HoverIndex
    {
        get => (int?)GetValue(HoverIndexProperty);
        set => SetValue(HoverIndexProperty, value is < 0 ? null : value);
    }

    /// <summary>Курсор над центром — «Отмена» крупно.</summary>
    public bool IsCenterHovered
    {
        get => (bool)GetValue(IsCenterHoveredProperty);
        set => SetValue(IsCenterHoveredProperty, value);
    }

    /// <summary>
    /// Сектора ещё не известны (колесо показано, DragEnter ещё не пришёл):
    /// при пустых Items рисуется только «Отмена», без «Нечего предложить».
    /// </summary>
    public bool IsPending
    {
        get => (bool)GetValue(IsPendingProperty);
        set => SetValue(IsPendingProperty, value);
    }

    /// <summary>Сектор под точкой (в координатах элемента); null — центр, вне кольца или пусто.</summary>
    public int? HitTest(Point p)
    {
        var (dx, dy) = Offset(p);
        return WheelGeometry.HitTest(dx, dy, Items.Count, InnerRadius, OuterRadius);
    }

    /// <summary>Точка в центральном круге.</summary>
    public bool IsCenter(Point p)
    {
        var (dx, dy) = Offset(p);
        return WheelGeometry.IsCenter(dx, dy, InnerRadius);
    }

    /// <summary>Точка на диске колеса (вне диска — прозрачный угол окна).</summary>
    public bool IsOnDisc(Point p)
    {
        var (dx, dy) = Offset(p);
        return dx * dx + dy * dy <= DiscRadius * DiscRadius;
    }

    /// <summary>Перечитать цвет акцента Windows (после смены темы).</summary>
    public void RefreshAccent()
    {
        _accent = AccentColor.Brush;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(DiscRadius * 2, DiscRadius * 2);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        dc.DrawEllipse(DiscBrush, RimPen, c, DiscRadius, DiscRadius);

        var items = Items;
        if (items.Count == 0 && IsPending)
        {
            dc.DrawEllipse(null, SeparatorPen, c, OuterRadius, OuterRadius);
            DrawCenter(dc, c, null, dpi);
            return;
        }
        if (items.Count == 0)
        {
            DrawText(dc, NothingText, c, 18, SemiBold, TextBrush, 2 * InnerRadius + 60, dpi);
            return;
        }

        var hover = HoverIndex is { } h && h < items.Count ? h : (int?)null;

        // Сектора: подсветка, разделители, подписи.
        if (hover is { } hi)
            dc.DrawGeometry(_accent, null, SectorGeometry(c, hi, items.Count));

        if (items.Count > 1)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var (start, _) = WheelGeometry.SectorAngles(i, items.Count);
                dc.DrawLine(SeparatorPen, At(c, start, InnerRadius), At(c, start, OuterRadius));
            }
        }

        var labelRadius = (InnerRadius + OuterRadius) / 2;
        var labelWidth = items.Count switch { 1 => 140.0, 2 => 120, 3 or 4 => 96, _ => 72 };
        for (var i = 0; i < items.Count; i++)
        {
            var p = At(c, WheelGeometry.MidAngle(i, items.Count), labelRadius);
            DrawText(dc, items[i].Label, p, 13, i == hover ? SemiBold : Regular, TextBrush, labelWidth, dpi);
        }

        DrawCenter(dc, c, hover is { } hl ? items[hl].Label : null, dpi);
    }

    /// <summary>Центр: подпись режима и «Отмена» либо крупная подпись сектора под курсором.</summary>
    private void DrawCenter(DrawingContext dc, Point c, string? hoverLabel, double dpi)
    {
        dc.DrawEllipse(IsCenterHovered ? CenterHoverBrush : CenterBrush, RimPen, c, InnerRadius, InnerRadius);
        var modeCaption = Mode == OperationMode.Convert ? "Конвертация" : "Инструменты";
        DrawText(dc, modeCaption, new Point(c.X, c.Y - 30), 10.5, Regular, DimTextBrush, 2 * InnerRadius - 16, dpi, maxLines: 1);

        if (hoverLabel is not null)
            DrawText(dc, hoverLabel, c, 18, SemiBold, TextBrush, 2 * InnerRadius - 14, dpi);
        else
            DrawText(dc, CancelText, c, IsCenterHovered ? 18 : 14, IsCenterHovered ? SemiBold : Regular,
                IsCenterHovered ? TextBrush : DimTextBrush, 2 * InnerRadius - 14, dpi);
    }

    private (double Dx, double Dy) Offset(Point p) => (p.X - ActualWidth / 2, p.Y - ActualHeight / 2);

    private static Point At(Point c, double angleDeg, double radius)
    {
        var (x, y) = WheelGeometry.PointAt(angleDeg, radius);
        return new Point(c.X + x, c.Y + y);
    }

    private static Geometry SectorGeometry(Point c, int index, int count)
    {
        if (count == 1)
        {
            var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
            ring.Children.Add(new EllipseGeometry(c, OuterRadius, OuterRadius));
            ring.Children.Add(new EllipseGeometry(c, InnerRadius, InnerRadius));
            ring.Freeze();
            return ring;
        }

        var (start, end) = WheelGeometry.SectorAngles(index, count);
        var large = end - start > 180;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(At(c, start, OuterRadius), isFilled: true, isClosed: true);
            ctx.ArcTo(At(c, end, OuterRadius), new Size(OuterRadius, OuterRadius), 0, large, SweepDirection.Clockwise, true, false);
            ctx.LineTo(At(c, end, InnerRadius), true, false);
            ctx.ArcTo(At(c, start, InnerRadius), new Size(InnerRadius, InnerRadius), 0, large, SweepDirection.Counterclockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>Текст, отцентрованный в точке center; переносится по словам в пределах maxWidth.</summary>
    private static void DrawText(DrawingContext dc, string text, Point center, double size, Typeface face,
        Brush brush, double maxWidth, double pixelsPerDip, int maxLines = 2)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            face, size, brush, pixelsPerDip)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = maxLines,
            TextAlignment = TextAlignment.Center,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        dc.DrawText(ft, new Point(center.X - maxWidth / 2, center.Y - ft.Height / 2));
    }

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
