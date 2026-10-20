using System.Windows.Threading;
using DropWheel.Conversions;
using DropWheel.Input;
using DropWheel.Logging;

namespace DropWheel.Wheel;

/// <summary>
/// Сценарий из раздела 1 ТЗ: тащим файл → горячая клавиша → колесо под курсором → бросаем на сектор.
/// Связывает хуки (Input), окно колеса и очередь задач.
/// </summary>
internal sealed class DragWheelController : IDisposable
{
    /// <summary>
    /// Хук видит отпускание кнопки раньше, чем OLE доставит Drop колесу.
    /// Ждём немного: если колесо за это время само не спряталось (Drop/Cancel) — бросили мимо, прячем.
    /// </summary>
    private static readonly TimeSpan ReleaseGrace = TimeSpan.FromMilliseconds(400);

    private readonly WheelWindow _wheel = new();
    private readonly DragDetector _detector;
    private readonly LowLevelHooks _hooks;
    private readonly DispatcherTimer _releaseTimer;
    private readonly Action<IReadOnlyList<string>, Operation> _enqueue;
    private readonly FileLog _log;

    public DragWheelController(string hotkey, Dispatcher ui, Action<IReadOnlyList<string>, Operation> enqueue, FileLog log)
    {
        _enqueue = enqueue;
        _log = log;
        var vk = Hotkeys.ToVirtualKey(hotkey) ?? Hotkeys.ToVirtualKey(Hotkeys.Default)!.Value;
        _detector = new DragDetector(vk);

        _wheel.FilesEntered += OnFilesEntered;
        _wheel.FilesDropped += OnFilesDropped;
        _wheel.Canceled += () => _detector.WheelOpen = false;
        _wheel.IsVisibleChanged += (_, _) =>
        {
            if (!_wheel.IsVisible) _detector.WheelOpen = false;
        };

        _releaseTimer = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = ReleaseGrace };
        _releaseTimer.Tick += (_, _) =>
        {
            _releaseTimer.Stop();
            if (_wheel.IsVisible && !_wheel.CloseOnDeactivate) _wheel.Cancel();
        };

        _hooks = new LowLevelHooks(_detector, ui, OnAction);
    }

    public void Start()
    {
        _hooks.Start();
        _log.Write($"Хуки установлены, горячая клавиша: VK 0x{_detector.HotkeyVk:X2}");
    }

    public string Hotkey
    {
        set
        {
            if (Hotkeys.ToVirtualKey(value) is { } vk) _detector.HotkeyVk = vk;
        }
    }

    private void OnAction(DragAction action, int x, int y)
    {
        switch (action)
        {
            case DragAction.ShowWheel:
                _releaseTimer.Stop();
                _wheel.CloseOnDeactivate = false;
                _wheel.ShowAt(x, y, activate: false);
                _detector.WheelOpen = true;
                // D10: DragEnter приходит новому окну только после движения мыши — шевельнём курсор.
                CursorNudge.NudgeWithInput();
                break;
            case DragAction.ToggleMode:
                if (_wheel.IsVisible) _wheel.ToggleMode();
                break;
            case DragAction.CloseWheel:
                _wheel.Cancel();
                break;
            case DragAction.MouseReleased:
                if (_wheel.IsVisible && !_wheel.CloseOnDeactivate) _releaseTimer.Start();
                break;
        }
    }

    private void OnFilesEntered(string[] files)
    {
        var ops = OperationCatalog.ForFiles(files);
        _wheel.SetItems(WheelItems.For(ops, OperationMode.Convert), WheelItems.For(ops, OperationMode.Tool));
    }

    private void OnFilesDropped(string[] files, string operationId)
    {
        _releaseTimer.Stop();
        if (OperationCatalog.Find(operationId) is not { } op) return;
        _log.Write($"Колесо: {op.Id} для {files.Length} файл(ов)");
        _enqueue(files, op);
    }

    public void Dispose()
    {
        _releaseTimer.Stop();
        _hooks.Dispose();
        _wheel.Close();
    }
}
