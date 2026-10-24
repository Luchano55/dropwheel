using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DropWheel.Ui;

namespace DropWheel.Annotate;

/// <summary>
/// Рисует пометки (D-ANNOTATE) в координатах пикселей картинки — одним и тем же кодом в окне (под масштабом
/// «вписать в окно») и в результат (<see cref="Render"/>: полный размер, картинка пиксель в пиксель, без размытия).
/// Размытие берёт пиксели исходной картинки (<see cref="Pixelate"/>); готовые клетки запоминаются для каждой пометки,
/// пока открыто окно.
/// </summary>
internal sealed class AnnotationRenderer
{
    private readonly Dictionary<Annotation, BitmapSource?> _blur = new(ReferenceEqualityComparer.Instance);
    private BitmapSource? _bgra;

    public AnnotationRenderer(BitmapSource image)
    {
        Image = image;
        Width = image.PixelWidth;
        Height = image.PixelHeight;
    }

    public BitmapSource Image { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// Картинка с пометками в полном размере (Pbgra32, 96 DPI: 1 единица = 1 пиксель). Прозрачность исходника
    /// сохраняется. Картинка рисуется ровно в свой размер без сглаживания — её пиксели не меняются.
    /// </summary>
    public BitmapSource Render(IReadOnlyList<Annotation> shapes)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(Image, new Rect(0, 0, Width, Height));
            Draw(dc, shapes, null);
        }
        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Пометки по порядку и (если есть) рисуемая сейчас — поверх.</summary>
    public void Draw(DrawingContext dc, IReadOnlyList<Annotation> shapes, Annotation? current)
    {
        foreach (var shape in shapes) DrawShape(dc, shape, final: true);
        if (current is not null) DrawShape(dc, current, final: false);
    }

    /// <summary>Забыть готовые клетки размытия (окно закрывается).</summary>
    public void Clear()
    {
        _blur.Clear();
        _bgra = null;
    }

    private void DrawShape(DrawingContext dc, Annotation s, bool final)
    {
        var color = ToColor(s.Color);
        var brush = Frozen(new SolidColorBrush(color));
        var stroke = AnnotationMetrics.Stroke(s.Size, Width, Height);
        switch (s.Tool)
        {
            case AnnotationTool.Arrow:
                DrawArrow(dc, s, brush, stroke);
                break;
            case AnnotationTool.Rectangle:
            {
                var (x, y, w, h) = AnnotationGeometry.Normalize(s.Start, s.End);
                dc.DrawRoundedRectangle(null, MakePen(brush, stroke, PenLineCap.Round), new Rect(x, y, w, h), stroke, stroke);
                break;
            }
            case AnnotationTool.Ellipse:
            {
                var (x, y, w, h) = AnnotationGeometry.Normalize(s.Start, s.End);
                dc.DrawEllipse(null, MakePen(brush, stroke, PenLineCap.Round), new Point(x + w / 2, y + h / 2), w / 2, h / 2);
                break;
            }
            case AnnotationTool.Pen:
                DrawPolyline(dc, s.Points, brush, stroke);
                break;
            case AnnotationTool.Marker:
            {
                var marker = Frozen(new SolidColorBrush(Color.FromArgb(AnnotationMetrics.MarkerAlpha, color.R, color.G, color.B)));
                DrawPolyline(dc, s.Points, marker, AnnotationMetrics.Marker(s.Size, Width, Height));
                break;
            }
            case AnnotationTool.Text:
                DrawText(dc, s, brush);
                break;
            case AnnotationTool.Number:
                DrawNumber(dc, s, brush);
                break;
            case AnnotationTool.Blur:
                DrawBlur(dc, s, final);
                break;
        }
    }

    private static void DrawArrow(DrawingContext dc, Annotation s, Brush brush, double stroke)
    {
        if (s.Start.DistanceTo(s.End) < 0.5) return;
        var (shaft, left, right) = AnnotationGeometry.Arrow(s.Start, s.End, stroke);
        var pen = MakePen(brush, stroke, PenLineCap.Round);
        pen.EndLineCap = PenLineCap.Flat;
        dc.DrawLine(pen, P(s.Start), P(shaft));
        var head = new StreamGeometry();
        using (var g = head.Open())
        {
            g.BeginFigure(P(s.End), isFilled: true, isClosed: true);
            g.LineTo(P(left), true, true);
            g.LineTo(P(right), true, true);
        }
        head.Freeze();
        // Тонкая обводка того же цвета скругляет углы наконечника.
        dc.DrawGeometry(brush, MakePen(brush, Math.Max(1, stroke * 0.35), PenLineCap.Round), head);
    }

