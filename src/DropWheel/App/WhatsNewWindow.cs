using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DropWheel.Localization;
using DropWheel.Logging;
using DropWheel.Ui;
using DropWheel.Updates;

namespace DropWheel.App;

/// <summary>
/// Окно «Что нового в DropWheel X.Y.Z» (D-WHATSNEW): после обновления и из меню трея. Текст — из docs/CHANGELOG.md,
/// вшитого в exe (<see cref="ChangelogNotes"/>). По центру основного экрана; при показе после запуска фокус не забирает.
/// Вид — как у окон обрезки: системный заголовок, Mica в Windows 11 22H2+, тема и акцент Windows.
/// </summary>
internal sealed class WhatsNewWindow : Window
{
    public const string ReleasesUrl = "https://github.com/Luchano55/dropwheel/releases";

    private readonly FileLog _log;
    private readonly WhatsNewView _view;
    private IntPtr _hwnd;

    public WhatsNewWindow(string version, IReadOnlyList<ChangelogNote> notes, FileLog log, bool activate)
    {
        _log = log;
        Title = WhatsNewView.TitleFor(version);
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowActivated = activate;
        ShowInTaskbar = true;
        UseLayoutRounding = true;
        TryLoadIcon();

        _view = new WhatsNewView(Palette(false), version, notes);
        _view.CloseRequested += Close;
        _view.AllChangesRequested += OpenReleases;
        Content = _view;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape || e.Key is Key.Enter && Keyboard.FocusedElement is not System.Windows.Controls.TextBlock)
            {
                e.Handled = true;
                Close();
            }
        };
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyTheme();
        };
        // По центру основного экрана (рабочая область без панели задач) — как только известен размер.
        SizeChanged += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + Math.Max(0, (area.Width - ActualWidth) / 2);
            Top = area.Top + Math.Max(0, (area.Height - ActualHeight) / 2);
        };
        SystemTheme.Changed += ApplyTheme;
    }

    /// <summary>docs/CHANGELOG.md из ресурсов exe; нет — пустая строка.</summary>
    public static string ReadChangelog()
    {
        using var stream = typeof(WhatsNewWindow).Assembly.GetManifestResourceStream("CHANGELOG.md");
        if (stream is null) return "";
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private void OpenReleases()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Write("Что нового: не открылась страница выпусков: " + ex.Message);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemTheme.Changed -= ApplyTheme;
        base.OnClosed(e);
    }

    // ---------- вид (как у CropWindow) ----------

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
            _log.Write("Окно «Что нового»: подложка не включилась: " + ex.Message);
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
            using var stream = typeof(WhatsNewWindow).Assembly.GetManifestResourceStream("DropWheel.ico");
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
