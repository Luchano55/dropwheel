using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using DropWheel.Jobs;
using DropWheel.Settings;
using DropWheel.Ui;

namespace DropWheel.App;

/// <summary>
/// Значок в трее: значок программы; пока идёт работа — с оранжевым кружком-счётчиком задач.
/// Клик — окно задач (ещё клик — спрятать). Меню — в цветах темы Windows.
/// </summary>
internal sealed partial class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _contextMenuItem;
    private readonly Icon _idle;
    private Icon? _busy;
    private int _busyCount = -1;

    public TrayIcon(App app)
    {
        _idle = TrayIconArt.CreateIcon(IconSize, null);
        _contextMenuItem = new ToolStripMenuItem("Пункт «Конвертировать…» в проводнике")
        {
            Checked = app.Settings.ContextMenuEnabled,
            CheckOnClick = true,
        };
        _contextMenuItem.CheckedChanged += (_, _) => app.SetContextMenuEnabled(_contextMenuItem.Checked);

        _menu = new ContextMenuStrip
        {
            Font = new Font("Segoe UI", 9f),
            ShowImageMargin = true,
            Padding = new Padding(2, 4, 2, 4),
        };
        _menu.Items.Add(Item("Задачи", app.ShowJobs));
        _menu.Items.Add(_contextMenuItem);
        _menu.Items.Add(Item("Открыть журнал", () => OpenFolder(AppPaths.LogsDir)));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item("Выход", app.Quit));
        foreach (ToolStripItem item in _menu.Items)
            if (item is ToolStripMenuItem) item.Padding = new Padding(4, 5, 8, 5);
        // Тема могла поменяться — перекрашиваем при каждом открытии.
        _menu.Opening += (_, _) => ApplyMenuTheme();
        _menu.Opened += (_, _) => NativeEffects.SetCorners(_menu.Handle, NativeEffects.CornerRoundSmall);
        ApplyMenuTheme();

        _icon = new NotifyIcon
        {
            Icon = _idle,
            Text = "DropWheel",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) app.ToggleJobs();
        };
    }

    /// <summary>Размер маленького значка Windows (16 при 100 %, 20 при 125 %, 24 при 150 %).</summary>
    private static int IconSize => Math.Clamp(SystemInformation.SmallIconSize.Width, 16, 64);

    public void ShowStartHint() => _icon.ShowBalloonTip(5000, "DropWheel работает",
        "Значок — здесь, в трее. Потащите файл и нажмите Пробел — или правой кнопкой по файлу → «Конвертировать…».",
        ToolTipIcon.Info);

    public void Update(IReadOnlyList<Job> jobs)
    {
        var active = jobs.Count(j => j.State is JobState.Queued or JobState.Running);
        if (active == _busyCount) return;
        _busyCount = active;

        var old = _busy;
        _busy = active > 0 ? TrayIconArt.CreateIcon(IconSize, active) : null;
        _icon.Icon = _busy ?? _idle;
        _icon.Text = active > 0 ? $"DropWheel — в работе: {active}" : "DropWheel";
        DestroyIcon(old);
    }

    private void ApplyMenuTheme()
    {
        var dark = !SystemTheme.ShellLight;
        _menu.Renderer = new TrayMenuRenderer(dark, drawBorder: !NativeEffects.IsWindows11);
        _menu.BackColor = dark ? Color.FromArgb(0x2C, 0x2C, 0x2C) : Color.FromArgb(0xF9, 0xF9, 0xF9);
        _menu.ForeColor = dark ? Color.White : Color.FromArgb(0x1A, 0x1A, 0x1A);
    }

    private static ToolStripMenuItem Item(string text, Action click)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => click();
        return item;
    }

    private static void DestroyIcon(Icon? icon)
    {
        if (icon is null) return;
        NativeIcon.DestroyIcon(icon.Handle);
        icon.Dispose();
    }

    private static void OpenFolder(string dir)
    {
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        DestroyIcon(_busy);
        DestroyIcon(_idle);
    }

    private static partial class NativeIcon
    {
        [System.Runtime.InteropServices.LibraryImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static partial bool DestroyIcon(IntPtr handle);
    }
}
