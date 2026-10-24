using DropWheel.Capture;

namespace DropWheel.Annotate;

/// <summary>
/// Картинка вписана в область окна (D-ANNOTATE): масштаб и сдвиг, перевод точек окна (DIP) в пиксели картинки и обратно.
/// Маленькая картинка не растягивается больше, чем «пиксель в пиксель» на экране (maxScale = 1 / масштаб Windows).
/// </summary>
public readonly record struct ViewMapping(double Scale, double OffsetX, double OffsetY, int ImageW, int ImageH)
{
    public static ViewMapping Fit(int imageW, int imageH, double viewW, double viewH, double maxScale = 1)
    {
        if (imageW < 1 || imageH < 1 || viewW <= 0 || viewH <= 0) return new ViewMapping(1, 0, 0, Math.Max(1, imageW), Math.Max(1, imageH));
        var scale = Math.Min(Math.Min(viewW / imageW, viewH / imageH), maxScale);
        var w = imageW * scale;
        var h = imageH * scale;
        return new ViewMapping(scale, (viewW - w) / 2, (viewH - h) / 2, imageW, imageH);
    }

    /// <summary>Картинка на экране: левый верхний угол, ширина и высота в DIP.</summary>
    public (double X, double Y, double W, double H) Shown => (OffsetX, OffsetY, ImageW * Scale, ImageH * Scale);

    public ImagePoint ToImage(double viewX, double viewY) => new((viewX - OffsetX) / Scale, (viewY - OffsetY) / Scale);

    public (double X, double Y) ToView(ImagePoint p) => (OffsetX + p.X * Scale, OffsetY + p.Y * Scale);

    /// <summary>Точка, прижатая к картинке (мышь ушла за край — фигура доходит до края, а не дальше).</summary>
    public ImagePoint Clamp(ImagePoint p) => new(Math.Clamp(p.X, 0, ImageW), Math.Clamp(p.Y, 0, ImageH));

    public bool Contains(ImagePoint p) => p.X >= 0 && p.Y >= 0 && p.X <= ImageW && p.Y <= ImageH;
}

/// <summary>Геометрия фигур без WPF: Shift-привязка, наконечник стрелки, прямоугольник по двум точкам.</summary>
public static class AnnotationGeometry
{
    /// <summary>
    /// Shift при рисовании: стрелка — по углу, кратному 45°; рамка, эллипс, размытие — квадрат (круг)
    /// со стороной по большему смещению, в ту же сторону, куда тянут.
    /// </summary>
    public static ImagePoint Constrain(AnnotationTool tool, ImagePoint start, ImagePoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (tool == AnnotationTool.Arrow)
        {
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length == 0) return end;
            var angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
            return new ImagePoint(start.X + Math.Round(Math.Cos(angle) * length, 6), start.Y + Math.Round(Math.Sin(angle) * length, 6));
        }
        if (tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.Blur)
        {
            var side = Math.Max(Math.Abs(dx), Math.Abs(dy));
            return new ImagePoint(start.X + (dx < 0 ? -side : side), start.Y + (dy < 0 ? -side : side));
        }
        return end;
    }

    /// <summary>Прямоугольник по двум углам в любую сторону: (x, y, ширина, высота).</summary>
    public static (double X, double Y, double W, double H) Normalize(ImagePoint a, ImagePoint b) =>
        (Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    /// <summary>
    /// Стрелка: конец древка (у основания наконечника) и два угла наконечника. Острие — в end, наконечник —
    /// треугольник с углом ~50°; древко кончается у его основания, чтобы скруглённый конец линии не торчал из острия.
    /// </summary>
    public static (ImagePoint ShaftEnd, ImagePoint Left, ImagePoint Right) Arrow(ImagePoint start, ImagePoint end, double stroke)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length == 0) return (end, end, end);
        var ux = dx / length;
        var uy = dy / length;
        var head = AnnotationMetrics.ArrowHead(stroke, length);
        var half = head * 0.48;
        var baseX = end.X - ux * head;
        var baseY = end.Y - uy * head;
        var shaft = new ImagePoint(end.X - ux * head * 0.6, end.Y - uy * head * 0.6);
        return (shaft, new ImagePoint(baseX - uy * half, baseY + ux * half), new ImagePoint(baseX + uy * half, baseY - ux * half));
    }

    /// <summary>
    /// Область размытия в целых пикселях картинки: по двум углам, подрезанная по картинке (наружу — округление вверх).
    /// Меньше 2 px по стороне — null (нечего размывать).
    /// </summary>
    public static PixelRect? Region(ImagePoint a, ImagePoint b, int imageW, int imageH)
    {
        var (x, y, w, h) = Normalize(a, b);
        var left = (int)Math.Floor(Math.Clamp(x, 0, imageW));
        var top = (int)Math.Floor(Math.Clamp(y, 0, imageH));
        var right = (int)Math.Ceiling(Math.Clamp(x + w, 0, imageW));
        var bottom = (int)Math.Ceiling(Math.Clamp(y + h, 0, imageH));
        return right - left >= 2 && bottom - top >= 2 ? PixelRect.FromEdges(left, top, right, bottom) : null;
    }
}

