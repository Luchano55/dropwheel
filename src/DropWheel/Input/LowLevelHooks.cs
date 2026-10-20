using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using static DropWheel.Input.NativeMethods;

namespace DropWheel.Input;

/// <summary>
/// Глобальные low-level хуки мыши и клавиатуры (WH_MOUSE_LL, WH_KEYBOARD_LL), питающие <see cref="DragDetector"/>.
/// <para>
/// Хуки ставятся в отдельном фоновом потоке со своим циклом GetMessage: Windows вызывает обработчик
/// именно в этом потоке, поэтому подвисание UI не упирается в LowLevelHooksTimeout (иначе Windows
/// молча снимает хук). Обработчик только обновляет детектор и, если есть действие, отправляет его
/// в поток UI через Dispatcher.BeginInvoke — всё тяжёлое (показ окна и т.д.) происходит там.
/// </para>
/// <para>
/// Программно сгенерированные нажатия клавиш (LLKHF_INJECTED: SendInput, экранная клавиатура,
/// AutoHotkey) пропускаются мимо детектора — не реагируем на собственный и чужой синтетический ввод.
/// Синтетические события мыши, наоборот, учитываются: сдвиг курсора <see cref="CursorNudge"/> и
/// ввод с сенсорного экрана/пера тоже двигают перетаскивание. Мышь никогда не глотается.
/// </para>
/// <para>
/// Координаты из MSLLHOOKSTRUCT.pt — физические пиксели (приложение Per-Monitor V2), передаются как есть.
/// </para>
/// </summary>
public sealed class LowLevelHooks : IDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly DragDetector _detector;
    private readonly Dispatcher _ui;
    private readonly Action<DragAction, int, int> _onAction;

    // Делегаты держим в полях: иначе GC соберёт их, пока Windows продолжает вызывать указатель.
    private readonly HookProc _mouseProc;
    private readonly HookProc _keyboardProc;

    private readonly object _gate = new();
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private bool _disposed;

    /// <param name="detector">Детектор перетаскивания (вызывается из потока хуков).</param>
    /// <param name="ui">Диспетчер потока UI, куда доставляются действия.</param>
    /// <param name="onAction">Обработчик действия: (действие, x, y в физических пикселях); вызывается в потоке UI.</param>
    public LowLevelHooks(DragDetector detector, Dispatcher ui, Action<DragAction, int, int> onAction)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _onAction = onAction ?? throw new ArgumentNullException(nameof(onAction));
        _mouseProc = MouseProc;
        _keyboardProc = KeyboardProc;
    }

    /// <summary>Запускает поток хуков и ждёт установки. Ошибка установки — <see cref="Win32Exception"/>.</summary>
    public void Start()
    {
        Exception? error = null;
        // Не освобождаем: при таймауте поток хуков ещё может позвать Set().
        var ready = new ManualResetEventSlim();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is not null) throw new InvalidOperationException("Хуки уже запущены.");

            _thread = new Thread(() => Run(ready, e => error = e))
            {
                Name = "DropWheel input hooks",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
            };
            // Страховка: снять хуки при выходе, даже если Dispose не позовут.
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            _thread.Start();
        }

        if (!ready.Wait(StartTimeout))
        {
            Dispose();
            throw new TimeoutException("Поток хуков не запустился.");
        }
        if (error is not null)
        {
            Dispose();
            throw error;
        }
    }

    /// <summary>Снимает оба хука (в потоке хуков) и останавливает поток.</summary>
    public void Dispose()
    {
        Thread? thread;
        uint threadId;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            thread = _thread;
            threadId = _threadId;
        }
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

        if (thread is not null && thread != Thread.CurrentThread && thread.IsAlive)
        {
            // WM_QUIT завершает цикл GetMessage; хуки снимаются в finally потока.
            if (threadId != 0) PostThreadMessageW(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            thread.Join(StopTimeout);
        }

        // Если поток не успел (или Dispose позвали из него самого) — снимаем отсюда.
        Unhook();
    }

    private void OnProcessExit(object? sender, EventArgs e) => Dispose();

    private void Run(ManualResetEventSlim ready, Action<Exception> fail)
    {
        try
        {
            // Создаём очередь сообщений потока до того, как кто-то сможет послать WM_QUIT.
            PeekMessageW(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);
            lock (_gate)
            {
                if (_disposed) { ready.Set(); return; }
                _threadId = GetCurrentThreadId();
            }

            // Для low-level хуков DLL не нужна: достаточно дескриптора собственного модуля.
            var module = GetModuleHandleW(null);
            _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, _mouseProc, module, 0);
            if (_mouseHook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось установить хук мыши.");
            _keyboardHook = SetWindowsHookExW(WH_KEYBOARD_LL, _keyboardProc, module, 0);
            if (_keyboardHook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось установить хук клавиатуры.");
        }
        catch (Exception e)
        {
            Unhook();
            fail(e);
            ready.Set();
            return;
        }

        ready.Set();
        try
        {
            // 0 — WM_QUIT, -1 — ошибка; хуки вызываются, пока поток ждёт в GetMessage.
            while (GetMessageW(out _, IntPtr.Zero, 0, 0) > 0) { }
        }
        finally
        {
            Unhook();
        }
    }

    private void Unhook()
    {
        var mouse = Interlocked.Exchange(ref _mouseHook, IntPtr.Zero);
        if (mouse != IntPtr.Zero) UnhookWindowsHookEx(mouse);
        var keyboard = Interlocked.Exchange(ref _keyboardHook, IntPtr.Zero);
        if (keyboard != IntPtr.Zero) UnhookWindowsHookEx(keyboard);
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == HC_ACTION)
        {
            try
            {
                var msg = (int)wParam;
                if (msg is WM_MOUSEMOVE or WM_LBUTTONDOWN or WM_LBUTTONUP)
                {
                    var x = Marshal.ReadInt32(lParam, MousePtXOffset);
                    var y = Marshal.ReadInt32(lParam, MousePtYOffset);
                    switch (msg)
                    {
                        case WM_MOUSEMOVE:
                            _detector.MouseMove(x, y);
                            break;
                        case WM_LBUTTONDOWN:
                            _detector.MouseDown(x, y);
                            break;
                        case WM_LBUTTONUP:
                            Post(_detector.MouseUp(x, y), x, y);
                            break;
                    }
                }
            }
            catch
            {
                // Исключение не должно уйти в Windows: ввод пользователя важнее.
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == HC_ACTION)
        {
            try
            {
                var flags = Marshal.ReadInt32(lParam, KbdFlagsOffset);
                if ((flags & LLKHF_INJECTED) == 0)
                {
                    var vk = Marshal.ReadInt32(lParam, KbdVkCodeOffset);
                    var swallow = false;
                    switch ((int)wParam)
                    {
                        case WM_KEYDOWN or WM_SYSKEYDOWN:
                            (var action, swallow) = _detector.KeyDown(vk);
                            if (action != DragAction.None)
                            {
                                var (x, y) = _detector.LastPosition;
                                Post(action, x, y);
                            }
                            break;
                        case WM_KEYUP or WM_SYSKEYUP:
                            swallow = _detector.KeyUp(vk);
                            break;
                    }
                    if (swallow) return 1; // не пускаем клавишу дальше
                }
            }
            catch
            {
                // См. MouseProc.
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void Post(DragAction action, int x, int y)
    {
        if (action == DragAction.None) return;
        _ui.BeginInvoke(DispatcherPriority.Send, () => _onAction(action, x, y));
    }
}
