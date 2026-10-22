using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using DropWheel.Conversions;
using DropWheel.Jobs;
using DropWheel.Logging;
using DropWheel.Shell;
using DropWheel.Ui;
using DropWheel.Wheel;
using DropWheel.Localization;

namespace DropWheel.App;

/// <summary>
/// Окно задач у трея (раздел 8 ТЗ) в виде всплывающего окна Windows 11: без рамки, со скруглёнными углами
/// (Windows 11), системной акриловой подложкой (Windows 11 22H2+; иначе — сплошной фон темы),
/// над областью уведомлений на мониторе с курсором (панель задач — у любого края).
/// Появляется с «выездом» от панели задач, прячется, когда уходит фокус или по Esc.
/// </summary>
internal sealed class JobsWindow : Window
{
    private const double EdgeMargin = 12;
    private const double SlideDistance = 16;
    private const double ShowMs = 220;
    private const int ThumbPx = 96;
    /// <summary>Клик по значку в трее сначала снимает фокус с окна (оно прячется) — не открывать его тут же снова.</summary>
    private const long ReopenGuardMs = 350;

    private readonly JobQueue _queue;
    private readonly FileLog _log;
    private readonly JobsFlyoutView _view;
    private readonly Tween _tween = new();
    private readonly HashSet<Job> _cleared = [];
    private readonly Dictionary<Job, Thumb> _thumbs = [];
    private IntPtr _hwnd;
    private bool _backdrop;
    private string _paletteKey = "";
    private bool _refreshQueued;
    private long _hiddenAt;
    private (int X, int Y) _anchor;
    private TaskbarEdge _edge;
    private int _heightPx;

    private sealed record Thumb(string Path, ImageSource? Image, bool IsIcon);

