using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DropWheel.Ui;

/// <summary>
/// Цвета окна настроек в духе «Параметров» Windows 11: карточки поверх Mica (или сплошного фона), акцент системы.
/// </summary>
public sealed class SettingsPalette
{
    private SettingsPalette(bool light, AccentColors accent, bool overBackdrop)
    {
        IsLight = light;
        OverBackdrop = overBackdrop;
        AccentColor = light ? accent.Dark1 : accent.Light2;
        Accent = B(AccentColor);
        OnAccent = B(light ? Colors.White : Colors.Black);
        if (light)
        {
            Solid = Color.FromRgb(0xF3, 0xF3, 0xF3);
            Card = B(0xB3, 0xFF, 0xFF, 0xFF);
            CardBorder = B(0x0F, 0x00, 0x00, 0x00);
            Text = B(0xE4, 0x00, 0x00, 0x00);
            TextSecondary = B(0x9E, 0x00, 0x00, 0x00);
            TextTertiary = B(0x72, 0x00, 0x00, 0x00);
            NavHover = B(0x09, 0x00, 0x00, 0x00);
            NavSelected = B(0x0F, 0x00, 0x00, 0x00);
            Control = B(0xB3, 0xFF, 0xFF, 0xFF);
            ControlHover = B(0x80, 0xF9, 0xF9, 0xF9);
            ControlPressed = B(0x4D, 0xF9, 0xF9, 0xF9);
            ControlBorder = B(0x29, 0x00, 0x00, 0x00);
            Strong = B(0x9E, 0x00, 0x00, 0x00);
            Track = B(0x72, 0x00, 0x00, 0x00);
            Divider = B(0x14, 0x00, 0x00, 0x00);
            Preview = B(0x0A, 0x00, 0x00, 0x00);
        }
        else
        {
            Solid = Color.FromRgb(0x20, 0x20, 0x20);
            Card = B(0x0D, 0xFF, 0xFF, 0xFF);
            CardBorder = B(0x14, 0xFF, 0xFF, 0xFF);
            Text = B(0xFF, 0xFF, 0xFF, 0xFF);
            TextSecondary = B(0xC5, 0xFF, 0xFF, 0xFF);
            TextTertiary = B(0x87, 0xFF, 0xFF, 0xFF);
            NavHover = B(0x0B, 0xFF, 0xFF, 0xFF);
            NavSelected = B(0x0F, 0xFF, 0xFF, 0xFF);
            Control = B(0x0F, 0xFF, 0xFF, 0xFF);
            ControlHover = B(0x15, 0xFF, 0xFF, 0xFF);
            ControlPressed = B(0x08, 0xFF, 0xFF, 0xFF);
            ControlBorder = B(0x18, 0xFF, 0xFF, 0xFF);
            Strong = B(0xC5, 0xFF, 0xFF, 0xFF);
            Track = B(0x8B, 0xFF, 0xFF, 0xFF);
            Divider = B(0x15, 0xFF, 0xFF, 0xFF);
            Preview = B(0x08, 0xFF, 0xFF, 0xFF);
        }
        Background = overBackdrop ? Brushes.Transparent : B(Solid);
    }

    public static SettingsPalette Light(AccentColors? accent = null, bool overBackdrop = false) =>
        new(true, accent ?? AccentColors.Default, overBackdrop);

    public static SettingsPalette Dark(AccentColors? accent = null, bool overBackdrop = false) =>
        new(false, accent ?? AccentColors.Default, overBackdrop);

