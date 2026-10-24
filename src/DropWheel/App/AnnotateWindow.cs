using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DropWheel.Annotate;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Localization;
using DropWheel.Logging;
using DropWheel.Shell;
using DropWheel.Ui;
using DropWheel.Wheel;

namespace DropWheel.App;

/// <summary>
/// Окно «Пометки» (D-ANNOTATE) — по образцу окон «Оформить…» и «Обрезать…»: Mica, тема и акцент Windows.
/// Картинка — снимок экрана (уже в памяти) или файл: его ImageMagick открывает в полном размере, повёрнутым по EXIF
/// (временный PNG в %TEMP%\DropWheel\annotate-… читается в память и сразу удаляется). Результат рисует WPF в полном
/// размере (<see cref="AnnotationRenderer.Render"/>); «Сохранить» отдаёт его приложению (файл рядом с исходником
/// или в папку снимков), «Копировать» — в буфер обмена. Закрыть с несохранёнными пометками — только после вопроса.
/// После закрытия окно не держит ни картинку, ни пометки.
/// </summary>
internal sealed class AnnotateWindow : Window
{
    private readonly AnnotateView _view;
    private readonly FileLog _log;
    private readonly Func<BitmapSource, string?> _save;
    private readonly CancellationTokenSource _closing = new();
    private readonly string? _file;
    private readonly ToolLocator? _tools;
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "DropWheel", "annotate-" + Guid.NewGuid().ToString("N")[..12]);
    private bool _discard;
    private IntPtr _hwnd;

    /// <summary>Картинка-файл (лепесток «Пометить…»).</summary>
    /// <param name="save">Сохранить результат; вернуть путь или null (не вышло).</param>
    public AnnotateWindow(string file, ToolLocator tools, string saveHint, Func<BitmapSource, string?> save, FileLog log)
        : this(Path.GetFileName(file), saveHint, save, log, null)
    {
        _file = file;
        _tools = tools;
        Loaded += (_, _) => _ = LoadAsync();
    }

    /// <summary>Снимок экрана (кнопка «Пометки» на панели снимка): картинка уже в памяти.</summary>
    public AnnotateWindow(BitmapSource image, string name, string saveHint, Func<BitmapSource, string?> save, FileLog log)
        : this(name, saveHint, save, log, image)
    {
    }

    private AnnotateWindow(string name, string saveHint, Func<BitmapSource, string?> save, FileLog log, BitmapSource? image)
    {
        _log = log;
        _save = save;
        Title = $"{Loc.T.AnnotateTitle} — {name}";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        ShowInTaskbar = true;
        TryLoadIcon();

        _view = new AnnotateView(Palette(false), saveHint);
        if (image is not null)
        {
            (_view.Width, _view.Height) = InitialSize(image.PixelWidth, image.PixelHeight, 1);
            _view.SetImage(image);
        }
        _view.SaveRequested += Save;
        _view.CopyRequested += Copy;
        _view.CancelRequested += Close;
        _view.DiscardConfirmed += () =>
        {
            _discard = true;
            Close();
        };
        Content = _view;

        PreviewKeyDown += (_, e) =>
        {
            if (_view.HandleKey(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers)) e.Handled = true;
        };
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            // Масштаб монитора известен только теперь: снимок 1000 px при 150 % — это 667 DIP.
            if (image is not null)
                (_view.Width, _view.Height) = InitialSize(image.PixelWidth, image.PixelHeight, VisualTreeHelper.GetDpi(this).DpiScaleX);
            ApplyTheme();
        };
        // Открылось в размер картинки — дальше окно можно тянуть, картинка вписывается заново.
        ContentRendered += (_, _) =>
        {
            SizeToContent = SizeToContent.Manual;
            _view.Width = _view.Height = double.NaN;
            MinWidth = 640;
            MinHeight = 420;
        };
        SystemTheme.Changed += ApplyTheme;
    }

    /// <summary>Размер вида: картинка «пиксель в пиксель» (в DIP основного монитора), но в пределах 800…1200 × 520…800.</summary>
    private static (double W, double H) InitialSize(int imageW, int imageH, double dpiScale)
    {
        var scale = Math.Max(0.5, dpiScale);
        var w = Math.Clamp(imageW / scale + 32, 800, Math.Min(1200, SystemParameters.WorkArea.Width * 0.9));
        var h = Math.Clamp(imageH / scale + 32 + 128, 520, Math.Min(800, SystemParameters.WorkArea.Height * 0.9));
        return (Math.Round(w), Math.Round(h));
    }

    /// <summary>Показать и вывести на передний план (окно открывается после броска на колесо или из оверлея снимка).</summary>
    public void ShowAndActivate()
    {
        Show();
        if (_hwnd != IntPtr.Zero) WheelNative.TrySetForeground(_hwnd);
        Activate();
    }

    /// <summary>ImageMagick: полная картинка, повёрнутая по EXIF, во временный PNG → в память (PNG сразу удаляется).</summary>
    private async Task LoadAsync()
    {
        if (_file is null || _tools is null) return;
        var ct = _closing.Token;
        var png = Path.Combine(_tempDir, "full.png");
        try
        {
            Directory.CreateDirectory(_tempDir);
            var size = await Task.Run(() => ImageProbe.FullAsync(_tools, _file, png, ct), ct);
            if (ct.IsCancellationRequested) return;
            if (size is not { } s)
            {
                _log.Write("Пометки: ImageMagick не открыл " + _file);
                _view.SetFailed(Loc.T.CropFailed);
                return;
            }
            if ((long)s.W * s.H > ImageAnnotate.MaxPixels)
            {
                _log.Write($"Пометки: картинка {s.W}×{s.H} слишком большая: {_file}");
                _view.SetFailed(Loc.F(Loc.T.AnnotateTooLarge, ImageAnnotate.MaxPixels / 1_000_000));
                return;
            }
            var image = await Task.Run(() => Decode(File.ReadAllBytes(png)), ct);
            if (ct.IsCancellationRequested) return;
            _log.Write($"Пометки: {_file}, {image.PixelWidth}×{image.PixelHeight}");
            _view.SetImage(image);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Write("Пометки: не удалось открыть: " + ex.Message);
            if (!ct.IsCancellationRequested) _view.SetFailed(ex is ConversionException ce ? ce.Message : Loc.T.CropFailed);
        }
        finally
        {
            DeleteTemp();
        }
    }

    private static BitmapSource Decode(byte[] png)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bitmap.StreamSource = new MemoryStream(png);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void Save()
    {
        if (_view.Renderer is not { } renderer) return;
        try
        {
            var image = renderer.Render(_view.Document.Shapes);
            if (_save(image) is not { } path)
            {
                _view.SetStatus(Loc.T.AnnotateSaveFailed);
                return;
            }
            _log.Write($"Пометки: сохранено {_view.Document.Shapes.Count} пометок → {path}");
            _view.Document.MarkSaved();
            _discard = true;
            Close();
        }
        catch (Exception ex)
        {
            _log.Write("Пометки: не удалось сохранить: " + ex);
            _view.SetStatus(Loc.T.AnnotateSaveFailed);
        }
    }

    private void Copy()
    {
        if (_view.Renderer is not { } renderer) return;
        try
        {
            ClipboardWriter.CopyImage(EncodePng(renderer.Render(_view.Document.Shapes)), _log);
            _log.Write($"Пометки: скопировано в буфер обмена ({_view.Document.Shapes.Count} пометок)");
            _view.Document.MarkSaved();
            _view.SetStatus(Loc.T.CopiedToClipboard);
        }
        catch (Exception ex)
        {
            _log.Write("Пометки: не удалось скопировать: " + ex.Message);
            _view.SetStatus(Loc.T.AnnotateCopyFailed);
        }
    }

    internal static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Крестик, Esc, «Отмена»: есть несохранённые пометки — сначала вопрос «Закрыть без сохранения?».</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_discard && _view.Document.HasUnsavedChanges)
        {
            e.Cancel = true;
            _view.ShowConfirm();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemTheme.Changed -= ApplyTheme;
        _closing.Cancel();
        _view.Release();
        Content = null;
        DeleteTemp();
        base.OnClosed(e);
    }

    private void DeleteTemp()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write("Пометки: временная папка не удалилась: " + ex.Message);
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
            _log.Write("Окно пометок: подложка не включилась: " + ex.Message);
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
            using var stream = typeof(AnnotateWindow).Assembly.GetManifestResourceStream("DropWheel.ico");
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
