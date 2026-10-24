namespace DropWheel.Input;

/// <summary>
/// Колесо не открывается, пока на весь экран развёрнуто приложение (игра «в окне без рамки», видео):
/// там зажатая кнопка мыши с движением — обычное дело, а не перетаскивание файла (решение D-HOTKEY).
/// Рабочий стол и панель задач на весь экран — тоже «окна», но их не считаем.
/// </summary>
public static class FullscreenRule
{
    /// <summary>Классы окон оболочки Windows: рабочий стол (Progman/WorkerW) и панель задач.</summary>
    public static readonly IReadOnlySet<string> ShellClasses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    /// <summary>WS_CAPTION: у обычного окна (в том числе развёрнутого проводника) есть заголовок, у полноэкранной игры — нет.</summary>
    public const long Caption = 0x00C00000;

    /// <summary>
    /// Окно без заголовка закрывает весь монитор (с запасом в пару пикселей) и это не рабочий стол.
    /// Развёрнутое окно со скрытой панелью задач тоже на весь экран, но у него есть заголовок — его не считаем.
    /// </summary>
    public static bool IsFullscreenApp(string? windowClass, long style, (int Left, int Top, int Right, int Bottom) window,
        (int Left, int Top, int Right, int Bottom) monitor)
    {
        if (windowClass is not null && ShellClasses.Contains(windowClass)) return false;
        if ((style & Caption) == Caption) return false;
        if (monitor.Right <= monitor.Left || monitor.Bottom <= monitor.Top) return false;
        const int slack = 2;
        return window.Left <= monitor.Left + slack && window.Top <= monitor.Top + slack
            && window.Right >= monitor.Right - slack && window.Bottom >= monitor.Bottom - slack;
    }
}
