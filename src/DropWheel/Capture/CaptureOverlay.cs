using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DropWheel.Logging;
using DropWheel.Ui;
using DropWheel.Wheel;

namespace DropWheel.Capture;

/// <summary>
/// Оверлей снимка экрана (решение D-CAPTURE): по окну без рамки поверх всего на каждый монитор — так у каждого свой DPI
/// (Per-Monitor V2), и стоп-кадр показывается пиксель в пиксель. Мышь — через GetCursorPos (физические пиксели
/// виртуального экрана), поэтому выделение может идти через несколько мониторов с разным масштабом.
/// Выбрали действие — оверлей закрывается и отдаёт вырезанную картинку в <see cref="Finished"/>;
/// Esc, правый щелчок, переход в другое окно — закрывается без действия. После закрытия ссылок на снимок не остаётся.
/// </summary>
internal sealed class CaptureOverlay
{
    private readonly ScreenCapture.Frame _frame;
    private readonly CaptureSession _session;
    private readonly List<(CaptureMonitor Monitor, CaptureWindow Window)> _windows = [];
    private readonly FileLog _log;
    private CaptureMonitor? _hintMonitor;
    private bool _closed;
    /// <summary>Хоть одно окно оверлея было активным: только тогда потеря фокуса закрывает оверлей.</summary>
    private bool _wasActive;

    public CaptureOverlay(ScreenCapture.Frame frame, bool ocr, FileLog log)
    {
        _frame = frame;
        _log = log;
        _session = new CaptureSession(frame.Monitors.Select(m => m.Bounds).ToList(), frame.Windows);
        var palette = SystemTheme.AppsLight ? SettingsPalette.Light(SystemTheme.Accent) : SettingsPalette.Dark(SystemTheme.Accent);
        foreach (var monitor in frame.Monitors)
        {
            var b = monitor.Bounds;
            var part = new CroppedBitmap(frame.Image, new Int32Rect(b.X - frame.Bounds.X, b.Y - frame.Bounds.Y, b.Width, b.Height));
            part.Freeze();
            var view = new CaptureView(palette, ocr) { Frame = part, Width = monitor.DipWidth, Height = monitor.DipHeight };
            var window = new CaptureWindow(monitor, view);
            view.ActionRequested += Finish;
            view.CloseRequested += Close;
            window.MouseMove += (_, _) => OnMove();
            window.MouseLeftButtonDown += (_, e) => OnPress(window, e);
            window.MouseLeftButtonUp += (_, _) => OnRelease();
            window.LostMouseCapture += (_, _) => { if (_session.IsDragging) OnRelease(); };
            window.MouseRightButtonUp += (_, e) => { e.Handled = true; Close(); };
            window.PreviewKeyDown += OnKey;
            window.Activated += (_, _) => _wasActive = true;
            window.Deactivated += (_, _) => window.Dispatcher.BeginInvoke(CloseIfInactive, System.Windows.Threading.DispatcherPriority.Background);
            _windows.Add((monitor, window));
        }
    }

    /// <summary>Выбрано действие: вырезанная область (отдельная копия, снимок целиком уже отпущен) и что с ней сделать.</summary>
    public event Action<BitmapSource, CaptureAction>? Finished;

    /// <summary>Оверлей закрыт (с действием или без).</summary>
    public event Action? Closed;

    public void Show()
    {
        var cursor = WheelNative.CursorPosition() ?? (_frame.Bounds.X, _frame.Bounds.Y);
        _hintMonitor = MonitorWindowAt(cursor.X, cursor.Y).Monitor;
        _session.Hover(cursor.X, cursor.Y);
        foreach (var (_, window) in _windows) window.ShowOnMonitor();
        Render();
        // Клавиатура (Esc, Enter) — окну под курсором.
        var active = MonitorWindowAt(cursor.X, cursor.Y).Window;
        WheelNative.TrySetForeground(active.Handle);
        active.Activate();
        active.Focus();
        _log.Write($"Снимок экрана: {_frame.Bounds}, мониторов {_frame.Monitors.Count} " +
                   $"({string.Join(", ", _frame.Monitors.Select(m => $"{m.Bounds} {m.Dpi} dpi"))}), окон {_frame.Windows.Count}");
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        foreach (var (_, window) in _windows)
        {
            window.View.Frame = null;
            window.Close();
        }
        _windows.Clear();
        Closed?.Invoke();
    }

    private void OnMove()
    {
        if (Cursor() is not { } p) return;
        if (_session.IsDragging) _session.Move(p.X, p.Y);
        else _session.Hover(p.X, p.Y);
        Render();
    }

    private void OnPress(CaptureWindow window, MouseButtonEventArgs e)
    {
        if (Cursor() is not { } p) return;
        e.Handled = true;
        if (e.ClickCount >= 2) _session.SelectMonitor(p.X, p.Y);
        else
        {
            _hintMonitor = null;
            _session.Press(p.X, p.Y);
            // Мышь — этому окну до отпускания, даже если курсор ушёл на другой монитор.
            window.CaptureMouse();
        }
        Render();
    }