/// <summary>
/// Размытие (D-ANNOTATE) — крупные квадраты-«пиксели» из исходных пикселей: область делится на клетки, каждая
/// заливается средним цветом своих пикселей (то же, что уменьшить и увеличить без сглаживания). Текст и лица под ней
/// не читаются, а восстановить их по клеткам нельзя: средний цвет клетки не хранит, что было внутри.
/// </summary>
public static class Pixelate
{
    /// <summary>
    /// Сторона клетки в пикселях: от короткой стороны картинки (/ 80), не меньше 8 и не больше 64; у маленькой
    /// области — не больше половины её стороны, чтобы клеток было хотя бы 2 × 2.
    /// </summary>
    public static int BlockSize(int imageW, int imageH, int regionW, int regionH)
    {
        var block = Math.Clamp((int)Math.Round(Math.Min(imageW, imageH) / 80.0), 8, 64);
        return Math.Max(1, Math.Min(block, Math.Min(regionW, regionH) / 2));
    }

    /// <summary>Сколько клеток по ширине и высоте (последние клетки у края могут быть неполными).</summary>
    public static (int Columns, int Rows) Grid(int regionW, int regionH, int block) =>
        ((regionW + block - 1) / block, (regionH + block - 1) / block);

    /// <summary>
    /// Пиксели области в формате BGRA (4 байта, построчно, stride ≥ w·4) → новые пиксели того же размера (stride = w·4),
    /// где каждая клетка block × block — средний цвет (с учётом прозрачности: цвет усредняется с весом альфы).
    /// </summary>
    public static byte[] Apply(ReadOnlySpan<byte> bgra, int width, int height, int stride, int block)
    {
        if (width < 1 || height < 1) return [];
        if (block < 1) throw new ArgumentOutOfRangeException(nameof(block));
        if (stride < width * 4 || bgra.Length < (long)stride * (height - 1) + width * 4)
            throw new ArgumentException("Мало пикселей для области", nameof(bgra));
        var output = new byte[width * height * 4];
        for (var by = 0; by < height; by += block)
        {
            var bh = Math.Min(block, height - by);
            for (var bx = 0; bx < width; bx += block)
            {
                var bw = Math.Min(block, width - bx);
                long b = 0, g = 0, r = 0, a = 0;
                for (var y = by; y < by + bh; y++)
                {
                    var row = y * stride;
                    for (var x = bx; x < bx + bw; x++)
                    {
                        var i = row + x * 4;
                        int pa = bgra[i + 3];
                        b += bgra[i] * pa;
                        g += bgra[i + 1] * pa;
                        r += bgra[i + 2] * pa;
                        a += pa;
                    }
                }
                var count = bw * bh;
                byte ob = 0, og = 0, or = 0;
                if (a > 0)
                {
                    ob = (byte)((b + a / 2) / a);
                    og = (byte)((g + a / 2) / a);
                    or = (byte)((r + a / 2) / a);
                }
                var oa = (byte)((a + count / 2) / count);
                for (var y = by; y < by + bh; y++)
                {
                    var row = y * width * 4;
                    for (var x = bx; x < bx + bw; x++)
                    {
                        var i = row + x * 4;
                        output[i] = ob;
                        output[i + 1] = og;
                        output[i + 2] = or;
                        output[i + 3] = oa;
                    }
                }
            }
        }
        return output;
    }
}
