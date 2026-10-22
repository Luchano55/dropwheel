using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DropWheel.Input;

/// <summary>
/// Системная горячая клавиша (RegisterHotKey) на скрытом окне-приёмнике сообщений (HWND_MESSAGE).
/// Создаётся и живёт в потоке UI: WM_HOTKEY приходит в его очередь, обработчик вызывается там же.
/// Если сочетание уже занято другой программой, <see cref="TryRegister"/> возвращает false —
/// приложение пишет в журнал и работает без клавиши (решение D-CLIPBOARD).
/// </summary>
internal sealed partial class GlobalHotkey : IDisposable
{
    public const uint ModAlt = 0x0001, ModControl = 0x0002, ModShift = 0x0004, ModNoRepeat = 0x4000;
    public const uint VkV = 0x56;

    private const int WM_HOTKEY = 0x0312;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly HwndSource _window;
    private readonly int _id;
    private readonly Action _pressed;
    private bool _registered;

    public GlobalHotkey(int id, Action pressed)
    {
        _id = id;
        _pressed = pressed;
        _window = new HwndSource(new HwndSourceParameters("DropWheel.Hotkey")
        {
            ParentWindow = HWND_MESSAGE,
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        });
        _window.AddHook(WndProc);
    }

    public bool IsRegistered => _registered;

    /// <summary>Зарегистрировать сочетание (уже зарегистрированное — заменить). false — занято или ошибка; код — в lastError.</summary>
    public bool TryRegister(uint modifiers, uint key, out int lastError)
    {
        Unregister();
        lastError = 0;
        _registered = RegisterHotKey(_window.Handle, _id, modifiers | ModNoRepeat, key);
        if (!_registered) lastError = Marshal.GetLastPInvokeError();
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(_window.Handle, _id);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _id)
        {
            handled = true;
            _pressed();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);
}
