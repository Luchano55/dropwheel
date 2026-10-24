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
using DropWheel.Shell;
using DropWheel.Ui;
using DropWheel.Wheel;

namespace DropWheel.App;

/// <summary>
/// Окно «Оформить картинку» (D-BEAUTIFY) для одной картинки — по образцу окна обрезки (<see cref="CropWindow"/>).
/// При открытии ImageMagick делает повёрнутую по EXIF копию ≤ 1000 px (<see cref="ImageProbe"/>) — она лежит во временной
/// папке %TEMP%\DropWheel\beautify-… до закрытия окна. Каждое изменение (с задержкой 120 мс, прежний запуск убивается)
/// рисует предпросмотр тем же шаблоном, что и задача, но на копии; PNG читается в память и сразу удаляется.
/// «Сохранить» отдаёт готовую операцию (<see cref="ImageBeautify.Bind"/>) в очередь; «Копировать» рисует полный
/// размер во временный PNG и кладёт картинку в буфер обмена, ничего не сохраняя рядом с исходником.
/// </summary>
internal sealed class BeautifyWindow : Window
{
    private static readonly TimeSpan PreviewDelay = TimeSpan.FromMilliseconds(120);

    /// <summary>Последнее оформление за этот запуск программы — следующее окно открывается с ним.</summary>
    private static BeautifyStyle _lastStyle = BeautifyStyle.Default;

    private readonly string _image;
    private readonly ToolLocator _tools;
    private readonly FileLog _log;
    private readonly BeautifyView _view;
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "DropWheel", "beautify-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly string _copyPng;
    private readonly CancellationTokenSource _closing = new();
    private readonly DispatcherTimer _previewTimer;
    private CancellationTokenSource? _previewCts;
    private (int W, int H)? _full, _copySize;
    private bool _copying;
    private IntPtr _hwnd;

