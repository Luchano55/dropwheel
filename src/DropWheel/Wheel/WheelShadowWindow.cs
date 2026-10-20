using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DropWheel.Wheel;

/// <summary>
/// Мягкая тень под колесом, когда у колеса системное размытие: круглая форма окна колеса обрезала бы
/// тень, поэтому её рисует это окно — того же размера, прямо под колесом. Окно прозрачно для мыши
/// и перетаскивания (WS_EX_TRANSPARENT), не активируется и не попадает в Alt+Tab.
/// Повторяет масштаб и прозрачность колеса во время анимаций.
/// </summary>
internal sealed class WheelShadowWindow : Window
{
    private readonly ShadowElement _element = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private IntPtr _hwnd;

    public WheelShadowWindow()
    {
        Title = "DropWheel";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        AllowDrop = false;
        Width = Height = WheelWindow.DipSize;
        _element.RenderTransform = _scale;
        Content = _element;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            WheelNative.MakeClickThrough(_hwnd);
        };
    }

    public void SetColor(Color shadow)
    {
        if (_element.Shadow == shadow) return;
        _element.Shadow = shadow;
        _element.InvalidateVisual();
    }

    /// <summary>Масштаб относительно точки origin (DIP окна) и прозрачность — как у колеса.</summary>
    public void SetAppearance(Point origin, double scale, double opacity)
    {
        _scale.CenterX = origin.X;
        _scale.CenterY = origin.Y;
        _scale.ScaleX = _scale.ScaleY = scale;
        _element.Opacity = opacity;
    }

    /// <summary>Поставить окно в тот же прямоугольник, что и колесо, сразу под ним по Z-порядку.</summary>
    public void PlaceBelow(IntPtr wheel, int left, int top, int size)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        _hwnd = hwnd;
        WheelNative.SetWindowPos(hwnd, wheel == IntPtr.Zero ? WheelNative.HwndTopmost : wheel, left, top, size, size,
            WheelNative.SwpNoActivate | WheelNative.SwpNoOwnerZOrder);
    }

    private sealed class ShadowElement : FrameworkElement
    {
        public Color Shadow { get; set; } = Color.FromArgb(0x66, 0, 0, 0);

        protected override void OnRender(DrawingContext dc) =>
            WheelControl.DrawDiscShadow(dc, new Point(ActualWidth / 2, ActualHeight / 2), WheelControl.DiscRadius, Shadow);
    }
}
