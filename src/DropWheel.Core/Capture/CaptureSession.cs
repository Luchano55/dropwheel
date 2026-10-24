namespace DropWheel.Capture;

/// <summary>
/// Выделение на «замороженном» снимке экрана (решение D-CAPTURE) без окон и мыши: всё в физических пикселях
/// виртуального экрана. Окна оверлея передают сюда положение курсора (GetCursorPos) и рисуют
/// <see cref="Highlight"/>; панель с кнопками — когда есть <see cref="Selection"/>.
/// <list type="bullet">
/// <item>Тянуть мышью в любую сторону — область (подрезается по экрану; тоньше <see cref="MinSize"/> — не область).</item>
/// <item>Щелчок без перетаскивания — окно под курсором (его рамка из списка окон), нет окна — монитор.</item>
/// <item>Двойной щелчок, Enter без выделения — весь монитор под курсором.</item>
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

    /// <summary>Курсор двигается без нажатой кнопки: подсветить окно под ним (если область ещё не выбрана).</summary>
    public void Hover(int x, int y)
    {
        if (_press is null && Selection is null) Highlight = WindowAt(x, y) ?? MonitorAt(x, y);
    }

    /// <summary>Нажата левая кнопка: начать новое выделение (старое пропадает).</summary>
    public void Press(int x, int y)
    {
        _press = (x, y);
        Selection = null;
        Highlight = WindowAt(x, y) ?? MonitorAt(x, y);
    }

    /// <summary>Курсор двигается с нажатой кнопкой: рамка от точки нажатия.</summary>
    public void Move(int x, int y)
    {
        if (_press is not { } p) { Hover(x, y); return; }
        if (IsClick(p.X, p.Y, x, y)) Highlight = WindowAt(p.X, p.Y) ?? MonitorAt(p.X, p.Y);
        else Highlight = FromDrag(p.X, p.Y, x, y, Bounds);
    }

    /// <summary>Кнопка отпущена: щелчок — окно (или монитор), перетаскивание — область. Слишком тонкая область — ничего.</summary>
    public PixelRect? Release(int x, int y)
    {
        if (_press is not { } p) return Selection;
        _press = null;
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
        var monitor = MonitorAt(x, y);
        Selection = Highlight = monitor;
        return monitor;
    }

    /// <summary>Сбросить выделение (правый щелчок при выбранной области): снова «выделите область».</summary>
    public void Clear(int x, int y)
    {
        _press = null;
        Selection = null;
        Hover(x, y);
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
