using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DropWheel.Jobs;
using DropWheel.Settings;

namespace DropWheel.App;

/// <summary>Значок в трее: серый — простой, синий с цифрой — идёт работа. Клик — окно задач.</summary>
internal sealed partial class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _idle;
    private readonly ToolStripMenuItem _contextMenuItem;
    private readonly ToolStripMenuItem _updateItem;
    private Icon? _busy;
    private int _busyCount = -1;

    public TrayIcon(App app)
    {
        _idle = Draw(Color.FromArgb(90, 90, 90), null);
        _contextMenuItem = new ToolStripMenuItem("Пункт «Конвертировать…» в проводнике")
        {
            Checked = app.Settings.ContextMenuEnabled,
            CheckOnClick = true,
        };
        _contextMenuItem.CheckedChanged += (_, _) => app.SetContextMenuEnabled(_contextMenuItem.Checked);

        _updateItem = new ToolStripMenuItem { Visible = false, Font = new Font(SystemFonts.MenuFont ?? SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold) };
        _updateItem.Click += (_, _) => app.InstallUpdate();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_updateItem);
        menu.Items.Add("Задачи", null, (_, _) => app.ShowJobs());
        menu.Items.Add(_contextMenuItem);
        menu.Items.Add("Открыть журнал", null, (_, _) => OpenFolder(AppPaths.LogsDir));
        menu.Items.Add("Проверить обновления", null, (_, _) => app.CheckUpdatesManually());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => app.Quit());

        _icon = new NotifyIcon
        {
            Icon = _idle,
            Text = "DropWheel",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) app.ShowJobs();
        };
    }

    /// <summary>Показать пункт «Обновить до сборки N» и подсказку.</summary>
    public void SetUpdateAvailable(int build)
    {
        _updateItem.Text = $"Обновить до сборки {build}";
        _updateItem.Visible = true;
        ShowInfo("Есть новая версия DropWheel", "Правый клик по значку → «Обновить».");
    }

    public void ShowInfo(string title, string text) => _icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);

    public void ShowStartHint() => _icon.ShowBalloonTip(5000, "DropWheel работает",
        "Значок — здесь, в трее. Нажмите правой кнопкой на фото, видео или музыку → «Конвертировать…».",
        ToolTipIcon.Info);

    public void Update(IReadOnlyList<Job> jobs)
    {
        var active = jobs.Count(j => j.State is JobState.Queued or JobState.Running);
        if (active == _busyCount) return;
        _busyCount = active;

        var old = _busy;
        _busy = active > 0 ? Draw(Color.FromArgb(0, 120, 215), active) : null;
        _icon.Icon = _busy ?? _idle;
        _icon.Text = active > 0 ? $"DropWheel — в работе: {active}" : "DropWheel";
        DestroyIcon(old);
    }

    /// <summary>Значок рисуем сами: кольцо-«колесо», при работе — число задач.</summary>
    private static Icon Draw(Color color, int? count)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var pen = new Pen(color, 5);
            g.DrawEllipse(pen, 4, 4, 24, 24);
            using var thin = new Pen(color, 2);
            g.DrawLine(thin, 16, 4, 16, 28);
            g.DrawLine(thin, 4, 16, 28, 16);
            if (count is { } n)
            {
                using var bg = new SolidBrush(color);
                g.FillEllipse(bg, 8, 8, 16, 16);
                using var font = new Font("Segoe UI", n > 9 ? 8 : 10, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
                var text = n > 9 ? "9+" : n.ToString();
                var size = g.MeasureString(text, font);
                g.DrawString(text, font, Brushes.White, 16 - size.Width / 2, 16 - size.Height / 2);
            }
        }
        return Icon.FromHandle(bmp.GetHicon());
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
