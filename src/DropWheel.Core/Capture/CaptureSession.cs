namespace DropWheel.Capture;

/// <summary>
/// Выделение на «замороженном» снимке экрана (решение D-CAPTURE) без окон и мыши: всё в физических пикселях
/// виртуального экрана. Окна оверлея передают сюда положение курсора (GetCursorPos) и рисуют
/// <see cref="Highlight"/>; панель с кнопками — когда есть <see cref="Selection"/>.
/// <list type="bullet">
/// <item>Тянуть мышью в любую сторону — область (подрезается по экрану; тоньше <see cref="MinSize"/> — не область).</item>
/// <item>Щелчок без перетаскивания — окно под курсором (его рамка из списка окон), нет окна — монитор.</item>
/// <item>Двойной щелчок, Enter без выделения — весь монитор под курсором.</item>
/// <item>Выделение уже есть: тянуть за край или угол — подогнать рамку, изнутри — сдвинуть (<see cref="GripAt"/>);
/// стрелки — сдвиг на пиксель (<see cref="Nudge"/>). Нажатие вне рамки — новое выделение.</item>
/// </list>
/// </summary>
public sealed class CaptureSession
{
    /// <summary>Сдвиг мыши (пикселей), до которого нажатие считается щелчком, а не перетаскиванием.</summary>
    public const int ClickSlop = 4;

    /// <summary>Самая маленькая область (по каждой стороне), пикселей.</summary>
    public const int MinSize = 2;

    private readonly IReadOnlyList<PixelRect> _monitors;
    private readonly IReadOnlyList<PixelRect> _windows;
    private (int X, int Y)? _press;
    /// <summary>Подгонка уже выбранной области: за что взялись и какой она была при нажатии.</summary>
    private (CaptureGrip Grip, PixelRect Start)? _adjust;

    /// <param name="monitors">Границы мониторов (физические пиксели).</param>
    /// <param name="windows">Рамки видимых окон сверху вниз (по Z-порядку), без самого оверлея; можно пустой.</param>
    public CaptureSession(IReadOnlyList<PixelRect> monitors, IReadOnlyList<PixelRect>? windows = null)
    {
        if (monitors.Count == 0) throw new ArgumentException("Нет ни одного монитора", nameof(monitors));
        _monitors = monitors;
        _windows = windows ?? [];
        Bounds = monitors.Aggregate(default(PixelRect), (a, m) => a.Union(m));
    }

    /// <summary>Весь виртуальный экран (объединение мониторов).</summary>
    public PixelRect Bounds { get; }

    /// <summary>Выбранная область (кнопка мыши отпущена); null — ещё не выбрано.</summary>
    public PixelRect? Selection { get; private set; }

    /// <summary>Что подсвечено сейчас: выбранная область, тянущаяся рамка или окно под курсором.</summary>
    public PixelRect? Highlight { get; private set; }

    public bool IsDragging => _press is not null;

    /// <summary>Сейчас тянут край, угол или всю рамку уже выбранной области.</summary>
    public bool IsAdjusting => _adjust is not null;

    /// <summary>
    /// За что возьмётся нажатие в точке при уже выбранной области: угол, край (в пределах <paramref name="tolerance"/>
    /// пикселей от линии рамки), середина — сдвиг; дальше от рамки или выделения нет — <see cref="CaptureGrip.None"/>.
    /// </summary>
    public CaptureGrip GripAt(int x, int y, int tolerance)
    {
        if (Selection is not { } s || tolerance < 0) return CaptureGrip.None;
        // Края рамки — линии X, Right, Y, Bottom (правый и нижний края — сразу за последним пикселем).
        var nearLeft = Math.Abs(x - s.X) <= tolerance;
        var nearRight = Math.Abs(x - s.Right) <= tolerance;
        var nearTop = Math.Abs(y - s.Y) <= tolerance;
        var nearBottom = Math.Abs(y - s.Bottom) <= tolerance;
        var inX = x >= s.X - tolerance && x <= s.Right + tolerance;
        var inY = y >= s.Y - tolerance && y <= s.Bottom + tolerance;
        if (!inX || !inY) return CaptureGrip.None;
        // У маленькой рамки зоны краёв пересекаются: ближе к правому/нижнему краю — он.
        var left = nearLeft && !(nearRight && x - s.X > s.Right - x);
        var right = nearRight && !left;
        var top = nearTop && !(nearBottom && y - s.Y > s.Bottom - y);
        var bottom = nearBottom && !top;
        return (left, right, top, bottom) switch
        {
            (true, _, true, _) => CaptureGrip.TopLeft,
            (_, true, true, _) => CaptureGrip.TopRight,
            (true, _, _, true) => CaptureGrip.BottomLeft,
            (_, true, _, true) => CaptureGrip.BottomRight,
            (true, _, _, _) => CaptureGrip.Left,
            (_, true, _, _) => CaptureGrip.Right,
            (_, _, true, _) => CaptureGrip.Top,
            (_, _, _, true) => CaptureGrip.Bottom,
            _ => s.Contains(x, y) ? CaptureGrip.Move : CaptureGrip.None,
        };
    }