    private void OnRelease()
    {
        if (!_session.IsDragging) return;
        var p = Cursor();
        if (p is { } c) _session.Release(c.X, c.Y);
        else _session.Clear(0, 0);
        if (Mouse.Captured is not null) Mouse.Capture(null);
        Render();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Close();
                break;
            case Key.Enter:
                e.Handled = true;
                if (_session.Selection is not null) Finish(CaptureAction.Copy);
                else if (Cursor() is { } p)
                {
                    _hintMonitor = null;
                    _session.SelectMonitor(p.X, p.Y);
                    Render();
                }
                break;
            case Key.C when ctrl:
                e.Handled = true;
                Finish(CaptureAction.Copy);
                break;
            case Key.S when ctrl:
                e.Handled = true;
                Finish(CaptureAction.Save);
                break;
        }
    }

    private void Finish(CaptureAction action)
    {
        if (_closed || _session.Selection is not { } selection) return;
        BitmapSource image;
        try
        {
            image = ScreenCapture.Crop(_frame, selection);
        }
        catch (Exception ex)
        {
            _log.Write("Снимок экрана: не удалось вырезать область: " + ex.Message);
            Close();
            return;
        }
        _log.Write($"Снимок экрана: {action}, область {selection}");
        Close();
        Finished?.Invoke(image, action);
    }

    /// <summary>Ушли в другое окно (Alt+Tab, Win) — оверлей не должен висеть поверх всего.</summary>
    private void CloseIfInactive()
    {
        if (_closed || !_wasActive || _windows.Any(w => w.Window.IsActive)) return;
        _log.Write("Снимок экрана: окно потеряло фокус — закрываю");
        Close();
    }

    private void Render()
    {
        if (_closed) return;
        var highlight = _session.Highlight;
        var selection = _session.Selection;
        var size = highlight is { } h ? CaptureLayout.SizeText(h) : null;
        // Панель — на мониторе, где правый нижний угол выделения.
        var toolbarMonitor = selection is { } s ? MonitorWindowAt(s.Right - 1, s.Bottom - 1).Monitor : null;
        foreach (var (monitor, window) in _windows)
        {
            var local = highlight is { } r ? monitor.ToLocalDip(r) : (DipRect?)null;
            // Подпись размера — только на мониторе с левым верхним углом подсветки.
            var labelHere = highlight is { } hl && ReferenceEquals(MonitorWindowAt(hl.X, hl.Y).Monitor, monitor);
            window.View.Show(local, labelHere ? size : null, toolbar: ReferenceEquals(monitor, toolbarMonitor),
                hint: ReferenceEquals(monitor, _hintMonitor) && selection is null && !_session.IsDragging);
        }
    }

    private (CaptureMonitor Monitor, CaptureWindow Window) MonitorWindowAt(int x, int y)
    {
        var bounds = _session.MonitorAt(x, y);
        foreach (var pair in _windows)
            if (pair.Monitor.Bounds == bounds) return pair;
        return _windows[0];
    }

    private static (int X, int Y)? Cursor() => WheelNative.CursorPosition();
}

/// <summary>Окно оверлея на одном мониторе: без рамки, поверх всего, не в Alt+Tab и панели задач, ровно по монитору.</summary>
internal sealed class CaptureWindow : Window
{
    private readonly CaptureMonitor _monitor;

    public CaptureWindow(CaptureMonitor monitor, CaptureView view)
    {
        _monitor = monitor;
        View = view;
        Title = "DropWheel";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        Topmost = true;
        Background = System.Windows.Media.Brushes.Black;
        Width = monitor.DipWidth;
        Height = monitor.DipHeight;
        Content = view;
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            WheelNative.MakeToolWindow(Handle);
            NativeEffects.SetCorners(Handle, NativeEffects.CornerDoNotRound);
        };
        // Переехали на монитор с другим масштабом — WPF меняет размер окна; ставим точные границы ещё раз.
        DpiChanged += (_, _) => Dispatcher.InvokeAsync(Place);
    }

    public CaptureView View { get; }
    public IntPtr Handle { get; private set; }

    /// <summary>Как у колеса: SetWindowPos дважды до показа (первый может сменить DPI) и раз после.</summary>
    public void ShowOnMonitor()
    {
        Handle = new WindowInteropHelper(this).EnsureHandle();
        Place();
        Place();
        Show();
        Place();
    }

    private void Place()
    {
        if (Handle == IntPtr.Zero) return;
        var b = _monitor.Bounds;
        WheelNative.SetWindowPos(Handle, WheelNative.HwndTopmost, b.X, b.Y, b.Width, b.Height, WheelNative.SwpNoOwnerZOrder);
    }
}