    public bool IsLight { get; }
    /// <summary>Под окном системная подложка Mica — фон окна прозрачный.</summary>
    public bool OverBackdrop { get; }
    public Color Solid { get; }
    public Color AccentColor { get; }
    public Brush Background { get; }
    public Brush Card { get; }
    public Brush CardBorder { get; }
    public Brush Text { get; }
    public Brush TextSecondary { get; }
    public Brush TextTertiary { get; }
    public Brush NavHover { get; }
    public Brush NavSelected { get; }
    public Brush Control { get; }
    public Brush ControlHover { get; }
    public Brush ControlPressed { get; }
    public Brush ControlBorder { get; }
    /// <summary>Обводка и «ручка» выключенного переключателя, рамка пустого флажка.</summary>
    public Brush Strong { get; }
    /// <summary>Незаполненная часть ползунка.</summary>
    public Brush Track { get; }
    public Brush Divider { get; }
    /// <summary>Подложка под мини-колесом.</summary>
    public Brush Preview { get; }
    public Brush Accent { get; }
    public Brush OnAccent { get; }

    /// <summary>Подсказка (ToolTip) в цветах темы.</summary>
    public ToolTip MakeToolTip(string text) => new()
    {
        Content = text,
        Background = B(Solid),
        Foreground = Text,
        BorderBrush = ControlBorder,
        Padding = new Thickness(8, 5, 8, 6),
        FontFamily = UiFonts.Text,
        FontSize = 12,
    };

    internal static Brush B(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Brush B(byte a, byte r, byte g, byte b) => B(Color.FromArgb(a, r, g, b));
}

/// <summary>
/// Основа кликабельных элементов окна настроек: наведение, нажатие, Enter/Пробел, рамка фокуса с клавиатуры.
/// Без шаблонов WPF — вид не зависит от темы WPF.
/// </summary>
public abstract class Pressable : Border
{
    private bool _down;

    protected Pressable(SettingsPalette palette)
    {
        Palette = palette;
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        BorderThickness = new Thickness(1);
        BorderBrush = Brushes.Transparent;
    }

    protected SettingsPalette Palette { get; }
    protected bool IsHovered => IsMouseOver;
    protected bool IsPressed => _down;
    protected bool HasKeyboardFocus { get; private set; }

    public event Action? Click;

    /// <summary>Перерисовать фон/рамку по состоянию (наведение, нажатие, фокус).</summary>
    protected abstract void UpdateLook();

    protected virtual void OnClick() => Click?.Invoke();

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        UpdateLook();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        UpdateLook();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        e.Handled = true;
        _down = true;
        CaptureMouse();
        UpdateLook();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        e.Handled = true;
        if (!_down) return;
        _down = false;
        ReleaseMouseCapture();
        var p = e.GetPosition(this);
        var inside = p.X >= 0 && p.Y >= 0 && p.X <= ActualWidth && p.Y <= ActualHeight;
        UpdateLook();
        if (inside) OnClick();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _down = false;
        UpdateLook();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Enter or Key.Space)) return;
        e.Handled = true;
        OnClick();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        HasKeyboardFocus = InputManager.Current.MostRecentInputDevice is KeyboardDevice;
        UpdateLook();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        HasKeyboardFocus = false;
        UpdateLook();
    }
}

/// <summary>Кнопка Windows 11: светлая плашка с тонкой рамкой; текст и/или значок.</summary>
public sealed class SettingsButton : Pressable
{
    private readonly bool _subtle;

    /// <param name="subtle">Без плашки (как кнопки-значки в списке) — фон только при наведении.</param>
    public SettingsButton(SettingsPalette palette, string? text, string? glyph = null, string? tooltip = null, bool subtle = false)
        : base(palette)
    {
        _subtle = subtle;
        CornerRadius = new CornerRadius(4);
        Cursor = Cursors.Arrow;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (glyph is not null)
            panel.Children.Add(new TextBlock
            {
                Text = glyph, FontFamily = UiFonts.Icons, FontSize = 12, Foreground = palette.Text,
                VerticalAlignment = VerticalAlignment.Center,
            });
        if (text is not null)
            panel.Children.Add(new TextBlock
            {
                Text = text, FontSize = 13, Foreground = palette.Text, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(glyph is null ? 0 : 8, 0, 0, 1),
            });
        Child = panel;
        Padding = text is null ? new Thickness(6) : new Thickness(12, 5, 12, 6);
        MinHeight = 30;
        MinWidth = text is null ? 30 : 0;
        if (tooltip is not null) ToolTip = palette.MakeToolTip(tooltip);
        AutomationProperties.SetName(this, tooltip ?? text ?? "");
        UpdateLook();
    }

