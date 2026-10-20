namespace DropWheel.Input;

/// <summary>Что должно сделать приложение в ответ на событие хука.</summary>
public enum DragAction
{
    /// <summary>Ничего.</summary>
    None,
    /// <summary>Показать колесо под курсором.</summary>
    ShowWheel,
    /// <summary>Переключить режим колеса: Конвертация ↔ Инструменты.</summary>
    ToggleMode,
    /// <summary>Закрыть колесо (Esc).</summary>
    CloseWheel,
    /// <summary>
    /// Левую кнопку отпустили после перетаскивания или при открытом колесе.
    /// Если колесо не получило DragEnter — тихо спрятать его (тащили окно или текст).
    /// </summary>
    MouseReleased,
}

/// <summary>
/// Детектор перетаскивания (ТЗ, разделы 3–4): автомат состояний, который кормят low-level хуки.
/// Вызывается прямо из обработчика хука, поэтому каждый метод — O(1) и без выделения памяти.
/// <para>
/// Потоки: события мыши и клавиатуры приходят из одного потока хуков.
/// <see cref="WheelOpen"/> и <see cref="HotkeyVk"/> можно менять из потока UI.
/// </para>
/// <para>
/// Порог сдвига — евклидов: «перетаскивание» начинается, когда расстояние от точки нажатия
/// строго больше порога (dx² + dy² &gt; порог²). Координаты — физические пиксели экрана.
/// </para>
/// </summary>
public sealed class DragDetector
{
    private const int KeyCount = 256;

    // Нажатые сейчас клавиши (для распознавания автоповтора) и клавиши,
    // чьё нажатие мы проглотили (их отпускание тоже глотаем).
    private readonly bool[] _down = new bool[KeyCount];
    private readonly bool[] _swallowed = new bool[KeyCount];
    private readonly long _thresholdSquared;

    private volatile int _hotkeyVk;
    private volatile bool _wheelOpen;

    private bool _buttonDown;
    private bool _dragCandidate;
    private int _downX, _downY;
    private int _lastX, _lastY;

    /// <param name="hotkeyVk">Код горячей клавиши (см. <see cref="Hotkeys"/>).</param>
    /// <param name="thresholdPx">Сдвиг в пикселях, после которого считаем, что идёт перетаскивание.</param>
    public DragDetector(int hotkeyVk, int thresholdPx = 10)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(thresholdPx);
        HotkeyVk = hotkeyVk;
        ThresholdPx = thresholdPx;
        _thresholdSquared = (long)thresholdPx * thresholdPx;
    }

    public int ThresholdPx { get; }

    /// <summary>Код горячей клавиши; можно менять на лету (смена клавиши в настройках).</summary>
    public int HotkeyVk
    {
        get => _hotkeyVk;
        set
        {
            if (value is <= 0 or >= KeyCount || value == Hotkeys.Escape)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Недопустимый код горячей клавиши.");
            _hotkeyVk = value;
        }
    }

    /// <summary>
    /// Открыто ли колесо. Хозяин выставляет при показе/скрытии окна; детектор сам ставит true
    /// при <see cref="DragAction.ShowWheel"/> и false при <see cref="DragAction.CloseWheel"/>,
    /// чтобы не зависеть от задержки потока UI. Кнопкой мыши не сбрасывается: колесо может быть
    /// открыто и без перетаскивания (пункт контекстного меню), и тогда клавиша тоже переключает режим.
    /// </summary>
    public bool WheelOpen
    {
        get => _wheelOpen;
        set => _wheelOpen = value;
    }

    /// <summary>Последние известные координаты курсора (физические пиксели).</summary>
    public (int X, int Y) LastPosition => (_lastX, _lastY);

    /// <summary>Сейчас идёт возможное перетаскивание (кнопка зажата и курсор ушёл дальше порога).</summary>
    public bool IsDragCandidate => _dragCandidate;

    /// <summary>Левая кнопка нажата. Повторное нажатие без отпускания начинает отсчёт заново.</summary>
    public void MouseDown(int x, int y)
    {
        _lastX = x;
        _lastY = y;
        _buttonDown = true;
        _dragCandidate = false;
        _downX = x;
        _downY = y;
    }

    /// <summary>Курсор сдвинулся. Только меняет состояние, действие всегда <see cref="DragAction.None"/>.</summary>
    public DragAction MouseMove(int x, int y)
    {
        _lastX = x;
        _lastY = y;
        if (_buttonDown && !_dragCandidate)
        {
            long dx = x - _downX, dy = y - _downY;
            if (dx * dx + dy * dy > _thresholdSquared)
                _dragCandidate = true;
        }
        return DragAction.None;
    }

    /// <summary>
    /// Левая кнопка отпущена. <see cref="DragAction.MouseReleased"/>, если шло перетаскивание
    /// или открыто колесо; отпускание без нажатия — <see cref="DragAction.None"/>. Сбрасывает состояние мыши.
    /// Если колесо открыто из контекстного меню (без перетаскивания), хозяин должен игнорировать это событие.
    /// </summary>
    public DragAction MouseUp(int x, int y)
    {
        _lastX = x;
        _lastY = y;
        var wasDown = _buttonDown;
        var wasDrag = _dragCandidate;
        _buttonDown = false;
        _dragCandidate = false;

        if (!wasDown) return DragAction.None;
        return wasDrag || _wheelOpen ? DragAction.MouseReleased : DragAction.None;
    }

    /// <summary>
    /// Клавиша нажата. Возвращает действие и нужно ли проглотить клавишу (не пускать в другие программы).
    /// Вне перетаскивания и при закрытом колесе клавиши не трогаем.
    /// Автоповтор (нажатие без отпускания) действий не вызывает, но глотается, если глоталось первое нажатие.
    /// </summary>
    public (DragAction Action, bool Swallow) KeyDown(int vk)
    {
        if ((uint)vk >= KeyCount) return (DragAction.None, false);

        if (_down[vk]) // автоповтор
            return (DragAction.None, _swallowed[vk]);
        _down[vk] = true;

        var action = DragAction.None;
        if (_wheelOpen)
        {
            if (vk == _hotkeyVk) action = DragAction.ToggleMode;
            else if (vk == Hotkeys.Escape)
            {
                action = DragAction.CloseWheel;
                _wheelOpen = false;
            }
        }
        else if (_dragCandidate && vk == _hotkeyVk)
        {
            action = DragAction.ShowWheel;
            _wheelOpen = true;
        }

        var swallow = action != DragAction.None;
        _swallowed[vk] = swallow;
        return (action, swallow);
    }

    /// <summary>Клавиша отпущена. true — проглотить (нажатие этой клавиши тоже было проглочено).</summary>
    public bool KeyUp(int vk)
    {
        if ((uint)vk >= KeyCount) return false;
        var swallow = _swallowed[vk];
        _down[vk] = false;
        _swallowed[vk] = false;
        return swallow;
    }

    /// <summary>
    /// Забыть, какие клавиши зажаты. На случай, если отпускание не дошло до хука
    /// (блокировка экрана, окно UAC) и клавиша «залипла» как автоповтор.
    /// </summary>
    public void ResetKeys()
    {
        Array.Clear(_down);
        Array.Clear(_swallowed);
    }
}