    /// <summary>Курсор двигается без нажатой кнопки: подсветить окно под ним (если область ещё не выбрана).</summary>
    public void Hover(int x, int y)
    {
        if (_press is null && Selection is null) Highlight = WindowAt(x, y) ?? MonitorAt(x, y);
    }

    /// <summary>
    /// Нажата левая кнопка. Есть выделение и нажали на его край, угол или внутрь (<paramref name="tolerance"/> &gt; 0) —
    /// начать подгонку; иначе — новое выделение (старое пропадает).
    /// </summary>
    public void Press(int x, int y, int tolerance = 0)
    {
        _press = (x, y);
        _adjust = null;
        if (tolerance > 0 && Selection is { } current && GripAt(x, y, tolerance) is var grip and not CaptureGrip.None)
        {
            _adjust = (grip, current);
            Highlight = current;
            return;
        }
        Selection = null;
        Highlight = WindowAt(x, y) ?? MonitorAt(x, y);
    }

    /// <summary>Курсор двигается с нажатой кнопкой: рамка от точки нажатия.</summary>
    public void Move(int x, int y)
    {
        if (_press is not { } p) { Hover(x, y); return; }
        if (_adjust is { } a)
        {
            Highlight = Adjust(a.Grip, a.Start, x - p.X, y - p.Y, Bounds) ?? Highlight;
            return;
        }
        if (IsClick(p.X, p.Y, x, y)) Highlight = WindowAt(p.X, p.Y) ?? MonitorAt(p.X, p.Y);
        else Highlight = FromDrag(p.X, p.Y, x, y, Bounds);
    }

    /// <summary>Кнопка отпущена: щелчок — окно (или монитор), перетаскивание — область. Слишком тонкая область — ничего.</summary>
    public PixelRect? Release(int x, int y)
    {
        if (_press is not { } p) return Selection;
        _press = null;
        if (_adjust is { } a)
        {
            _adjust = null;
            // Щелчок внутри рамки без сдвига — как раньше: окно под курсором (или монитор).
            if (a.Grip == CaptureGrip.Move && IsClick(p.X, p.Y, x, y))
            {
                Selection = Highlight = WindowAt(p.X, p.Y) ?? MonitorAt(p.X, p.Y);
                return Selection;
            }
            Selection = Highlight = Adjust(a.Grip, a.Start, x - p.X, y - p.Y, Bounds) ?? Highlight ?? a.Start;
            return Selection;
        }
        Selection = IsClick(p.X, p.Y, x, y)
            ? WindowAt(p.X, p.Y) ?? MonitorAt(p.X, p.Y)
            : FromDrag(p.X, p.Y, x, y, Bounds);
        if (Selection is null) Hover(x, y);
        else Highlight = Selection;
        return Selection;
    }

    /// <summary>Весь монитор под курсором (двойной щелчок, Enter без выделения).</summary>
    public PixelRect SelectMonitor(int x, int y)
    {
        _press = null;
        _adjust = null;
        var monitor = MonitorAt(x, y);
        Selection = Highlight = monitor;
        return monitor;
    }

    /// <summary>Сбросить выделение (правый щелчок при выбранной области): снова «выделите область».</summary>
    public void Clear(int x, int y)
    {
        _press = null;
        _adjust = null;
        Selection = null;
        Hover(x, y);
    }