    public JobsWindow(JobQueue queue, FileLog log, string hotkeyName)
    {
        _queue = queue;
        _log = log;
        Title = Loc.T.JobsTitle;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.Manual;
        Width = JobsFlyoutView.FlyoutWidth;
        Height = 240;
        UseLayoutRounding = true;

        // Рамки нет (клиентская область — всё окно), но окно остаётся «настоящим»: тень и скругление от Windows.
        // «Стекло» на всё окно нужно только для системной подложки Windows 11 22H2+.
        var glass = NativeEffects.SupportsSystemBackdrop && NativeEffects.EffectsAllowed;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = glass ? new Thickness(-1) : new Thickness(1),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
            NonClientFrameEdges = NonClientFrameEdges.None,
        });

        _view = new JobsFlyoutView(FlyoutPalette.Light(), hotkeyName);
        _view.OpenRequested += key => Act(key, job => Notifier.Open(job.OutputPath));
        _view.RevealRequested += key => Act(key, job => Notifier.RevealInExplorer(job.OutputPath ?? job.Input));
        _view.CancelRequested += key => Act(key, job => job.Cancel());
        _view.DetailsRequested += _ => Notifier.OpenLog(_log);
        _view.ClearRequested += () =>
        {
            foreach (var job in _queue.Jobs.Where(j => j.State is JobState.Done or JobState.Failed or JobState.Canceled))
                _cleared.Add(job);
            RefreshNow();
        };
        Content = _view;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            WheelNative.RemoveSystemMenu(_hwnd);
            WheelNative.MakeToolWindow(_hwnd);
            ApplyTheme();
        };
        Deactivated += (_, _) => HideFlyout();
        SystemTheme.Changed += OnThemeChanged;
    }

    /// <summary>Сменился язык интерфейса: заголовок, подписи и строки задач — заново.</summary>
    public void Relocalize(string hotkeyName)
    {
        Title = Loc.T.JobsTitle;
        _view.Relocalize(hotkeyName);
        RefreshNow();
    }

    /// <summary>Горячую клавишу сменили в настройках — подсказка в пустом окне называет новую.</summary>
    public string HotkeyName
    {
        set => _view.HotkeyName = value;
    }

    /// <summary>Показать у трея (повторный запуск программы, пункт меню «Задачи»).</summary>
    public void ShowNearTray() => ShowFlyout();

    /// <summary>Клик по значку в трее: открыть или спрятать.</summary>
    public void Toggle()
    {
        if (IsVisible) HideFlyout();
        else if (Environment.TickCount64 - _hiddenAt > ReopenGuardMs) ShowFlyout();
    }

    /// <summary>Задача изменилась. Пока окно спрятано — ничего не делаем, обновим при показе.</summary>
    public void Refresh(Job job)
    {
        if (!IsVisible || _refreshQueued) return;
        // Прогресс ffmpeg приходит часто — обновляем не чаще одного раза за проход диспетчера.
        _refreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _refreshQueued = false;
            if (IsVisible) RefreshNow();
        });
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        HideFlyout();
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemTheme.Changed -= OnThemeChanged;
        base.OnClosed(e);
    }

    private void ShowFlyout()
    {
        new WindowInteropHelper(this).EnsureHandle();
        ApplyTheme();
        RefreshNow(place: false);
        _anchor = WheelNative.CursorPosition() ?? (0, 0);

        var appearing = !IsVisible;
        Place();
        Place();
        if (appearing && Motion.Enabled) SetAppearance(0);
        if (appearing) Show();
        Place();
        WheelNative.TrySetForeground(_hwnd);
        Activate();

        if (appearing) _tween.Run(ShowMs, Motion.EaseOutCubic, SetAppearance);
    }

    private void HideFlyout()
    {
        if (!IsVisible) return;
        _tween.Stop(complete: false);
        Hide();
        SetAppearance(1);
        _hiddenAt = Environment.TickCount64;
    }

    /// <summary>Появление: содержимое проявляется и «выезжает» от панели задач.</summary>
    private void SetAppearance(double t)
    {
        _view.Opacity = t;
        var d = SlideDistance * (1 - t);
        (_view.Slide.X, _view.Slide.Y) = _edge switch
        {
            TaskbarEdge.Top => (0.0, -d),
            TaskbarEdge.Left => (-d, 0.0),
            TaskbarEdge.Right => (d, 0.0),
            _ => (0.0, d),
        };
    }

    private void RefreshNow(bool place = true)
    {
        var jobs = _queue.Jobs.Where(j => !_cleared.Contains(j)).Reverse().ToList();
        foreach (var job in jobs) EnsureThumbnail(job);
        _view.SetRows(jobs.Select(ModelFor).ToList());
        if (place && IsVisible) Place(onlyIfHeightChanged: true);
    }

    private JobRowModel ModelFor(Job job)
    {
        var name = Path.GetFileName(job.Input);
        if (job.Inputs.Count > 1) name = Loc.F(Loc.T.AndMore, name, job.Inputs.Count - 1);
        var label = Loc.Label(job.Operation);
        var operation = job.Operation.Mode == OperationMode.Convert ? "→ " + label : label;
        _thumbs.TryGetValue(job, out var thumb);
        return new JobRowModel(job, name, operation, job.State)
        {
            Progress = job.Progress,
            Error = job.Error,
            ResultName = job.OutputPath is { } output ? Path.GetFileName(output.TrimEnd('\\', '/')) : null,
            Glyph = OperationGlyphs.For(job.Operation),
            Thumbnail = thumb?.Image,
            ThumbnailIsIcon = thumb?.IsIcon ?? false,
        };
    }

    /// <summary>Миниатюра: у готовой задачи — результата, иначе — исходника. Грузится в фоне.</summary>
    private void EnsureThumbnail(Job job)
    {
        var path = job.State == JobState.Done && job.OutputPath is not null ? job.OutputPath : job.Input;
        if (_thumbs.TryGetValue(job, out var current) && current.Path == path) return;
        // Пока грузится новая — показываем старую.
        _thumbs[job] = new Thumb(path, current?.Image, current?.IsIcon ?? false);
        _ = LoadThumbnailAsync(job, path);
    }

    private async Task LoadThumbnailAsync(Job job, string path)
    {
        try
        {
            var image = await ShellThumbnails.GetAsync(path, ThumbPx, thumbnailOnly: true);
            var isIcon = false;
            if (image is null)
            {
                image = await ShellThumbnails.GetAsync(path, 64);
                isIcon = true;
            }
            if (image is null || !_thumbs.TryGetValue(job, out var current) || current.Path != path) return;
            _thumbs[job] = new Thumb(path, image, isIcon);
            if (IsVisible) RefreshNow();
        }
        catch (Exception ex)
        {
            _log.Write("Миниатюра не получилась: " + ex.Message);
        }
    }

    private void Act(object key, Action<Job> action)
    {
        if (key is not Job job) return;
        try { action(job); }
        catch (Exception ex) { _log.Write("Действие в окне задач не выполнено: " + ex.Message); }
    }

    // ---- вид: тема, подложка, место ----

    private void OnThemeChanged()
    {
        if (!IsVisible) return;
        ApplyTheme();
        Place();
    }

    private void ApplyTheme()
    {
        if (_hwnd == IntPtr.Zero) return;
        var light = SystemTheme.ShellLight;
        NativeEffects.SetDarkMode(_hwnd, !light);
        NativeEffects.SetCorners(_hwnd, NativeEffects.CornerRound);

        var wantBackdrop = SystemTheme.TransparencyEnabled && NativeEffects.SupportsSystemBackdrop && NativeEffects.EffectsAllowed;
        _backdrop = wantBackdrop && NativeEffects.SetSystemBackdrop(_hwnd, NativeEffects.BackdropTransient);
        if (!_backdrop && NativeEffects.SupportsSystemBackdrop) NativeEffects.SetSystemBackdrop(_hwnd, NativeEffects.BackdropNone);

        var accent = SystemTheme.Accent;
        var key = $"{light}|{_backdrop}|{accent.Base}";
        if (key != _paletteKey)
        {
            _paletteKey = key;
            _view.Palette = light ? FlyoutPalette.Light(accent, _backdrop) : FlyoutPalette.Dark(accent, _backdrop);
        }

        if (_backdrop)
        {
            // Под прозрачным фоном WPF видна подложка Windows.
            Background = Brushes.Transparent;
            if (HwndSource.FromHwnd(_hwnd)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
        }
        else
        {
            var solid = new SolidColorBrush(_view.Palette.Solid);
            solid.Freeze();
            Background = solid;
        }
    }

    /// <summary>
    /// Размер по содержимому (ширина 380 DIP, высота — по строкам) и место у трея на мониторе, где был курсор.
    /// Всё в физических пикселях монитора (Per-Monitor DPI).
    /// </summary>
    private void Place(bool onlyIfHeightChanged = false)
    {
        if (_hwnd == IntPtr.Zero) return;
        var (work, bounds, dpi) = WheelNative.MonitorAreasAt(_anchor.X, _anchor.Y);
        var k = dpi / 96.0;
        _view.Measure(new Size(JobsFlyoutView.FlyoutWidth, double.PositiveInfinity));
        var width = (int)Math.Round(JobsFlyoutView.FlyoutWidth * k);
        var height = (int)Math.Ceiling(_view.DesiredSize.Height * k);
        if (onlyIfHeightChanged && height == _heightPx) return;
        _heightPx = height;

        var taskbar = WheelNative.TaskbarRect() is { } t ? ToPixels(t) : (PixelRect?)null;
        var (x, y, edge) = TrayFlyoutPlacement.Place(ToPixels(bounds), ToPixels(work), taskbar, width, height,
            (int)Math.Round(EdgeMargin * k));
        _edge = edge;
        WheelNative.SetWindowPos(_hwnd, WheelNative.HwndTopmost, x, y, width, height,
            WheelNative.SwpNoActivate | WheelNative.SwpNoOwnerZOrder);
    }

    private static PixelRect ToPixels(WheelNative.Rect r) => new(r.Left, r.Top, r.Right, r.Bottom);
}
