using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using DropWheel.Conversions;

namespace DropWheel.Ui;

public enum TrimHandle { Start, End }

/// <summary>
/// Шкала окна обрезки (D-TRIM): полоса миниатюр кадров по всей длине ролика, выбранный кусок — в рамке цвета
/// акцента, вне его — затемнено; по краям рамки — ручки «начало» и «конец».
/// Мышь: тянуть ручку; щелчок по шкале — ближайшая ручка прыгает туда. Клавиатура: ←/→ — на 0,1 с
/// (с Shift — на 1 с), Home/End — к краю, Tab — другая ручка.
/// </summary>
public sealed class TrimTimeline : FrameworkElement
{
    private const double Pad = 12;          // место под ручки слева и справа
    private const double HandleWidth = 12;
    private const double TrackHeight = 52;
    private const double Corner = 6;

    private SettingsPalette _palette;
    private TrimRange? _range;
    private ImageSource?[] _thumbnails = [];
    private TrimHandle _active = TrimHandle.Start;
    private TrimHandle? _dragging;

    public TrimTimeline(SettingsPalette palette)
    {
        _palette = palette;
        Height = TrackHeight + 8;
        MinWidth = 200;
        Focusable = true;
        FocusVisualStyle = null;
        AutomationProperties.SetName(this, Localization.Loc.T.TrimTitle);
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

    /// <summary>Выбранный кусок; null — длительность ещё неизвестна (шкала пустая, не нажимается).</summary>
    public TrimRange? Range
    {
        get => _range;
        set
        {
            _range = value;
            InvalidateVisual();
        }
    }

    /// <summary>Ручка, которую двигали последней: её кадр показывает предпросмотр, её двигают стрелки.</summary>
    public TrimHandle ActiveHandle
    {
        get => _active;
        set
        {
            _active = value;
            InvalidateVisual();
        }
    }

    /// <summary>Миниатюры по порядку, равными долями шкалы; null — кадра пока нет (место закрашено).</summary>
    public IReadOnlyList<ImageSource?> Thumbnails
    {
        get => _thumbnails;
        set
        {
            _thumbnails = [.. value];
            InvalidateVisual();
        }
    }

    public void SetThumbnail(int index, ImageSource? image)
    {
        if (index < 0 || index >= _thumbnails.Length) return;
        _thumbnails[index] = image;
        InvalidateVisual();
    }

    /// <summary>Пользователь сдвинул ручку (мышью или клавиатурой).</summary>
    public event Action<TrimRange, TrimHandle>? RangeChanged;

    private double Left => Pad;
    private double Right => Math.Max(Pad + 1, ActualWidth - Pad);
    private double Top => (ActualHeight - TrackHeight) / 2;

    private double XOf(double seconds) =>
        _range is null ? Left : Left + seconds / _range.Duration * (Right - Left);

    private double TimeAt(double x) =>
        _range is null ? 0 : Math.Clamp((x - Left) / (Right - Left), 0, 1) * _range.Duration;

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 560 : availableSize.Width, Height);

    protected override void OnRender(DrawingContext dc)
    {
        var p = _palette;
        var track = new Rect(Left, Top, Right - Left, TrackHeight);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        // Миниатюры (обрезаны по скруглённой полосе), под ними — подложка на случай пустых мест.
        dc.PushClip(new RectangleGeometry(track, Corner, Corner));
        dc.DrawRectangle(p.IsLight ? SettingsPalette.B(Color.FromRgb(0xD9, 0xD9, 0xD9)) : SettingsPalette.B(Color.FromRgb(0x3A, 0x3A, 0x3A)), null, track);
        if (_thumbnails.Length > 0)
        {
            var slot = track.Width / _thumbnails.Length;
            for (var i = 0; i < _thumbnails.Length; i++)
                if (_thumbnails[i] is { } image)
                    DrawFill(dc, image, new Rect(track.X + i * slot, track.Y, slot + 0.5, track.Height));
        }

        if (_range is { } r)
        {
            var x1 = XOf(r.Start);
            var x2 = XOf(r.End);
            var dim = SettingsPalette.B(Color.FromArgb(0xA6, 0, 0, 0));
            if (x1 > track.Left) dc.DrawRectangle(dim, null, new Rect(track.Left, track.Top, x1 - track.Left, track.Height));
            if (x2 < track.Right) dc.DrawRectangle(dim, null, new Rect(x2, track.Top, track.Right - x2, track.Height));
        }
        dc.Pop();

        if (_range is not { } range) return;
        var a = XOf(range.Start);
        var b = XOf(range.End);
        var accent = p.Accent;
        // Рамка выбранного куска: сверху и снизу полоски, по бокам — ручки.
        const double edge = 3;
        dc.DrawRectangle(accent, null, new Rect(a, track.Top - 1, Math.Max(0, b - a), edge));
        dc.DrawRectangle(accent, null, new Rect(a, track.Bottom - edge + 1, Math.Max(0, b - a), edge));
        DrawHandle(dc, new Rect(a - HandleWidth, track.Top - 1, HandleWidth, track.Height + 2), left: true, TrimHandle.Start);
        DrawHandle(dc, new Rect(b, track.Top - 1, HandleWidth, track.Height + 2), left: false, TrimHandle.End);
    }

