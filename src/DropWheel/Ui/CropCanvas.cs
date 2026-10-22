using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using DropWheel.Conversions;

namespace DropWheel.Ui;

/// <summary>
/// Картинка с рамкой обрезки (D-CROP): картинка вписана целиком, вне рамки — затемнено, внутри — линии третей;
/// 8 ручек (углы и середины краёв). Мышь: тянуть угол или край, внутри рамки — двигать её.
/// Клавиатура: стрелки двигают рамку на 1 px картинки (с Shift — на 10).
/// Рамка хранится в пикселях картинки (<see cref="CropRect"/>), логика перетаскивания — <see cref="ImageCrop.Drag"/>.
/// </summary>
public sealed class CropCanvas : FrameworkElement
{
    private const double Pad = 12;          // место под ручки по краям
    private const double Grab = 10;         // насколько близко к ручке / краю можно попасть мышью
    private const double MinScreen = 24;    // рамка не меньше 24 px на экране

    private SettingsPalette _palette;
    private ImageSource? _image;
    private int _imageW, _imageH;
    private CropRect? _rect;
    private double? _aspect;
    private CropHandle _dragging = CropHandle.None;
    private Point _dragFrom;
    private CropRect _dragStart;

    public CropCanvas(SettingsPalette palette)
    {
        _palette = palette;
        Focusable = true;
        FocusVisualStyle = null;
        MinWidth = 200;
        MinHeight = 160;
        AutomationProperties.SetName(this, Localization.Loc.T.CropTitle);
    }

    public SettingsPalette Palette
    {
        get => _palette;
        set
        {
            _palette = value;
            InvalidateVisual();
        }
    }

    /// <summary>Картинка для показа (может быть уменьшенной копией) и размер настоящей картинки в пикселях.</summary>
    public void SetImage(ImageSource? image, int width, int height)
    {
        _image = image;
        _imageW = width;
        _imageH = height;
        InvalidateVisual();
    }

    /// <summary>Рамка в пикселях картинки; null — картинки ещё нет.</summary>
    public CropRect? Selection
    {
        get => _rect;
        set
        {
            _rect = value;
            InvalidateVisual();
        }
    }

    /// <summary>Пропорция рамки (ширина / высота) при перетаскивании углов и краёв; null — свободно.</summary>
    public double? Aspect
    {
        get => _aspect;
        set => _aspect = value;
    }

    /// <summary>Пользователь изменил рамку (мышью или клавиатурой).</summary>
    public event Action<CropRect>? SelectionChanged;

    private bool HasImage => _imageW > 0 && _imageH > 0;

    /// <summary>Где на экране картинка: вписана целиком по центру.</summary>
    private Rect ImageArea()
    {
        if (!HasImage) return Rect.Empty;
        var w = Math.Max(1, ActualWidth - 2 * Pad);
        var h = Math.Max(1, ActualHeight - 2 * Pad);
        var scale = Math.Min(w / _imageW, h / _imageH);
        var size = new Size(_imageW * scale, _imageH * scale);
        return new Rect((ActualWidth - size.Width) / 2, (ActualHeight - size.Height) / 2, size.Width, size.Height);
    }

    private double Scale => HasImage ? ImageArea().Width / _imageW : 1;

    private Rect ScreenOf(CropRect r)
    {
        var area = ImageArea();
        var s = area.Width / _imageW;
        return new Rect(area.X + r.X * s, area.Y + r.Y * s, r.W * s, r.H * s);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 560 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 380 : availableSize.Height);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (!HasImage) return;
        var area = ImageArea();
        if (_image is not null) dc.DrawImage(_image, area);
        if (_rect is not { } r) return;

        var c = ScreenOf(r);
        // Вне рамки — затемнено (четыре полосы, без наложений).
        var dim = SettingsPalette.B(Color.FromArgb(0x99, 0, 0, 0));
        dc.DrawRectangle(dim, null, new Rect(area.Left, area.Top, area.Width, Math.Max(0, c.Top - area.Top)));
        dc.DrawRectangle(dim, null, new Rect(area.Left, c.Bottom, area.Width, Math.Max(0, area.Bottom - c.Bottom)));
        dc.DrawRectangle(dim, null, new Rect(area.Left, c.Top, Math.Max(0, c.Left - area.Left), c.Height));
        dc.DrawRectangle(dim, null, new Rect(c.Right, c.Top, Math.Max(0, area.Right - c.Right), c.Height));

        // Линии третей и рамка.
        var white = Brushes.White;
        var thirds = new Pen(SettingsPalette.B(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), 1);
        for (var i = 1; i < 3; i++)
        {
            var x = Math.Round(c.Left + c.Width * i / 3) + 0.5;
            var y = Math.Round(c.Top + c.Height * i / 3) + 0.5;
            dc.DrawLine(thirds, new Point(x, c.Top), new Point(x, c.Bottom));
            dc.DrawLine(thirds, new Point(c.Left, y), new Point(c.Right, y));
        }
        dc.DrawRectangle(null, new Pen(white, 1), new Rect(c.X + 0.5, c.Y + 0.5, Math.Max(0, c.Width - 1), Math.Max(0, c.Height - 1)));
        if (IsKeyboardFocused)
            dc.DrawRectangle(null, new Pen(_palette.Accent, 2), Rect.Inflate(c, 2, 2));

