using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Localization;
using DropWheel.Logging;
using DropWheel.Ui;
using DropWheel.Wheel;

namespace DropWheel.App;

/// <summary>
/// Окно «Обрезать картинку» (D-CROP) для одной картинки: открывается после выбора «Обрезать…» в колесе
/// (перетаскивание, меню проводника, буфер обмена). Показ — уменьшенная копия от ImageMagick (<see cref="ImageProbe"/>):
/// уже повёрнутая по EXIF, как при обрезке, и для любых форматов (HEIC, AVIF, PSD, RAW). Копия — PNG во временной
/// папке %TEMP%\DropWheel\crop-…: читается в память и сразу удаляется, папка удаляется при закрытии окна.
/// «Обрезать» отдаёт готовую операцию (<see cref="ImageCrop.Bind(CropRect,int,int)"/>) в очередь.
/// Вид — как у окна обрезки видео: системный заголовок, Mica в Windows 11 22H2+, тема и акцент Windows.
/// </summary>
internal sealed class CropWindow : Window
{
    private readonly string _image;
    private readonly ToolLocator _tools;
    private readonly FileLog _log;
    private readonly CropView _view;
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "DropWheel", "crop-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly CancellationTokenSource _closing = new();
    private IntPtr _hwnd;

    public CropWindow(string image, ToolLocator tools, FileLog log, Action<Operation> enqueue)
    {
        _image = image;
        _tools = tools;
        _log = log;
        Title = Loc.T.CropTitle;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        ShowInTaskbar = true;
        TryLoadIcon();

        _view = new CropView(Palette(false), Path.GetFileName(image));
        _view.CropRequested += (rect, w, h) =>
        {
            var op = ImageCrop.Bind(rect, w, h);
            _log.Write($"Обрезка картинки: {rect.Geometry} из {w}×{h}, {image}");
            Close();
            enqueue(op);
        };
        _view.CancelRequested += Close;
        Content = _view;

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
        // Enter на кнопке («Отмена», пилюля) — её собственное нажатие, а не «Обрезать».
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is not Pressable)
        {
            e.Handled = true;
            _view.RequestCrop();
        }
    }

    /// <summary>ImageMagick: размер после поворота и копия ≤ 1600 px → картинка в памяти; PNG удаляется.</summary>
    private async Task LoadAsync()
    {
        var ct = _closing.Token;
        var png = Path.Combine(_tempDir, "preview.png");
        try
        {
            Directory.CreateDirectory(_tempDir);
            var size = await Task.Run(() => ImageProbe.PreviewAsync(_tools, _image, png, ct), ct);
            if (ct.IsCancellationRequested) return;
            if (size is not { } s)
            {
                _log.Write("Обрезка картинки: ImageMagick не открыл " + _image);
                _view.SetFailed(Loc.T.CropFailed);
                return;
            }
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(await File.ReadAllBytesAsync(png, ct));
            bitmap.EndInit();
            bitmap.Freeze();
            _log.Write($"Обрезка картинки: {_image}, {s.W}×{s.H}");
            _view.SetImage(bitmap, s.W, s.H);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Write("Обрезка картинки: не удалось открыть: " + ex.Message);
            if (!ct.IsCancellationRequested) _view.SetFailed(ex is ConversionException ce ? ce.Message : Loc.T.CropFailed);
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
                if (attempt == 4) log.Write("Обрезка картинки: временная папка не удалилась: " + ex.Message);
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
            _log.Write("Окно обрезки картинки: подложка не включилась: " + ex.Message);
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
            using var stream = typeof(CropWindow).Assembly.GetManifestResourceStream("DropWheel.ico");
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