    /// <summary>Недоступна (например, «Выше» у первого пункта): бледная и не нажимается.</summary>
    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        Opacity = enabled ? 1 : 0.35;
    }

    protected override void UpdateLook()
    {
        var p = Palette;
        Background = IsPressed ? p.ControlPressed
            : IsHovered ? p.ControlHover
            : _subtle ? Brushes.Transparent : p.Control;
        BorderBrush = HasKeyboardFocus ? p.Text : _subtle ? Brushes.Transparent : p.ControlBorder;
    }
}

/// <summary>Ссылка: текст цвета акцента, подчёркивание при наведении.</summary>
public sealed class SettingsLink : Pressable
{
    private readonly TextBlock _text;

    public SettingsLink(SettingsPalette palette, string text) : base(palette)
    {
        _text = new TextBlock { Text = text, FontSize = 13, Foreground = palette.Accent };
        Child = _text;
        Padding = new Thickness(2, 1, 2, 2);
        CornerRadius = new CornerRadius(3);
        AutomationProperties.SetName(this, text);
        UpdateLook();
    }

    protected override void UpdateLook()
    {
        _text.TextDecorations = IsHovered ? TextDecorations.Underline : null;
        _text.Opacity = IsPressed ? 0.7 : 1;
        BorderBrush = HasKeyboardFocus ? Palette.Text : Brushes.Transparent;
    }
}

/// <summary>Переключатель Windows 11 (40×20): выключен — контур, включён — заливка акцентом.</summary>
public sealed class ToggleSwitch : Pressable
{
    private bool _isOn;

    public ToggleSwitch(SettingsPalette palette, bool isOn, string automationName) : base(palette)
    {
        _isOn = isOn;
        Width = 44;
        Height = 24;
        Padding = new Thickness(1);
        CornerRadius = new CornerRadius(12);
        Background = Brushes.Transparent; // щелчок по всему прямоугольнику, а не только по нарисованному
        Cursor = Cursors.Arrow;
        Child = new Canvas();
        AutomationProperties.SetName(this, automationName);
        UpdateLook();
    }

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;
            _isOn = value;
            UpdateLook();
        }
    }

    /// <summary>Пользователь переключил (не вызывается при установке IsOn из кода).</summary>
    public event Action<bool>? Toggled;

    protected override void OnClick()
    {
        IsOn = !IsOn;
        Toggled?.Invoke(IsOn);
        base.OnClick();
    }

    protected override void UpdateLook()
    {
        BorderBrush = HasKeyboardFocus ? Palette.Text : Brushes.Transparent;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var p = Palette;
        var rect = new Rect(2, 2, 40, 20);
        var r = rect.Height / 2;
        var knob = IsPressed ? 8.5 : IsHovered ? 7.5 : 6.5;
        if (_isOn)
        {
            dc.DrawRoundedRectangle(p.Accent, null, rect, r, r);
            dc.DrawEllipse(p.OnAccent, null, new Point(rect.Right - r, rect.Top + r), knob, knob);
        }
        else
        {
            var fill = IsHovered ? p.ControlHover : p.Control;
            dc.DrawRoundedRectangle(fill, new Pen(p.Strong, 1), new Rect(rect.X + 0.5, rect.Y + 0.5, rect.Width - 1, rect.Height - 1), r - 0.5, r - 0.5);
            dc.DrawEllipse(p.Strong, null, new Point(rect.Left + r, rect.Top + r), knob, knob);
        }
    }
}

/// <summary>Флажок Windows 11 (20×20): пустой — контур, отмечен — акцент с галочкой.</summary>
public sealed class CheckMark : Pressable
{
    private bool _isChecked;
    private readonly TextBlock _glyph;

