using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DropWheel.Ui;
using DropWheel.Wheel;

namespace DropWheel.Capture;

/// <summary>
/// Плашка обратного отсчёта «3… 2… 1…» у курсора перед снимком с задержкой (D-CAPTURE). Поверх всех окон,
/// не активируется, мышь проходит сквозь неё; перед стоп-кадром закрывается, поэтому в снимок не попадает.
/// </summary>
internal sealed class CountdownPill : Window
{
    private readonly TextBlock _text = new()
    {
        FontFamily = UiFonts.Display, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    public CountdownPill()
    {
        Title = "DropWheel";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Left = -10000;
        Top = -10000;
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x20)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18, 4, 18, 6),
            MinWidth = 64,
            Child = _text,
        };
        SourceInitialized += (_, _) => WheelNative.MakeClickThrough(new WindowInteropHelper(this).Handle);
    }

    public void SetText(string text) => _text.Text = text;

    /// <summary>Показать справа снизу от курсора; у края экрана — с другой стороны, чтобы не уйти за край.</summary>
    public void ShowNearCursor(string text)
    {
        SetText(text);
        Show();
        UpdateLayout();
        if (WheelNative.CursorPosition() is not { } cursor) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var w = (int)Math.Ceiling(ActualWidth * scale);
        var h = (int)Math.Ceiling(ActualHeight * scale);
        var gap = (int)(20 * scale);
        var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y)).WorkingArea;
        var x = cursor.X + gap + w <= area.Right ? cursor.X + gap : cursor.X - gap - w;
        var y = cursor.Y + gap + h <= area.Bottom ? cursor.Y + gap : cursor.Y - gap - h;
        const uint NoSize = 0x0001, NoZOrder = 0x0004, NoActivate = 0x0010;
        WheelNative.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, NoSize | NoZOrder | NoActivate);
    }
}