    public BeautifyWindow(string image, ToolLocator tools, FileLog log, Action<Operation> enqueue)
    {
        _image = image;
        _tools = tools;
        _log = log;
        _copyPng = Path.Combine(_tempDir, "copy.png");
        Title = Loc.T.BeautifyTitle;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        ShowInTaskbar = true;
        TryLoadIcon();

        _view = new BeautifyView(Palette(false), Path.GetFileName(image), _lastStyle);
        _view.StyleChanged += style =>
        {
            _lastStyle = style;
            _previewTimer!.Stop();
            _previewTimer.Start();
        };
        _view.SaveRequested += style =>
        {
            if (_full is not { } f) return;
            _lastStyle = style;
            var op = ImageBeautify.Bind(style, f.W, f.H);
            _log.Write($"Оформление: {style.Format()} для {f.W}×{f.H}, {image}");
            Close();
            enqueue(op);
        };
        _view.CopyRequested += style => _ = CopyAsync(style);
        _view.CancelRequested += Close;
        Content = _view;

        _previewTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = PreviewDelay };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            _ = RenderPreviewAsync(_view.CurrentStyle);
        };

        PreviewKeyDown += OnKey;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyTheme();
        };
        SystemTheme.Changed += ApplyTheme;
        Loaded += (_, _) => _ = LoadAsync();
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
        // Enter на кнопке («Отмена», пилюля фона) — её собственное нажатие, а не «Сохранить».
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is not Pressable)
        {
            e.Handled = true;
            _view.RequestSave();
        }
    }

    /// <summary>ImageMagick: размер после поворота и копия ≤ 1000 px (остаётся до закрытия окна); затем первый предпросмотр.</summary>
    private async Task LoadAsync()
    {
        var ct = _closing.Token;
        try
        {
            Directory.CreateDirectory(_tempDir);
            var size = await Task.Run(() => ImageProbe.PreviewAsync(_tools, _image, _copyPng, ct, ImageBeautify.PreviewSide), ct);
            if (ct.IsCancellationRequested) return;
            var copy = size is null ? null : ImageProbe.PngSize(await ReadHeadAsync(_copyPng, ct));
            if (size is not { } s || copy is null)
            {
                _log.Write("Оформление: ImageMagick не открыл " + _image);
                _view.SetFailed(Loc.T.CropFailed);
                return;
            }
            _full = s;
            _copySize = copy;
            _log.Write($"Оформление: {_image}, {s.W}×{s.H}");
            _view.SetImage(s.W, s.H);
            await RenderPreviewAsync(_view.CurrentStyle);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Write("Оформление: не удалось открыть: " + ex.Message);
            if (!ct.IsCancellationRequested) _view.SetFailed(ex is ConversionException ce ? ce.Message : Loc.T.CropFailed);
        }
    }

    private static async Task<byte[]> ReadHeadAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var head = new byte[32];
        var read = await stream.ReadAsync(head, ct);
        return head[..read];
    }

    /// <summary>Предпросмотр: тот же шаблон на копии, радиус — в её масштабе. Новый запуск отменяет прежний.</summary>
    private async Task RenderPreviewAsync(BeautifyStyle style)
    {
        if (_full is not { } full || _copySize is not { } copy) return;
        _previewCts?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        _previewCts = cts;
        try
        {
            var scaled = ImageBeautify.ForPreview(style, full.W, full.H, copy.W, copy.H);
            var image = await Task.Run(() => RenderAsync(_copyPng, scaled, copy.W, copy.H, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;
            _view.SetPreview(image is null ? null : Decode(image));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Write("Оформление: предпросмотр не получился: " + ex.Message);
            if (!cts.IsCancellationRequested) _view.SetPreview(null);
        }
    }

    /// <summary>«Копировать»: полный размер во временный PNG → картинка в буфер обмена (без файла рядом с исходником).</summary>
    private async Task CopyAsync(BeautifyStyle style)
    {
        if (_full is not { } full || _copying) return;
        _copying = true;
        _view.SetStatus(Loc.T.BeautifyRendering);
        var ct = _closing.Token;
        try
        {
            var png = await Task.Run(() => RenderAsync(_image, style, full.W, full.H, ct), ct);
            if (ct.IsCancellationRequested) return;
            if (png is null) throw new InvalidOperationException("ImageMagick не нарисовал картинку");
            ClipboardWriter.CopyImage(png, _log);
            _log.Write($"Оформление: скопировано в буфер обмена {style.Format()}, {_image}");
            _view.SetStatus(Loc.T.CopiedToClipboard);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Write("Оформление: не удалось скопировать: " + ex.Message);
            if (!ct.IsCancellationRequested) _view.SetStatus(Loc.T.BeautifyCopyFailed);
        }
        finally
        {
            _copying = false;
        }
    }

    /// <summary>Нарисовать оформление картинки input (w × h — её размер после поворота) → байты PNG; null — не вышло.</summary>
    private async Task<byte[]?> RenderAsync(string input, BeautifyStyle style, int w, int h, CancellationToken ct)
    {
        var output = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var args = ArgumentBuilder.Build(ImageBeautify.Bind(style, w, h), input, output, ConversionSettings.Default);
            if (!await ImageProbe.RenderAsync(_tools, args, output, ct)) return null;
            return await File.ReadAllBytesAsync(output, ct);
        }
        finally
        {
            try { File.Delete(output); }
            catch (Exception) { /* удалим вместе с папкой */ }
        }
    }

    private static ImageSource Decode(byte[] png)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(png);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemTheme.Changed -= ApplyTheme;
        _previewTimer.Stop();
        _closing.Cancel();
        _ = CleanUpAsync(_tempDir, _log);
        base.OnClosed(e);
    }

    /// <summary>Временная папка: ImageMagick, убитый при закрытии, может ещё держать файл — несколько попыток.</summary>
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
                if (attempt == 4) log.Write("Оформление: временная папка не удалилась: " + ex.Message);
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
            _log.Write("Окно оформления: подложка не включилась: " + ex.Message);
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
            using var stream = typeof(BeautifyWindow).Assembly.GetManifestResourceStream("DropWheel.ico");
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