    public CheckMark(SettingsPalette palette, bool isChecked, string automationName) : base(palette)
    {
        _isChecked = isChecked;
        Width = Height = 22;
        CornerRadius = new CornerRadius(5);
        Cursor = Cursors.Arrow;
        _glyph = new TextBlock
        {
            Text = Glyphs.Check, FontFamily = UiFonts.Icons, FontSize = 12, Foreground = palette.OnAccent,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        Child = _glyph;
        AutomationProperties.SetName(this, automationName);
        UpdateLook();
    }

    public bool IsChecked => _isChecked;

    public event Action<bool>? Changed;

    protected override void OnClick()
    {
        _isChecked = !_isChecked;
        UpdateLook();
        Changed?.Invoke(_isChecked);
        base.OnClick();
    }

    protected override void UpdateLook()
    {
        var p = Palette;
        _glyph.Visibility = _isChecked ? Visibility.Visible : Visibility.Hidden;
        Background = _isChecked ? p.Accent : IsHovered ? p.ControlHover : p.Control;
        BorderBrush = HasKeyboardFocus ? p.Text : _isChecked ? p.Accent : p.Strong;
        Opacity = IsPressed ? 0.8 : 1;
    }
}

/// <summary>
/// Выбор одного из нескольких вариантов «пилюлями» (вместо выпадающего списка): выбранная — акцентом.
/// Пилюли переносятся на новую строку, если не помещаются.
/// </summary>
public sealed class ChoicePills : WrapPanel
{
    private readonly SettingsPalette _palette;
    private readonly List<Pill> _pills = [];

    public ChoicePills(SettingsPalette palette, IEnumerable<(object Value, string Label)> items, object? selected, string automationName)
    {
        _palette = palette;
        AutomationProperties.SetName(this, automationName);
        foreach (var (value, label) in items)
        {
            var pill = new Pill(palette, value, label);
            pill.Click += () => Select(pill.Value, raise: true);
            _pills.Add(pill);
            Children.Add(pill);
        }
        Select(selected, raise: false);
    }

    public object? Selected { get; private set; }

    public event Action<object>? SelectionChanged;

    public void Select(object? value, bool raise)
    {
        Selected = value;
        foreach (var pill in _pills) pill.IsSelected = Equals(pill.Value, value);
        if (raise && value is not null) SelectionChanged?.Invoke(value);
    }

    private sealed class Pill : Pressable
    {
        private readonly TextBlock _text;
        private bool _selected;

        public Pill(SettingsPalette palette, object value, string label) : base(palette)
        {
            Value = value;
            CornerRadius = new CornerRadius(15);
            Padding = new Thickness(13, 5, 13, 6);
            Margin = new Thickness(0, 0, 6, 6);
            MinWidth = 44;
            Cursor = Cursors.Arrow;
            _text = new TextBlock { Text = label, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center };
            Child = _text;
            AutomationProperties.SetName(this, label);
            UpdateLook();
        }

        public object Value { get; }

        public bool IsSelected
        {
            get => _selected;
            set
            {
                _selected = value;
                UpdateLook();
            }
        }

        protected override void UpdateLook()
        {
            var p = Palette;
            if (_selected)
            {
                Background = p.Accent;
                _text.Foreground = p.OnAccent;
                BorderBrush = HasKeyboardFocus ? p.Text : p.Accent;
                _text.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                Background = IsPressed ? p.ControlPressed : IsHovered ? p.ControlHover : p.Control;
                _text.Foreground = p.Text;
                BorderBrush = HasKeyboardFocus ? p.Text : p.ControlBorder;
                _text.FontWeight = FontWeights.Normal;
            }
        }
    }
}

/// <summary>
/// Ползунок Windows 11 с целыми значениями: заполненная часть — акцентом, круглая «ручка» с точкой.
/// Мышь (перетаскивание, щелчок по дорожке, колесо мыши) и стрелки клавиатуры.
/// </summary>
public sealed class ValueSlider : FrameworkElement
{
    private const double ThumbRadius = 10;
    private readonly SettingsPalette _palette;
    private readonly int _min;
    private readonly int _max;
    private int _value;
    private bool _dragging;

