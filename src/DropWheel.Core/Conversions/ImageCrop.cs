using System.Globalization;
using System.Text.RegularExpressions;

namespace DropWheel.Conversions;

/// <summary>
/// Область обрезки картинки в пикселях исходника после -auto-orient (решение D-CROP): левый верхний угол X, Y
/// и размер W × H. Запись как у ImageMagick — «WxH+X+Y» (<see cref="Geometry"/>).
/// </summary>
public readonly partial record struct CropRect(int X, int Y, int W, int H)
{
    public int Right => X + W;
    public int Bottom => Y + H;

    /// <summary>«800x600+100+50» — для -crop и CLI.</summary>
    public string Geometry => string.Create(CultureInfo.InvariantCulture, $"{W}x{H}+{X}+{Y}");

    public override string ToString() => Geometry;

    [GeneratedRegex(@"^\s*(\d{1,6})\s*[xх×]\s*(\d{1,6})(?:\s*\+\s*(\d{1,6})\s*\+\s*(\d{1,6}))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex GeometryRegex();

    /// <summary>
    /// «800x600+100+50» или «800x600» (от левого верхнего угла). «x» можно русской «х» или «×», пробелы не мешают.
    /// Только целые неотрицательные числа, ширина и высота — не меньше 1.
    /// </summary>
    public static bool TryParse(string? text, out CropRect rect)
    {
        rect = default;
        if (text is null || GeometryRegex().Match(text) is not { Success: true } m) return false;
        static int N(Group g) => g.Success ? int.Parse(g.Value, CultureInfo.InvariantCulture) : 0;
        var r = new CropRect(N(m.Groups[3]), N(m.Groups[4]), N(m.Groups[1]), N(m.Groups[2]));
        if (r.W < 1 || r.H < 1) return false;
        rect = r;
        return true;
    }

    /// <summary>Часть области внутри картинки imageW × imageH; null — область целиком за краем картинки.</summary>
    public CropRect? Intersect(int imageW, int imageH)
    {
        var x1 = Math.Max(0, X);
        var y1 = Math.Max(0, Y);
        var x2 = Math.Min(imageW, Right);
        var y2 = Math.Min(imageH, Bottom);
        return x2 - x1 >= 1 && y2 - y1 >= 1 ? new CropRect(x1, y1, x2 - x1, y2 - y1) : null;
    }

    /// <summary>Вся картинка — обрезать нечего.</summary>
    public bool IsWhole(int imageW, int imageH) => X <= 0 && Y <= 0 && Right >= imageW && Bottom >= imageH;

    public static CropRect Whole(int imageW, int imageH) => new(0, 0, Math.Max(1, imageW), Math.Max(1, imageH));
}

/// <summary>За что тянут рамку обрезки: края, углы или всю рамку (Move).</summary>
public enum CropHandle { None, Move, Left, Top, Right, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>
/// Обрезка картинки (D-CROP): id операции, значения {x} {y} {w} {h} (<see cref="Bind"/>) и логика рамки окна
/// «Обрезать…»: перетаскивание краёв и углов с пропорциями и без, рамка под пропорцию.
/// </summary>
public static class ImageCrop
{
    public const string Id = "img-crop";
    public const string XKey = "x", YKey = "y", WKey = "w", HKey = "h";
    public static readonly IReadOnlyList<string> ParameterNames = [XKey, YKey, WKey, HKey];

    /// <summary>
    /// Операция обрезки с областью rect, подрезанной по картинке imageW × imageH.
    /// Область целиком за краем или картинка без размера — ArgumentException.
    /// </summary>
    public static Operation Bind(CropRect rect, int imageW, int imageH)
    {
        if (imageW < 1 || imageH < 1) throw new ArgumentException($"Неверный размер картинки {imageW}×{imageH}");
        var r = rect.Intersect(imageW, imageH) ?? throw new ArgumentException($"Область {rect} за краем картинки {imageW}×{imageH}");
        return Bind(r);
    }

    /// <summary>Операция обрезки как есть, без подрезки по картинке (размер неизвестен): ImageMagick подрежет сам.</summary>
    public static Operation Bind(CropRect rect)
    {
        if (rect.X < 0 || rect.Y < 0 || rect.W < 1 || rect.H < 1) throw new ArgumentException($"Неверная область {rect}");
        return OperationCatalog.Find(Id)!.WithValues(new Dictionary<string, string>
        {
            [XKey] = rect.X.ToString(CultureInfo.InvariantCulture),
            [YKey] = rect.Y.ToString(CultureInfo.InvariantCulture),
            [WKey] = rect.W.ToString(CultureInfo.InvariantCulture),
            [HKey] = rect.H.ToString(CultureInfo.InvariantCulture),
        });
    }

    /// <summary>
    /// Пропорция (ширина / высота) для кнопки окна: «Свободно» — null, «Как было» — как у картинки;
    /// 4:3 и 16:9 у вертикальной картинки — 3:4 и 9:16 (рамка поворачивается вместе с фото).
    /// </summary>
    public static double? Aspect(CropAspect preset, int imageW, int imageH)
    {
        var portrait = imageH > imageW;
        return preset switch
        {
            CropAspect.Square => 1,
            CropAspect.FourThree => portrait ? 3.0 / 4 : 4.0 / 3,
            CropAspect.SixteenNine => portrait ? 9.0 / 16 : 16.0 / 9,
            CropAspect.Original => imageW / (double)Math.Max(1, imageH),
            _ => null,
        };
    }

    /// <summary>Самая большая рамка с пропорцией aspect по центру картинки (null — вся картинка).</summary>
    public static CropRect Largest(double? aspect, int imageW, int imageH)
    {
        if (aspect is not { } a || a <= 0) return CropRect.Whole(imageW, imageH);
        double w = imageW, h = w / a;
        if (h > imageH)
        {
            h = imageH;
            w = h * a;
        }
        var wi = Math.Clamp((int)Math.Round(w), 1, imageW);
        var hi = Math.Clamp((int)Math.Round(h), 1, imageH);
        return new CropRect((imageW - wi) / 2, (imageH - hi) / 2, wi, hi);
    }

    /// <summary>
    /// Рамка после перетаскивания: start — какой она была при нажатии мыши, dx/dy — сдвиг мыши с тех пор
    /// (в пикселях картинки; так округления не копятся). Рамка не выходит за картинку и не меньше min.
    /// С пропорцией aspect угол тянет рамку от противоположного угла, край — от противоположного края
    /// с центром по другой оси.
    /// </summary>
    public static CropRect Drag(CropRect start, CropHandle handle, double dx, double dy, int imageW, int imageH,
        double? aspect = null, int min = 1)
    {
        min = Math.Clamp(min, 1, Math.Max(1, Math.Min(imageW, imageH)));
        double l = start.X, t = start.Y, r = start.Right, b = start.Bottom;
        switch (handle)
        {
            case CropHandle.None:
                return start;
            case CropHandle.Move:
            {
                var x = Math.Clamp(Math.Round(l + dx), 0, imageW - start.W);
                var y = Math.Clamp(Math.Round(t + dy), 0, imageH - start.H);
                return new CropRect((int)x, (int)y, start.W, start.H);
            }
        }

        bool left = handle is CropHandle.Left or CropHandle.TopLeft or CropHandle.BottomLeft;
        bool right = handle is CropHandle.Right or CropHandle.TopRight or CropHandle.BottomRight;
        bool top = handle is CropHandle.Top or CropHandle.TopLeft or CropHandle.TopRight;
        bool bottom = handle is CropHandle.Bottom or CropHandle.BottomLeft or CropHandle.BottomRight;

        if (aspect is not { } a || a <= 0)
        {
            if (left) l = Math.Clamp(l + dx, 0, r - min);
            if (right) r = Math.Clamp(r + dx, l + min, imageW);
            if (top) t = Math.Clamp(t + dy, 0, b - min);
            if (bottom) b = Math.Clamp(b + dy, t + min, imageH);
            return Round(l, t, r, b, imageW, imageH);
        }

        // С пропорцией. Наименьшая рамка с этой пропорцией, где обе стороны не меньше min.
        var minW = Math.Max(min, min * a);
        if ((left || right) && (top || bottom))
        {
            // Угол: противоположный угол стоит на месте, размер — по большему из движений мыши.
            var ax = left ? r : l;
            var ay = top ? b : t;
            var w = Math.Max((left ? r - (l + dx) : r + dx - l), (top ? b - (t + dy) : b + dy - t) * a);
            var roomW = left ? ax : imageW - ax;
            var roomH = top ? ay : imageH - ay;
            w = Math.Clamp(w, Math.Min(minW, roomW), Math.Min(roomW, roomH * a));
            var h = w / a;
            l = left ? ax - w : ax;
            r = l + w;
            t = top ? ay - h : ay;
            b = t + h;
        }
        else if (left || right)
        {
            // Край слева/справа: ширина — от мыши, высота — по пропорции, вокруг прежнего центра по вертикали.
            var cy = (t + b) / 2;
            var ax = left ? r : l;
            var w = left ? r - (l + dx) : r + dx - l;
            var roomW = left ? ax : imageW - ax;
            var maxW = Math.Min(roomW, Math.Min(imageH, 2 * Math.Min(cy, imageH - cy)) * a);
            w = Math.Clamp(w, Math.Min(minW, maxW), maxW);
            var h = w / a;
            l = left ? ax - w : ax;
            r = l + w;
            t = Math.Clamp(cy - h / 2, 0, imageH - h);
            b = t + h;
        }
        else
        {
            // Край сверху/снизу: высота — от мыши, ширина — по пропорции, вокруг прежнего центра по горизонтали.
            var cx = (l + r) / 2;
            var ay = top ? b : t;
            var h = top ? b - (t + dy) : b + dy - t;
            var roomH = top ? ay : imageH - ay;
            var maxH = Math.Min(roomH, Math.Min(imageW, 2 * Math.Min(cx, imageW - cx)) / a);
            h = Math.Clamp(h, Math.Min(minW / a, maxH), maxH);
            var w = h * a;
            t = top ? ay - h : ay;
            b = t + h;
            l = Math.Clamp(cx - w / 2, 0, imageW - w);
            r = l + w;
        }
        return Round(l, t, r, b, imageW, imageH);
    }

    private static CropRect Round(double l, double t, double r, double b, int imageW, int imageH)
    {
        var x = Math.Clamp((int)Math.Round(l), 0, imageW - 1);
        var y = Math.Clamp((int)Math.Round(t), 0, imageH - 1);
        var w = Math.Clamp((int)Math.Round(r) - x, 1, imageW - x);
        var h = Math.Clamp((int)Math.Round(b) - y, 1, imageH - y);
        return new CropRect(x, y, w, h);
    }
}

/// <summary>Кнопки пропорций окна «Обрезать…»: «Свободно», «1:1», «4:3», «16:9», «Как было».</summary>
public enum CropAspect { Free, Square, FourThree, SixteenNine, Original }
