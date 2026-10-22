using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Localization;
using DropWheel.Logging;
using DropWheel.Ui;
using DropWheel.Wheel;

namespace DropWheel.App;

/// <summary>
/// Окно «Обрезать видео» (D-TRIM) для одного ролика: открывается после выбора «Обрезать…» в колесе
/// (перетаскивание, меню проводника, буфер обмена). Длительность и кадры даёт ffmpeg (<see cref="VideoProbe"/>):
/// кадр в месте ручки — с задержкой 120 мс, пока ручку тянут; полоса из 10 миниатюр — по очереди в фоне.
/// Кадры — PNG во временной папке %TEMP%\DropWheel\trim-…: каждый читается в память и сразу удаляется,
/// папка удаляется при закрытии окна. «Обрезать» отдаёт готовую операцию (<see cref="VideoTrim.Bind"/>) в очередь.
/// Вид — как у окна настроек: системный заголовок, Mica в Windows 11 22H2+, тема и акцент Windows.
/// </summary>
internal sealed class TrimWindow : Window
{
    private static readonly TimeSpan PreviewDelay = TimeSpan.FromMilliseconds(120);
    private const int PreviewWidth = 640;
    private const int ThumbWidth = 160;

    private readonly string _video;
    private readonly ToolLocator _tools;
    private readonly FileLog _log;
    private readonly Action<Operation> _enqueue;
    private readonly TrimView _view;
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "DropWheel", "trim-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly CancellationTokenSource _closing = new();
    private readonly DispatcherTimer _previewTimer;
    private CancellationTokenSource? _previewCts;
    private double _previewAt;
    private IntPtr _hwnd;

