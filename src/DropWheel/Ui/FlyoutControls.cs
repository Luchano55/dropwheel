using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DropWheel.Ui;

/// <summary>
/// Плоская кнопка в стиле Windows 11 (значок или текст): прозрачная, подсветка при наведении и нажатии.
/// Без шаблонов WPF — всё в коде, чтобы вид не зависел от темы WPF.
/// </summary>
public sealed class FlyoutButton : Border
{
    private readonly Brush _hover;
    private readonly Brush _pressed;
    private readonly Brush _focus;
    private bool _down;

    public event Action? Click;

    /// <param name="glyph">Значок (Glyphs) или null.</param>
    /// <param name="text">Подпись или null.</param>
    /// <param name="tooltip">Подсказка при наведении.</param>
    public FlyoutButton(string? glyph, string? text, string? tooltip, Brush foreground, Brush hover, Brush pressed,
        Brush focus, ToolTip? toolTip = null)
    {
        _hover = hover;
        _pressed = pressed;
        _focus = focus;
        Background = Brushes.Transparent;
        CornerRadius = new CornerRadius(4);
        BorderThickness = new Thickness(1);
        BorderBrush = Brushes.Transparent;
        VerticalAlignment = VerticalAlignment.Center;
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Arrow;

        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (glyph is not null)
            panel.Children.Add(new TextBlock
            {
                Text = glyph,
                FontFamily = UiFonts.Icons,
                FontSize = 14,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
            });
        if (text is not null)
            panel.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(glyph is null ? 0 : 6, 0, 0, 1),
            });
        Child = panel;
        Padding = text is null ? new Thickness(8) : new Thickness(10, 5, 10, 5);
        MinWidth = 32;
        MinHeight = 32;

        if (tooltip is not null)
        {
            if (toolTip is not null)
            {
                toolTip.Content = tooltip;
                ToolTip = toolTip;
            }
            else ToolTip = tooltip;
        }
        AutomationProperties.SetName(this, tooltip ?? text ?? "");
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        Background = _down ? _pressed : _hover;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_down) Background = Brushes.Transparent;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        e.Handled = true;
        _down = true;
        Background = _pressed;
        CaptureMouse();
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
        Background = inside ? _hover : Brushes.Transparent;
        if (inside) Click?.Invoke();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _down = false;
        if (!IsMouseOver) Background = Brushes.Transparent;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Enter or Key.Space)) return;
        e.Handled = true;
        Click?.Invoke();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (InputManager.Current.MostRecentInputDevice is KeyboardDevice) BorderBrush = _focus;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        BorderBrush = Brushes.Transparent;
    }
}

/// <summary>
/// Тонкая полоска прогресса (3 px, скруглённая), как в Windows 11. Значение подтягивается плавно;
/// «бегущий» отрезок — когда прогресс неизвестен. Анимации — только пока полоска видна.
/// </summary>
public sealed class SlimProgressBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(SlimProgressBar),
        new FrameworkPropertyMetadata(0.0, (d, _) => ((SlimProgressBar)d).AnimateValue()));

    public static readonly DependencyProperty IsIndeterminateProperty = DependencyProperty.Register(
        nameof(IsIndeterminate), typeof(bool), typeof(SlimProgressBar),
        new FrameworkPropertyMetadata(false, (d, _) => ((SlimProgressBar)d).UpdatePhase()));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(SlimProgressBar),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(SlimProgressBar),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty ShownProperty = DependencyProperty.Register(
        "Shown", typeof(double), typeof(SlimProgressBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        "Phase", typeof(double), typeof(SlimProgressBar),
        new FrameworkPropertyMetadata(0.35, FrameworkPropertyMetadataOptions.AffectsRender));

    public SlimProgressBar()
    {
        Height = 3;
        IsVisibleChanged += (_, _) => UpdatePhase();
    }

    /// <summary>0..1.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    public Brush Track
    {
        get => (Brush)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, 3);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var r = h / 2;
        dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), r, r);
        if (IsIndeterminate)
        {
            // Отрезок 35 % ширины пробегает слева направо.
            var phase = (double)GetValue(PhaseProperty);
            var len = w * 0.35;
            var x = -len + (w + len) * phase;
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), r, r));
            dc.DrawRoundedRectangle(Fill, null, new Rect(x, 0, len, h), r, r);
            dc.Pop();
            return;
        }
        var shown = Math.Clamp((double)GetValue(ShownProperty), 0, 1);
        if (shown > 0) dc.DrawRoundedRectangle(Fill, null, new Rect(0, 0, Math.Max(h, w * shown), h), r, r);
    }

    private void AnimateValue()
    {
        var target = Math.Clamp(Value, 0, 1);
        if (!Motion.Enabled || !IsVisible || target < (double)GetValue(ShownProperty))
        {
            BeginAnimation(ShownProperty, null);
            SetValue(ShownProperty, target);
            return;
        }
        BeginAnimation(ShownProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    /// <summary>«Бег» отрезка крутится только пока полоска на экране — скрытое окно не тратит процессор.</summary>
    private void UpdatePhase()
    {
        if (IsIndeterminate && IsVisible && Motion.Enabled)
        {
            BeginAnimation(PhaseProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1500))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        }
        else
        {
            BeginAnimation(PhaseProperty, null);
            InvalidateVisual();
        }
    }
}