    private void DrawHandle(DrawingContext dc, Rect rect, bool left, TrimHandle handle)
    {
        var p = _palette;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            // Скругление — только снаружи куска.
            var r = Corner;
            if (left)
            {
                g.BeginFigure(new Point(rect.Right, rect.Top), true, true);
                g.LineTo(new Point(rect.Left + r, rect.Top), true, false);
                g.ArcTo(new Point(rect.Left, rect.Top + r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                g.LineTo(new Point(rect.Left, rect.Bottom - r), true, false);
                g.ArcTo(new Point(rect.Left + r, rect.Bottom), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
                g.LineTo(new Point(rect.Right, rect.Bottom), true, false);
            }
            else
            {
                g.BeginFigure(new Point(rect.Left, rect.Top), true, true);
                g.LineTo(new Point(rect.Right - r, rect.Top), true, false);
                g.ArcTo(new Point(rect.Right, rect.Top + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                g.LineTo(new Point(rect.Right, rect.Bottom - r), true, false);
                g.ArcTo(new Point(rect.Right - r, rect.Bottom), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                g.LineTo(new Point(rect.Left, rect.Bottom), true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(p.Accent, null, geometry);
        // «Хват» — две короткие полоски; у активной ручки при фокусе с клавиатуры — обводка.
        var cx = rect.Left + rect.Width / 2;
        var cy = rect.Top + rect.Height / 2;
        var grip = _dragging == handle ? 9.0 : 7.0;
        dc.DrawRoundedRectangle(p.OnAccent, null, new Rect(cx - 1, cy - grip, 2, 2 * grip), 1, 1);
        if (IsKeyboardFocused && _active == handle)
            dc.DrawRoundedRectangle(null, new Pen(p.Text, 1.5), Rect.Inflate(rect, 2.5, 2.5), Corner, Corner);
    }

    /// <summary>Картинка заполняет место целиком (лишнее обрезается), как миниатюры в проводнике.</summary>
    private static void DrawFill(DrawingContext dc, ImageSource image, Rect rect)
    {
        var w = image.Width;
        var h = image.Height;
        if (w <= 0 || h <= 0) return;
        var scale = Math.Max(rect.Width / w, rect.Height / h);
        var size = new Size(w * scale, h * scale);
        dc.PushClip(new RectangleGeometry(rect));
        dc.DrawImage(image, new Rect(rect.X + (rect.Width - size.Width) / 2, rect.Y + (rect.Height - size.Height) / 2, size.Width, size.Height));
        dc.Pop();
    }

    // ---------- мышь ----------

    private TrimHandle? HandleAt(double x)
    {
        if (_range is not { } r) return null;
        var a = XOf(r.Start) - HandleWidth / 2;
        var b = XOf(r.End) + HandleWidth / 2;
        var da = Math.Abs(x - a);
        var db = Math.Abs(x - b);
        if (Math.Min(da, db) > HandleWidth) return null;
        // Ручки вплотную: щелчок левее середины — начало, правее — конец.
        return da < db || (da == db && x < a) ? TrimHandle.Start : TrimHandle.End;
    }

    private void Move(TrimHandle handle, double seconds)
    {
        if (_range is not { } r) return;
        var next = handle == TrimHandle.Start ? r.WithStart(seconds) : r.WithEnd(seconds);
        _active = handle;
        if (next == r)
        {
            InvalidateVisual();
            return;
        }
        _range = next;
        InvalidateVisual();
        RangeChanged?.Invoke(next, handle);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_range is not { } r) return;
        e.Handled = true;
        Focus();
        var x = e.GetPosition(this).X;
        var handle = HandleAt(x);
        if (handle is null)
        {
            // Щелчок по шкале — ближайшая ручка прыгает сюда.
            var t = TimeAt(x);
            handle = Math.Abs(t - r.Start) <= Math.Abs(t - r.End) ? TrimHandle.Start : TrimHandle.End;
            Move(handle.Value, t);
        }
        else
        {
            _active = handle.Value;
        }
        _dragging = handle;
        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var x = e.GetPosition(this).X;
        if (_dragging is { } handle)
        {
            // Ручка «держится» за край: начало — правым краем, конец — левым.
            Move(handle, TimeAt(x + (handle == TrimHandle.Start ? HandleWidth / 2 : -HandleWidth / 2)));
            return;
        }
        Cursor = HandleAt(x) is null ? Cursors.Arrow : Cursors.SizeWE;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging is null) return;
        _dragging = null;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = null;
        InvalidateVisual();
    }

    // ---------- клавиатура ----------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_range is not { } r) return;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var current = _active == TrimHandle.Start ? r.Start : r.End;
        switch (e.Key)
        {
            case Key.Left or Key.Down:
                Move(_active, current - (shift ? 1 : 0.1));
                break;
            case Key.Right or Key.Up:
                Move(_active, current + (shift ? 1 : 0.1));
                break;
            case Key.Home:
                Move(_active, 0);
                break;
            case Key.End:
                Move(_active, r.Duration);
                break;
            // Tab с начала — к концу, Shift+Tab с конца — к началу; дальше фокус уходит как обычно.
            case Key.Tab when !shift && _active == TrimHandle.Start:
            case Key.Tab when shift && _active == TrimHandle.End:
                ActiveHandle = shift ? TrimHandle.Start : TrimHandle.End;
                RangeChanged?.Invoke(r, _active);
                break;
            default:
                return;
        }
        e.Handled = true;
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