    private static void DrawPolyline(DrawingContext dc, IReadOnlyList<ImagePoint> points, Brush brush, double thickness)
    {
        if (points.Count == 0) return;
        if (points.Count == 1)
        {
            dc.DrawEllipse(brush, null, P(points[0]), thickness / 2, thickness / 2);
            return;
        }
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(P(points[0]), isFilled: false, isClosed: false);
            g.PolyLineTo(points.Skip(1).Select(P).ToList(), isStroked: true, isSmoothJoin: true);
        }
        line.Freeze();
        dc.DrawGeometry(null, MakePen(brush, thickness, PenLineCap.Round), line);
    }

    /// <summary>Текст Segoe UI Variable (полужирный) с обводкой контрастного цвета — читается на любом фоне.</summary>
    private void DrawText(DrawingContext dc, Annotation s, Brush brush)
    {
        if (string.IsNullOrEmpty(s.Text)) return;
        var size = AnnotationMetrics.FontSize(s.Size, Width, Height);
        var text = MakeText(s.Text, size, brush);
        var geometry = text.BuildGeometry(P(s.Start));
        var outline = Frozen(new SolidColorBrush(AnnotationColors.IsLight(s.Color) ? Color.FromArgb(0xE6, 0x1A, 0x1A, 0x1A) : Colors.White));
        var pen = new Pen(outline, Math.Max(2, size * 0.16)) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, geometry);
        dc.DrawGeometry(brush, null, geometry);
    }

    /// <summary>Кружок с номером: заливка цветом, белое кольцо, цифра белая (на жёлтом и белом — тёмная).</summary>
    private void DrawNumber(DrawingContext dc, Annotation s, Brush brush)
    {
        var r = AnnotationMetrics.NumberRadius(s.Size, Width, Height);
        var center = P(s.Start);
        var light = AnnotationColors.IsLight(s.Color);
        var ring = Frozen(new SolidColorBrush(light ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White));
        dc.DrawEllipse(brush, MakePen(ring, Math.Max(1.5, r * 0.14), PenLineCap.Round), center, r, r);
        var digits = MakeText(s.Number.ToString(CultureInfo.InvariantCulture), r * (s.Number >= 10 ? 1.0 : 1.2), ring);
        // По рамке самих цифр, а не строки текста: цифра стоит точно в центре кружка.
        var geometry = digits.BuildGeometry(new Point(0, 0));
        var b = geometry.Bounds;
        if (b.IsEmpty) return;
        dc.PushTransform(new TranslateTransform(center.X - (b.X + b.Width / 2), center.Y - (b.Y + b.Height / 2)));
        dc.DrawGeometry(ring, null, geometry);
        dc.Pop();
    }

    private void DrawBlur(DrawingContext dc, Annotation s, bool final)
    {
        if (AnnotationGeometry.Region(s.Start, s.End, Width, Height) is not { } r) return;
        var rect = new Rect(r.X, r.Y, r.Width, r.Height);
        if (!final)
        {
            // Пока тянут — только рамка (клетки считаются один раз, когда отпустили мышь).
            var fill = Frozen(new SolidColorBrush(Color.FromArgb(0x55, 0x80, 0x80, 0x80)));
            var pen = new Pen(Frozen(new SolidColorBrush(Colors.White)), Math.Max(1, Math.Min(Width, Height) / 500.0)) { DashStyle = DashStyles.Dash };
            pen.Freeze();
            dc.DrawRectangle(fill, pen, rect);
            return;
        }
        if (!_blur.TryGetValue(s, out var cells))
        {
            cells = MakeCells(r);
            _blur[s] = cells;
        }
        if (cells is not null) dc.DrawImage(cells, rect);
    }

    /// <summary>Клетки размытия из исходных пикселей области (BGRA): ровно размер области, рисуются 1:1.</summary>
    private BitmapSource? MakeCells(Capture.PixelRect r)
    {
        try
        {
            if (_bgra is null)
            {
                BitmapSource source = Image.Format == PixelFormats.Bgra32 ? Image : new FormatConvertedBitmap(Image, PixelFormats.Bgra32, null, 0);
                if (source.CanFreeze && !source.IsFrozen) source.Freeze();
                _bgra = source;
            }
            var stride = r.Width * 4;
            var pixels = new byte[stride * r.Height];
            _bgra.CopyPixels(new Int32Rect(r.X, r.Y, r.Width, r.Height), pixels, stride, 0);
            var block = Pixelate.BlockSize(Width, Height, r.Width, r.Height);
            var cells = Pixelate.Apply(pixels, r.Width, r.Height, stride, block);
            var bitmap = BitmapSource.Create(r.Width, r.Height, 96, 96, PixelFormats.Bgra32, null, cells, stride);
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    // ---------- мелочи ----------

    internal static FormattedText MakeText(string text, double size, Brush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, UiFonts.Face(UiFonts.Display, FontWeights.SemiBold), size, brush, 1.0);

    /// <summary>Размер текста пометки в пикселях картинки — окну, чтобы поставить поле ввода по месту.</summary>
    public double FontSize(StrokeSize size) => AnnotationMetrics.FontSize(size, Width, Height);

    public static Color ToColor(string hex)
    {
        var (r, g, b) = AnnotationColors.Parse(hex);
        return Color.FromRgb(r, g, b);
    }

    private static Point P(ImagePoint p) => new(p.X, p.Y);

    private static Pen MakePen(Brush brush, double thickness, PenLineCap cap)
    {
        var pen = new Pen(brush, thickness) { StartLineCap = cap, EndLineCap = cap, LineJoin = PenLineJoin.Round };
        return pen;
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
