using System.Windows;
using System.Windows.Controls;
using DropWheel.App;
using DropWheel.Localization;
using DropWheel.Ui;

namespace DropWheel.Capture;

/// <summary>
/// Оверлей в режиме записи экрана (решение D-RECORD): то же выделение, что у снимка, а на панели —
/// «MP4 / GIF» и «Записать» вместо «Копировать / Сохранить / Колесо / Текст».
/// </summary>
internal sealed partial class CaptureView
{
    private RecordFormat? _record;

    /// <summary>Режим записи: формат на панели; null — обычный снимок.</summary>
    public RecordFormat? RecordMode => _record;

    /// <summary>Нажата «Записать» (формат — выбранный на панели).</summary>
    public event Action<RecordFormat>? RecordRequested;

    /// <summary>Нажать «Записать» с клавиатуры (Enter).</summary>
    public void RequestRecord()
    {
        if (_record is { } format) RecordRequested?.Invoke(format);
    }

    private StackPanel RecordToolbar(SettingsPalette p)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var formats = new ChoicePills(p, [(RecordFormat.Mp4, "MP4"), (RecordFormat.Gif, "GIF")], _record, "MP4 / GIF")
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 6, 0),
        };
        formats.SelectionChanged += value => _record = (RecordFormat)value;
        row.Children.Add(formats);
        var start = new TrimView.PrimaryButton(p, "●  " + Loc.T.RecordStart) { Margin = new Thickness(0, 0, 2, 0) };
        start.Click += RequestRecord;
        row.Children.Add(start);
        row.Children.Add(new Border { Width = 1, Margin = new Thickness(4, 6, 6, 6), Background = p.Divider });
        var close = new SettingsButton(p, null, Glyphs.Cancel, Loc.T.CaptureClose, subtle: true);
        close.Click += () => CloseRequested?.Invoke();
        row.Children.Add(close);
        return row;
    }
}
