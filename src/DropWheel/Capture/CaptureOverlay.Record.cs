using System.Windows.Input;

namespace DropWheel.Capture;

/// <summary>
/// Оверлей в режиме записи экрана (решение D-RECORD): выделение то же самое (область, щелчок — окно, Enter — монитор),
/// но вместо действий со снимком — «Записать». Оверлей закрывается и отдаёт область (физические пиксели виртуального
/// экрана) и мониторы — по ним ставятся рамка и пилюля «Стоп».
/// </summary>
internal sealed partial class CaptureOverlay
{
    private readonly bool _recordMode;

    /// <summary>Нажата «Записать»: область, мониторы (границы и DPI) и формат.</summary>
    public event Action<PixelRect, IReadOnlyList<CaptureMonitor>, RecordFormat>? RecordRequested;

    /// <summary>Клавиши режима записи. true — обработано (Esc и прочее — как у снимка).</summary>
    private bool OnRecordKey(KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        switch (e.Key)
        {
            case Key.Enter when _session.Selection is not null:
                e.Handled = true;
                ToolbarView()?.RequestRecord();
                return true;
            case Key.C or Key.S when ctrl:
                // «Копировать» / «Сохранить» снимка в режиме записи не действуют.
                e.Handled = true;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Окно монитора с панелью кнопок (правый нижний угол выделения) — его выбор MP4 / GIF.</summary>
    private CaptureView? ToolbarView()
    {
        if (_session.Selection is not { } s) return null;
        return MonitorWindowAt(s.Right - 1, s.Bottom - 1).Window.View;
    }

    private void FinishRecord(RecordFormat format)
    {
        if (_closed || _session.Selection is not { } selection) return;
        var monitors = _frame.Monitors;
        _log.Write($"Запись экрана: область {selection}, {format}");
        Close();
        RecordRequested?.Invoke(selection, monitors, format);
    }
}
