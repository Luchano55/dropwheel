using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using DropWheel.Localization;
using DropWheel.Ui;
using DropWheel.Wheel;

namespace DropWheel.Capture;

/// <summary>
/// Окна записи экрана скрываются от захвата (решение D-RECORD): SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE),
/// Windows 10 2004+ — человек их видит, а ffmpeg (gdigrab, BitBlt рабочего стола) — нет.
/// Старее — функция не срабатывает: рамка ставится снаружи области, пилюля — тоже снаружи, если есть место.
/// </summary>
internal static partial class RecordNative
{
    private const uint WdaExcludeFromCapture = 0x11;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080, WsExNoActivate = 0x08000000;

    /// <summary>Скрыть окно от захвата экрана. false — не поддерживается (Windows старее 10 2004) или ошибка.</summary>
    public static bool TryExcludeFromCapture(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return false;
        try { return SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture); }
        catch (Exception) { return false; }
    }

    /// <summary>Не в Alt+Tab и не забирает фокус у записываемой программы при щелчке.</summary>
    public static void MakeNoActivate(IntPtr hwnd)
    {
        try
        {
            var style = GetWindowLongPtrW(hwnd, GwlExStyle).ToInt64();
            SetWindowLongPtrW(hwnd, GwlExStyle, new IntPtr(style | WsExToolWindow | WsExNoActivate));
        }
        catch (Exception) { }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);
}

/// <summary>
/// Тонкая красная рамка вокруг записываемой области: прозрачное окно поверх всего, мышь проходит сквозь него.
/// Скрыто от захвата — рамка по краю самой области, нет — снаружи (<see cref="ScreenRecord.FrameBounds"/>).
/// </summary>
internal sealed class RecordFrameWindow : Window
{
    /// <summary>Толщина рамки, физических пикселей.</summary>
    public const int Thickness = 2;

    public static readonly Color FrameColor = Color.FromRgb(0xE8, 0x1E, 0x25);

    private readonly PixelRect _region;
    private readonly Border _border = new() { BorderBrush = new SolidColorBrush(FrameColor), SnapsToDevicePixels = true };
    private IntPtr _handle;

    public RecordFrameWindow(PixelRect region)
    {
        _region = region;
        Title = "DropWheel";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        Content = _border;
        DpiChanged += (_, _) => Dispatcher.InvokeAsync(Place);
    }

    /// <summary>Окно скрыто от захвата (рамка — внутри области).</summary>
    public bool Excluded { get; private set; }

    public void ShowAround()
    {
        _handle = new WindowInteropHelper(this).EnsureHandle();
        WheelNative.MakeClickThrough(_handle);
        Excluded = RecordNative.TryExcludeFromCapture(_handle);
        Place();
        Show();
        Place();
    }

    private void Place()
    {
        if (_handle == IntPtr.Zero) return;
        var b = ScreenRecord.FrameBounds(_region, Thickness, Excluded);
        WheelNative.SetWindowPos(_handle, WheelNative.HwndTopmost, b.X, b.Y, b.Width, b.Height,
            WheelNative.SwpNoOwnerZOrder | WheelNative.SwpNoActivate);
        var scale = Math.Max(0.5, VisualTreeHelper.GetDpi(this).DpiScaleX);
        _border.BorderThickness = new System.Windows.Thickness(Thickness / scale);
    }
}

/// <summary>Пилюля «● 00:12  Стоп» (её же рисует скриншот-тест).</summary>
internal sealed class RecordPillView : Border
{
    private readonly TextBlock _time = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, MinWidth = 40 };

    public RecordPillView(SettingsPalette palette)
    {
        Background = SettingsPalette.B(palette.Solid);
        BorderBrush = palette.ControlBorder;
        BorderThickness = new System.Windows.Thickness(1);
        CornerRadius = new CornerRadius(8);
        Padding = new System.Windows.Thickness(12, 4, 4, 4);
        _time.FontFamily = UiFonts.Text;
        _time.Foreground = palette.Text;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Ellipse
        {
            Width = 10, Height = 10, Fill = new SolidColorBrush(RecordFrameWindow.FrameColor),
            VerticalAlignment = VerticalAlignment.Center, Margin = new System.Windows.Thickness(0, 0, 8, 0),
        });
        row.Children.Add(_time);
        row.Children.Add(new Border { Width = 1, Margin = new System.Windows.Thickness(8, 6, 4, 6), Background = palette.Divider });
        var stop = new SettingsButton(palette, Loc.T.RecordStop, StopGlyph, Loc.T.TrayRecordStop, subtle: true);
        stop.Click += () => StopRequested?.Invoke();
        row.Children.Add(stop);
        Child = row;
        Elapsed = TimeSpan.Zero;
    }

    /// <summary>Квадрат «стоп» (Segoe Fluent Icons).</summary>
    private const string StopGlyph = "";

    public event Action? StopRequested;

    public TimeSpan Elapsed
    {
        set => _time.Text = ScreenRecord.FormatElapsed(value);
    }
}

/// <summary>
/// Окно пилюли у записываемой области: под ней справа, не помещается — над ней, нигде — внутри у нижнего края
/// (как панель снимка, <see cref="CaptureLayout.Toolbar"/>). Поверх всего, не забирает фокус, скрыто от захвата.
/// </summary>
internal sealed class RecordPillWindow : Window
{
    private readonly PixelRect _region;
    private readonly CaptureMonitor _monitor;
    private IntPtr _handle;

    public RecordPillWindow(PixelRect region, IReadOnlyList<CaptureMonitor> monitors)
    {
        _region = region;
        // Монитор с правым нижним углом области (там же была панель «Записать»).
        var corner = (X: region.Right - 1, Y: region.Bottom - 1);
        _monitor = monitors.FirstOrDefault(m => m.Bounds.Contains(corner.X, corner.Y))
                   ?? monitors.MinBy(m => m.Bounds.DistanceSquared(corner.X, corner.Y))!;
        var palette = SystemTheme.AppsLight ? SettingsPalette.Light(SystemTheme.Accent) : SettingsPalette.Dark(SystemTheme.Accent);
        View = new RecordPillView(palette);
        Title = "DropWheel";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = SettingsPalette.B(palette.Solid);
        Content = View;
        DpiChanged += (_, _) => Dispatcher.InvokeAsync(Place);
    }

    public RecordPillView View { get; }

    /// <summary>Скрыто от захвата.</summary>
    public bool Excluded { get; private set; }

    public void ShowNearRegion()
    {
        _handle = new WindowInteropHelper(this).EnsureHandle();
        RecordNative.MakeNoActivate(_handle);
        NativeEffects.SetCorners(_handle, NativeEffects.CornerRound);
        Excluded = RecordNative.TryExcludeFromCapture(_handle);
        Place();
        Show();
        Place();
    }

    private void Place()
    {
        if (_handle == IntPtr.Zero) return;
        View.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = View.DesiredSize;
        var area = new DipRect(0, 0, _monitor.DipWidth, _monitor.DipHeight);
        var (x, y) = CaptureLayout.Toolbar(_monitor.ToLocalDip(_region), size.Width, size.Height, area);
        var (sx, sy) = _monitor.ToScreen(x, y);
        WheelNative.SetWindowPos(_handle, WheelNative.HwndTopmost, sx, sy,
            (int)Math.Ceiling(size.Width * _monitor.Scale), (int)Math.Ceiling(size.Height * _monitor.Scale),
            WheelNative.SwpNoOwnerZOrder | WheelNative.SwpNoActivate);
    }
}
