using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DropWheel.Conversions;
using DropWheel.Logging;
using DropWheel.Ui;
using DropWheel.Wheel;

namespace DropWheel.App;

/// <summary>
/// Окно пароля для одного PDF (D-PDFPASS): «Защитить…» — пароль и повтор, «Снять пароль…» — одно поле.
/// Открывается после выбора в колесе (тем же путём, что окно обрезки: App.Enqueue → AskValues).
/// Кнопка отдаёт готовую операцию (<see cref="PdfPassword.Bind"/>) в очередь; пароль не пишется в журнал
/// и не запоминается — после закрытия окна он есть только у задачи в памяти, пока она идёт.
/// Вид — как у окна обрезки: системный заголовок, Mica в Windows 11 22H2+, тема и акцент Windows.
/// </summary>
internal sealed class PasswordWindow : Window
{
    private readonly FileLog _log;
    private readonly PasswordView _view;
    private IntPtr _hwnd;

    public PasswordWindow(string pdf, bool protect, FileLog log, Action<Operation> enqueue)
    {
        _log = log;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        ShowInTaskbar = true;
        TryLoadIcon();

        _view = new PasswordView(Palette(false), Path.GetFileName(pdf), protect);
        Title = _view.Title;
        _view.Confirmed += password =>
        {
            var op = PdfPassword.Bind(protect, password);
            _log.Write($"Пароль PDF: {op.Id}, {pdf}"); // без пароля
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
        Loaded += (_, _) => _view.FocusPassword();
    }

    /// <summary>Показать и вывести на передний план (окно открывается после броска файла в другое окно).</summary>
    public void ShowAndActivate()
    {
        Show();
        if (_hwnd != IntPtr.Zero) WheelNative.TrySetForeground(_hwnd);
        Activate();
        _view.FocusPassword();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _view.RequestCancel();
        }
        // Enter на кнопке («Отмена», флажок) — её собственное нажатие.
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is not Pressable)
        {
            e.Handled = true;
            _view.RequestConfirm();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemTheme.Changed -= ApplyTheme;
        base.OnClosed(e);
    }

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
            _log.Write("Окно пароля: подложка не включилась: " + ex.Message);
        }

        // Перестройка вида сохраняет введённое (поля пересоздаются с тем же текстом).
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
            using var stream = typeof(PasswordWindow).Assembly.GetManifestResourceStream("DropWheel.ico");
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