    /// <summary>Сдвинуть выбранную область (стрелки): не за край экрана. Нет выделения или идёт перетаскивание — ничего.</summary>
    public PixelRect? Nudge(int dx, int dy)
    {
        if (_press is not null || Selection is not { } s) return Selection;
        Selection = Highlight = Adjust(CaptureGrip.Move, s, dx, dy, Bounds) ?? s;
        return Selection;
    }

    /// <summary>
    /// Рамка <paramref name="start"/> после того, как её взяли за <paramref name="grip"/> и сдвинули мышь на (dx, dy).
    /// Сдвиг целиком — без изменения размера и не за <paramref name="bounds"/>; край или угол — можно перетянуть
    /// через противоположный край (рамка «выворачивается»), результат подрезается по экрану.
    /// Уже <see cref="MinSize"/> — null (оставить прежнюю).
    /// </summary>
    public static PixelRect? Adjust(CaptureGrip grip, PixelRect start, int dx, int dy, PixelRect bounds)
    {
        if (grip == CaptureGrip.None) return start;
        if (grip == CaptureGrip.Move)
        {
            var nx = Math.Clamp(start.X + dx, bounds.X, Math.Max(bounds.X, bounds.Right - start.Width));
            var ny = Math.Clamp(start.Y + dy, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - start.Height));
            return start with { X = nx, Y = ny };
        }
        int l = start.X, t = start.Y, r = start.Right, b = start.Bottom;
        if (grip is CaptureGrip.Left or CaptureGrip.TopLeft or CaptureGrip.BottomLeft) l += dx;
        if (grip is CaptureGrip.Right or CaptureGrip.TopRight or CaptureGrip.BottomRight) r += dx;
        if (grip is CaptureGrip.Top or CaptureGrip.TopLeft or CaptureGrip.TopRight) t += dy;
        if (grip is CaptureGrip.Bottom or CaptureGrip.BottomLeft or CaptureGrip.BottomRight) b += dy;
        var rect = PixelRect.FromEdges(Math.Min(l, r), Math.Min(t, b), Math.Max(l, r), Math.Max(t, b)).Intersect(bounds);
        return rect.Width >= MinSize && rect.Height >= MinSize ? rect : null;
    }

    /// <summary>Монитор, которому принадлежит точка; в промежутке между мониторами — ближайший.</summary>
    public PixelRect MonitorAt(int x, int y)
    {
        foreach (var m in _monitors)
            if (m.Contains(x, y)) return m;
        return _monitors.MinBy(m => m.DistanceSquared(x, y));
    }

    /// <summary>
    /// Самое верхнее окно под точкой, подрезанное по экрану; нет — null. Окно почти целиком на одном мониторе
    /// (развёрнутое заходит на соседний на ширину рамки) подрезается по этому монитору.
    /// </summary>
    public PixelRect? WindowAt(int x, int y)
    {
        foreach (var w in _windows)
        {
            if (!w.Contains(x, y)) continue;
            var clipped = w.Intersect(Bounds);
            var onMonitor = w.Intersect(MonitorAt(x, y));
            if (onMonitor.Area * 100 >= clipped.Area * 95) clipped = onMonitor;
            return clipped.Width >= MinSize && clipped.Height >= MinSize ? clipped : null;
        }
        return null;
    }

    /// <summary>Сдвиг от нажатия так мал, что это щелчок.</summary>
    public static bool IsClick(int ax, int ay, int bx, int by) => Math.Abs(bx - ax) <= ClickSlop && Math.Abs(by - ay) <= ClickSlop;

    /// <summary>
    /// Рамка между точкой нажатия и курсором в любом направлении, подрезанная по <paramref name="bounds"/>.
    /// Точки — края рамки: от 10 до 110 — ширина 100. Уже <see cref="MinSize"/> по любой стороне — null.
    /// </summary>
    public static PixelRect? FromDrag(int ax, int ay, int bx, int by, PixelRect bounds)
    {
        var rect = PixelRect.FromEdges(Math.Min(ax, bx), Math.Min(ay, by), Math.Max(ax, bx), Math.Max(ay, by)).Intersect(bounds);
        return rect.Width >= MinSize && rect.Height >= MinSize ? rect : null;
    }
}

/// <summary>За что взяли уже выбранную область: край, угол или вся рамка (сдвиг).</summary>
public enum CaptureGrip
{
    None,
    Move,
    Left,
    Top,
    Right,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}