    public TrimWindow(string video, ToolLocator tools, FileLog log, Action<Operation> enqueue)
    {
        _video = video;
        _tools = tools;
        _log = log;
        _enqueue = enqueue;
        Title = Loc.T.TrimTitle;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        // По центру экрана, на котором курсор (там, где бросили файл на колесо).
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        ShowInTaskbar = true;
        TryLoadIcon();

        _view = new TrimView(Palette(false), Path.GetFileName(video));
        _view.PreviewRequested += SchedulePreview;
        _view.TrimRequested += (range, exact) =>
        {
            var op = VideoTrim.Bind(exact, range.Start, range.End);
            _log.Write($"Обрезка: {op.Id} {VideoTrim.Arg(range.Start)}…{VideoTrim.Arg(range.End)} с, {video}");
            Close();
            _enqueue(op);
        };
        _view.CancelRequested += Close;
        Content = _view;

        _previewTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = PreviewDelay };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            _ = LoadPreviewAsync(_previewAt);
        };

        PreviewKeyDown += OnKey;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyTheme();
        };
        SystemTheme.Changed += ApplyTheme;
        Loaded += (_, _) => _ = StartAsync();
    }

    /// <summary>Показать и вывести на передний план (окно открывается после броска файла в другое окно).</summary>
    public void ShowAndActivate()
    {
        Show();
        if (_hwnd != IntPtr.Zero) WheelNative.TrySetForeground(_hwnd);
        Activate();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        // Enter на кнопке («Отмена») — её собственное нажатие, а не «Обрезать».
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is not Pressable)
        {
            e.Handled = true;
            _view.RequestTrim();
        }
    }

    // ---------- ffmpeg: длительность, кадры ----------

    private async Task StartAsync()
    {
        var ct = _closing.Token;
        double? duration;
        try
        {
            duration = await Task.Run(() => VideoProbe.DurationAsync(_tools, _video, ct), ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _log.Write("Обрезка: не удалось прочитать видео: " + ex.Message);
            if (!ct.IsCancellationRequested) _view.SetFailed(ex is ConversionException ce ? ce.Message : Loc.T.TrimFailed);
            return;
        }
        if (ct.IsCancellationRequested) return;
        if (duration is not { } seconds)
        {
            _log.Write("Обрезка: ffmpeg не узнал длительность " + _video);
            _view.SetFailed(Loc.T.TrimFailed);
            return;
        }
        _log.Write($"Обрезка: {_video}, длительность {seconds:0.###} с");
        _view.SetDuration(seconds);
        _ = LoadPreviewAsync(0);
        await LoadThumbnailsAsync(seconds, ct);
    }

    private void SchedulePreview(double seconds)
    {
        _previewAt = seconds;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private async Task LoadPreviewAsync(double seconds)
    {
        _previewCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        _previewCts = cts;
        try
        {
            var image = await Task.Run(() => FrameAsync(seconds, PreviewWidth, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;
            _view.SetFrame(image, seconds, image is null ? Loc.T.TrimNoFrame : null);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Write("Обрезка: кадр не получился: " + ex.Message);
            if (!cts.IsCancellationRequested) _view.SetFrame(null, seconds, Loc.T.TrimNoFrame);
        }
    }

    private async Task LoadThumbnailsAsync(double duration, CancellationToken ct)
    {
        for (var i = 0; i < TrimView.ThumbnailCount; i++)
        {
            var at = (i + 0.5) * duration / TrimView.ThumbnailCount;
            try
            {
                var image = await Task.Run(() => FrameAsync(at, ThumbWidth, ct), ct);
                if (ct.IsCancellationRequested) return;
                _view.SetThumbnail(i, image);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.Write("Обрезка: миниатюра не получилась: " + ex.Message);
            }
        }
    }

    /// <summary>Кадр → временный PNG → картинка в памяти (замороженная, её можно отдать потоку окна); PNG удаляется.</summary>
    private async Task<ImageSource?> FrameAsync(double seconds, int width, CancellationToken ct)
    {
        Directory.CreateDirectory(_tempDir);
        var png = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".png");
        try
        {
            if (!await VideoProbe.FrameAsync(_tools, _video, seconds, png, width, ct)) return null;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(await File.ReadAllBytesAsync(png, ct));
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        finally
        {
            try { File.Delete(png); }
            catch (Exception) { /* удалим вместе с папкой */ }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemTheme.Changed -= ApplyTheme;
        _previewTimer.Stop();
        _closing.Cancel();
        _ = CleanUpAsync(_tempDir, _log);
        base.OnClosed(e);
    }

    /// <summary>Временная папка кадров: ffmpeg, убитый при закрытии, может ещё держать файл — несколько попыток.</summary>
    private static async Task CleanUpAsync(string dir, FileLog log)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 4) log.Write("Обрезка: временная папка не удалилась: " + ex.Message);
                await Task.Delay(500);
            }
        }
    }

    // ---------- вид ----------

    private static SettingsPalette Palette(bool backdrop) =>
        SystemTheme.AppsLight ? SettingsPalette.Light(SystemTheme.Accent, backdrop) : SettingsPalette.Dark(SystemTheme.Accent, backdrop);

    private void ApplyTheme()
    {
        if (_hwnd == IntPtr.Zero) return;
        var backdrop = false;
        try
        {
            NativeEffects.SetDarkMode(_hwnd, !SystemTheme.AppsLight);
            var want = SystemTheme.TransparencyEnabled && NativeEffects.SupportsSystemBackdrop && NativeEffects.EffectsAllowed;
            backdrop = want && NativeEffects.ExtendFrameIntoClientArea(_hwnd, true)
                       && NativeEffects.SetSystemBackdrop(_hwnd, NativeEffects.BackdropMica);
            if (!backdrop && NativeEffects.SupportsSystemBackdrop)
            {
                NativeEffects.SetSystemBackdrop(_hwnd, NativeEffects.BackdropNone);
                NativeEffects.ExtendFrameIntoClientArea(_hwnd, false);
            }
        }
        catch (Exception ex)
        {
            backdrop = false;
            _log.Write("Окно обрезки: подложка не включилась: " + ex.Message);
        }

        var palette = Palette(backdrop);
        _view.Palette = palette;
        Background = backdrop ? Brushes.Transparent : palette.Background;
        if (HwndSource.FromHwnd(_hwnd)?.CompositionTarget is { } target)
            target.BackgroundColor = backdrop ? Colors.Transparent : palette.Solid;
    }

    private void TryLoadIcon()
    {
        try
        {
            using var stream = typeof(TrimWindow).Assembly.GetManifestResourceStream("DropWheel.ico");
            if (stream is null) return;
            var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Icon = decoder.Frames.OrderByDescending(f => f.PixelWidth).FirstOrDefault(f => f.PixelWidth <= 64) ?? decoder.Frames[0];
        }
        catch (Exception)
        {
            // Без значка — значок WPF по умолчанию.
        }
    }
}
