using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DropWheel.Logging;
using DropWheel.Settings;
using DropWheel.Ui;
using DropWheel.Wheel;

namespace DropWheel.App;

/// <summary>
/// Окно «Настройки DropWheel» (этап 4): обычное окно Windows с системным заголовком, в Windows 11 22H2+ —
/// с подложкой Mica (иначе сплошной фон темы), тёмный заголовок в тёмной теме. Тема и акцент — как у приложений
/// Windows и меняются на лету. Содержимое — <see cref="SettingsView"/>. Один экземпляр (держит App).
/// </summary>
internal sealed class SettingsWindow : Window
{
    private readonly SettingsView _view;
    private readonly FileLog _log;
    private IntPtr _hwnd;
    private bool _backdrop;

    public SettingsWindow(AppSettings settings, Func<Func<AppSettings, AppSettings>, AppSettings> update, int build, FileLog log)
    {
        _log = log;
        Title = Strings.Settings.WindowTitle;
        Width = SettingsView.DefaultWidth;
        Height = SettingsView.DefaultHeight;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        TryLoadIcon();

        _view = new SettingsView(Palette(false), settings, SystemTheme.Accent, build) { Update = update };
        _view.PickFolder = PickFolder;
        Content = _view;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyTheme();
        };
        SystemTheme.Changed += ApplyTheme;
    }

    public SettingsView View => _view;

    /// <summary>Сменился язык интерфейса — заголовок и всё содержимое на новом языке.</summary>
    public void Relocalize()
    {
        Title = Strings.Settings.WindowTitle;
        _view.Relocalize();
    }

    /// <summary>Настройки поменялись не из окна (трей, «последний выбор») — показать новые.</summary>
    public void Reload(AppSettings settings) => _view.Settings = settings;

    /// <summary>Показать и вывести на передний план (повторный вызов — просто активировать).</summary>
    public void ShowAndActivate()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (_hwnd != IntPtr.Zero) WheelNative.TrySetForeground(_hwnd);
        Activate();
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
        try
        {
            NativeEffects.SetDarkMode(_hwnd, !SystemTheme.AppsLight);
            var want = SystemTheme.TransparencyEnabled && NativeEffects.SupportsSystemBackdrop && NativeEffects.EffectsAllowed;
            _backdrop = want && NativeEffects.ExtendFrameIntoClientArea(_hwnd, true)
                        && NativeEffects.SetSystemBackdrop(_hwnd, NativeEffects.BackdropMica);
            if (!_backdrop && NativeEffects.SupportsSystemBackdrop)
            {
                NativeEffects.SetSystemBackdrop(_hwnd, NativeEffects.BackdropNone);
                NativeEffects.ExtendFrameIntoClientArea(_hwnd, false);
            }
        }
        catch (Exception ex)
        {
            _backdrop = false;
            _log.Write("Окно настроек: подложка не включилась: " + ex.Message);
        }

        var palette = Palette(_backdrop);
        _view.Accent = SystemTheme.Accent;
        _view.Palette = palette;
        if (_backdrop)
        {
            Background = Brushes.Transparent;
            if (HwndSource.FromHwnd(_hwnd)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
        }
        else
        {
            Background = palette.Background;
            if (HwndSource.FromHwnd(_hwnd)?.CompositionTarget is { } target) target.BackgroundColor = palette.Solid;
        }
    }

    private string? PickFolder(string current, string title)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = title,
                InitialDirectory = Directory.Exists(current) ? current : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Multiselect = false,
            };
            return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
        }
        catch (Exception ex)
        {
            _log.Write("Выбор папки не открылся: " + ex.Message);
            return null;
        }
    }

    private void TryLoadIcon()
    {
        try
        {
            using var stream = typeof(SettingsWindow).Assembly.GetManifestResourceStream("DropWheel.ico");
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