    public ValueSlider(SettingsPalette palette, int min, int max, int value, string automationName)
    {
        _palette = palette;
        _min = min;
        _max = max;
        _value = Math.Clamp(value, min, max);
        Height = 28;
        MinWidth = 120;
        Focusable = true;
        FocusVisualStyle = null;
        AutomationProperties.SetName(this, automationName);
    }

    public int Value
    {
        get => _value;
        set => SetValue(value, raise: false);
    }

    /// <summary>Значение изменил пользователь.</summary>
    public event Action<int>? ValueChanged;

    private void SetValue(int value, bool raise)
    {
        value = Math.Clamp(value, _min, _max);
        if (value == _value) return;
        _value = value;
        InvalidateVisual();
        if (raise) ValueChanged?.Invoke(value);
    }

    private double TrackLeft => ThumbRadius + 1;
    private double TrackRight => Math.Max(TrackLeft + 1, ActualWidth - ThumbRadius - 1);

    private int ValueAt(double x)
    {
        var t = Math.Clamp((x - TrackLeft) / (TrackRight - TrackLeft), 0, 1);
        return _min + (int)Math.Round(t * (_max - _min));
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width, 28);

    protected override void OnRender(DrawingContext dc)
    {
        var p = _palette;
        var cy = ActualHeight / 2;
        var t = _max == _min ? 0 : (double)(_value - _min) / (_max - _min);
        var x = TrackLeft + t * (TrackRight - TrackLeft);
        // Прозрачная подложка — чтобы щелчок мимо дорожки тоже попадал в элемент.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.DrawRoundedRectangle(p.Track, null, new Rect(TrackLeft, cy - 2, TrackRight - TrackLeft, 4), 2, 2);
        dc.DrawRoundedRectangle(p.Accent, null, new Rect(TrackLeft, cy - 2, Math.Max(0, x - TrackLeft), 4), 2, 2);
        var ring = p.IsLight ? Brushes.White : PaletteBrush(Color.FromRgb(0x45, 0x45, 0x45));
        dc.DrawEllipse(ring, new Pen(p.ControlBorder, 1), new Point(x, cy), ThumbRadius, ThumbRadius);
        var inner = _dragging ? 4.5 : IsMouseOver ? 7 : 5.5;
        dc.DrawEllipse(p.Accent, null, new Point(x, cy), inner, inner);
        if (IsKeyboardFocused)
            dc.DrawEllipse(null, new Pen(p.Text, 1.5), new Point(x, cy), ThumbRadius + 3, ThumbRadius + 3);
    }

    private static Brush PaletteBrush(Color c) => SettingsPalette.B(c);

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        e.Handled = true;
        Focus();
        _dragging = true;
        CaptureMouse();
        SetValue(ValueAt(e.GetPosition(this).X), raise: true);
        InvalidateVisual();
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) SetValue(ValueAt(e.GetPosition(this).X), raise: true);
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnLostMouseCapture(System.Windows.Input.MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
        InvalidateVisual();
    }

    protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        InvalidateVisual();
    }

    protected override void OnMouseWheel(System.Windows.Input.MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!IsKeyboardFocused) return;
        e.Handled = true;
        SetValue(_value + Math.Sign(e.Delta), raise: true);
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = e.Key switch
        {
            Key.Left or Key.Down => -1,
            Key.Right or Key.Up => 1,
            Key.PageDown => -5,
            Key.PageUp => 5,
            Key.Home => _min - _value,
            Key.End => _max - _value,
            _ => 0,
        };
        if (step == 0) return;
        e.Handled = true;
        SetValue(_value + step, raise: true);
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