        // Ручки: уголки буквой «Г» на углах, короткие полоски — на серединах краёв (с тенью, чтобы видно на белом).
        const double t = 3;
        var len = Math.Min(18, Math.Min(c.Width, c.Height) / 2);
        var shadow = SettingsPalette.B(Color.FromArgb(0x60, 0, 0, 0));
        foreach (var (rect, _) in HandleBars(c, len, t))
            dc.DrawRectangle(shadow, null, new Rect(rect.X + 1, rect.Y + 1, rect.Width, rect.Height));
        foreach (var (rect, handle) in HandleBars(c, len, t))
            dc.DrawRectangle(_dragging == handle ? _palette.Accent : white, null, rect);
    }

    /// <summary>Полоски ручек вокруг рамки c (снаружи на толщину t).</summary>
    private static IEnumerable<(Rect, CropHandle)> HandleBars(Rect c, double len, double t)
    {
        double l = c.Left - t, r = c.Right, top = c.Top - t, b = c.Bottom;
        // Углы: горизонтальная и вертикальная полоски.
        yield return (new Rect(l, top, len + t, t), CropHandle.TopLeft);
        yield return (new Rect(l, top, t, len + t), CropHandle.TopLeft);
        yield return (new Rect(r - len, top, len + t, t), CropHandle.TopRight);
        yield return (new Rect(r, top, t, len + t), CropHandle.TopRight);
        yield return (new Rect(l, b, len + t, t), CropHandle.BottomLeft);
        yield return (new Rect(l, b - len, t, len + t), CropHandle.BottomLeft);
        yield return (new Rect(r - len, b, len + t, t), CropHandle.BottomRight);
        yield return (new Rect(r, b - len, t, len + t), CropHandle.BottomRight);
        // Середины краёв.
        var cx = c.Left + c.Width / 2;
        var cy = c.Top + c.Height / 2;
        yield return (new Rect(cx - len / 2, top, len, t), CropHandle.Top);
        yield return (new Rect(cx - len / 2, b, len, t), CropHandle.Bottom);
        yield return (new Rect(l, cy - len / 2, t, len), CropHandle.Left);
        yield return (new Rect(r, cy - len / 2, t, len), CropHandle.Right);
    }

    // ---------- мышь ----------

    /// <summary>Что под мышью: угол, край (на всю длину), внутри рамки — Move.</summary>
    private CropHandle HandleAt(Point p)
    {
        if (_rect is not { } r || !HasImage) return CropHandle.None;
        var c = ScreenOf(r);
        var nearL = Math.Abs(p.X - c.Left) <= Grab;
        var nearR = Math.Abs(p.X - c.Right) <= Grab;
        var nearT = Math.Abs(p.Y - c.Top) <= Grab;
        var nearB = Math.Abs(p.Y - c.Bottom) <= Grab;
        // У маленькой рамки края рядом: ближе к какому — тот и берём.
        if (nearL && nearR) (nearL, nearR) = (Math.Abs(p.X - c.Left) <= Math.Abs(p.X - c.Right), Math.Abs(p.X - c.Left) > Math.Abs(p.X - c.Right));
        if (nearT && nearB) (nearT, nearB) = (Math.Abs(p.Y - c.Top) <= Math.Abs(p.Y - c.Bottom), Math.Abs(p.Y - c.Top) > Math.Abs(p.Y - c.Bottom));
        var inX = p.X >= c.Left - Grab && p.X <= c.Right + Grab;
        var inY = p.Y >= c.Top - Grab && p.Y <= c.Bottom + Grab;
        if (!inX || !inY) return CropHandle.None;
        if (nearT && nearL) return CropHandle.TopLeft;
        if (nearT && nearR) return CropHandle.TopRight;
        if (nearB && nearL) return CropHandle.BottomLeft;
        if (nearB && nearR) return CropHandle.BottomRight;
        if (nearL) return CropHandle.Left;
        if (nearR) return CropHandle.Right;
        if (nearT) return CropHandle.Top;
        if (nearB) return CropHandle.Bottom;
        return c.Contains(p) ? CropHandle.Move : CropHandle.None;
    }

    private static Cursor CursorFor(CropHandle h) => h switch
    {
        CropHandle.TopLeft or CropHandle.BottomRight => Cursors.SizeNWSE,
        CropHandle.TopRight or CropHandle.BottomLeft => Cursors.SizeNESW,
        CropHandle.Left or CropHandle.Right => Cursors.SizeWE,
        CropHandle.Top or CropHandle.Bottom => Cursors.SizeNS,
        CropHandle.Move => Cursors.SizeAll,
        _ => Cursors.Arrow,
    };

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var p = e.GetPosition(this);
        var handle = HandleAt(p);
        if (handle == CropHandle.None || _rect is not { } r) return;
        e.Handled = true;
        _dragging = handle;
        _dragFrom = p;
        _dragStart = r;
        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        if (_dragging == CropHandle.None)
        {
            Cursor = CursorFor(HandleAt(p));
            return;
        }
        var s = Scale;
        var min = (int)Math.Ceiling(MinScreen / s);
        Change(ImageCrop.Drag(_dragStart, _dragging, (p.X - _dragFrom.X) / s, (p.Y - _dragFrom.Y) / s, _imageW, _imageH, _aspect, min));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging == CropHandle.None) return;
        _dragging = CropHandle.None;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = CropHandle.None;
        InvalidateVisual();
    }

    private void Change(CropRect next)
    {
        if (_rect == next) return;
        _rect = next;
        InvalidateVisual();
        SelectionChanged?.Invoke(next);
    }

    // ---------- клавиатура ----------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_rect is not { } r || !HasImage) return;
        var step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
        var (dx, dy) = e.Key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, -step),
            Key.Down => (0, step),
            _ => (0, 0),
        };
        if (dx == 0 && dy == 0) return;
        e.Handled = true;
        Change(ImageCrop.Drag(r, CropHandle.Move, dx, dy, _imageW, _imageH));
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }
}
