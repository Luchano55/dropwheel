using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DropWheel.Localization;
using DropWheel.Settings;

namespace DropWheel.App;

/// <summary>
/// Запись экрана в трее (решение D-RECORD): пункт «Записать экран» / «Остановить запись», во время записи —
/// значок с красной точкой и время в подсказке.
/// </summary>
internal sealed partial class TrayIcon
{
    private ToolStripMenuItem _recordItem = null!;
    private Icon? _recordingIcon;
    private string? _recordStatus;

    private ToolStripMenuItem RecordItem(App app)
    {
        _recordItem = Item(Loc.T.TrayRecord, app.RecordFromTray);
        _recordItem.ShortcutKeyDisplayString = RecordShortcut(app.Settings);
        return _recordItem;
    }

    /// <summary>Подсказка сочетания справа от «Записать экран» (если оно включено).</summary>
    private static string? RecordShortcut(AppSettings s) =>
        s.RecordHotkey ? (Capture.KeyChord.Parse(s.RecordKeys) ?? Capture.ScreenRecord.DefaultChord).DisplayName : null;

    private void SyncRecordSettings(AppSettings settings) => _recordItem.ShortcutKeyDisplayString = RecordShortcut(settings);

    private void RelocalizeRecord() => _recordItem.Text = _recordStatus is null ? Loc.T.TrayRecord : Loc.T.TrayRecordStop;

    /// <summary>
    /// Идёт запись: status — «идёт запись 00:12» (или «сохраняю запись…»), значок с красной точкой,
    /// пункт меню — «Остановить запись». null — запись закончилась.
    /// </summary>
    public void SetRecording(string? status)
    {
        var was = _recordStatus;
        _recordStatus = status;
        RelocalizeRecord();
        if (status is null)
        {
            var old = _recordingIcon;
            _recordingIcon = null;
            _icon.Icon = _busy ?? _idle;
            DestroyIcon(old);
            if (_status is null) UpdateText(Math.Max(_busyCount, 0));
            return;
        }
        if (was is null || _recordingIcon is null)
        {
            _recordingIcon = CreateRecordingIcon();
            _icon.Icon = _recordingIcon;
        }
        var text = "DropWheel — " + status;
        _icon.Text = text.Length > 127 ? text[..127] : text;
    }

    /// <summary>Значок программы с красной точкой в правом нижнем углу.</summary>
    private static Icon CreateRecordingIcon()
    {
        var size = IconSize;
        using var bmp = TrayIconArt.Render(size, null);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var d = size * 0.5f;
            var rect = new RectangleF(size - d, size - d, d - 0.5f, d - 0.5f);
            using var ring = new SolidBrush(Color.White);
            g.FillEllipse(ring, RectangleF.Inflate(rect, 0.75f, 0.75f));
            using var dot = new SolidBrush(Color.FromArgb(0xE8, 0x1E, 0x25));
            g.FillEllipse(dot, rect);
        }
        // Как TrayIconArt.CreateIcon: описатель освобождает DestroyIcon при смене значка.
        return Icon.FromHandle(bmp.GetHicon());
    }
}
