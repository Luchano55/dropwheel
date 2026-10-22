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
/// Исключение — демо-режим для CI (<see cref="AcceptInjectedKeys"/>, DROPWHEEL_DEMO=1).
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

    /// <summary>
    /// ТОЛЬКО для автоматического демо-ролика / сквозного теста в CI (build/demo/record-demo.ps1):
    /// при DROPWHEEL_DEMO=1 программные нажатия клавиш (SendInput) тоже доходят до детектора —
    /// иначе робот не сможет «нажать Пробел». Без этой переменной поведение не меняется.
    /// </summary>
    public static bool AcceptInjectedKeys { get; } = Environment.GetEnvironmentVariable("DROPWHEEL_DEMO") == "1";

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

    // Страж: Windows молча снимает low-level хук, если обработчик хоть раз не уложился в LowLevelHooksTimeout
    // (бывает при сильной нагрузке, после сна). Узнать об этом нельзя — сравниваем время последнего
    // ввода в системе (GetLastInputInfo) с последним событием, дошедшим до наших хуков.
    private static readonly TimeSpan WatchdogPeriod = TimeSpan.FromSeconds(5);
    private const uint DeadAfterMs = 3000;
    private static readonly TimeSpan MinReinstallGap = TimeSpan.FromSeconds(30);
    private readonly Action<string>? _log;
    private Timer? _watchdog;
    private long _lastEventTick;
    private long _lastReinstallTick;
    private bool _disposed;

    /// <param name="detector">Детектор перетаскивания (вызывается из потока хуков).</param>
    /// <param name="ui">Диспетчер потока UI, куда доставляются действия.</param>
    /// <param name="onAction">Обработчик действия: (действие, x, y в физических пикселях); вызывается в потоке UI.</param>
    /// <param name="log">Журнал (переустановка хуков); необязателен.</param>
    public LowLevelHooks(DragDetector detector, Dispatcher ui, Action<DragAction, int, int> onAction, Action<string>? log = null)
    {
        _log = log;
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

        Volatile.Write(ref _lastEventTick, Environment.TickCount64);
        _watchdog = new Timer(_ => CheckAlive(), null, WatchdogPeriod, WatchdogPeriod);
        // После блокировки экрана и сна отпускания клавиш могли потеряться, а хуки — слететь.
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason is Microsoft.Win32.SessionSwitchReason.SessionUnlock or Microsoft.Win32.SessionSwitchReason.ConsoleConnect
            or Microsoft.Win32.SessionSwitchReason.RemoteConnect)
            RequestReinstall("разблокировка экрана");
    }

    private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume) RequestReinstall("выход из сна");
    }

    /// <summary>Пользователь что-то делал, а до хуков ничего не дошло — значит, Windows их сняла.</summary>
    private void CheckAlive()
    {
        try
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return;
            // Оба значения — миллисекунды GetTickCount; разница в uint корректна и при переполнении.
            var lastHook = (uint)Volatile.Read(ref _lastEventTick);
            var gap = unchecked(info.dwTime - lastHook);
            if (gap > DeadAfterMs && gap < int.MaxValue) RequestReinstall($"ввод не доходит до хуков {gap} мс");
        }
        catch
        {
            // Страж не должен ронять программу.
        }
    }

    private void RequestReinstall(string reason)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastReinstallTick);
        if (now - last < MinReinstallGap.TotalMilliseconds && last != 0) return;
        Interlocked.Exchange(ref _lastReinstallTick, now);
        uint threadId;
        lock (_gate)
        {
            if (_disposed) return;
            threadId = _threadId;
        }
        _log?.Invoke($"Хуки переустанавливаются: {reason}");
        if (threadId != 0) PostThreadMessageW(threadId, WM_APP_REINSTALL, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>В потоке хуков: снять, поставить заново, забыть «зажатые» клавиши.</summary>
    private void Reinstall()
    {
        Unhook();
        var module = GetModuleHandleW(null);
        _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, _mouseProc, module, 0);
        _keyboardHook = SetWindowsHookExW(WH_KEYBOARD_LL, _keyboardProc, module, 0);
        _detector.ResetKeys();
        Volatile.Write(ref _lastEventTick, Environment.TickCount64);
        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
            _log?.Invoke($"Переустановка хуков не удалась: ошибка {Marshal.GetLastWin32Error()}");
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
        _watchdog?.Dispose();
        Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;

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
            while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == WM_APP_REINSTALL) Reinstall();
            }
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
        Volatile.Write(ref _lastEventTick, Environment.TickCount64);
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
        Volatile.Write(ref _lastEventTick, Environment.TickCount64);
        if (nCode == HC_ACTION)
        {
            try
            {
                var flags = Marshal.ReadInt32(lParam, KbdFlagsOffset);
                if ((flags & LLKHF_INJECTED) == 0 || AcceptInjectedKeys)
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
