using DropWheel.Input;

namespace DropWheel.Tests.Input;

public class DragDetectorTests
{
    private const int Space = 0x20;
    private const int F2 = 0x71;
    private const int KeyA = 0x41;
    private const int Shift = 0x10;
    private const int Esc = Hotkeys.Escape;

    /// <summary>Детектор, в котором уже идёт перетаскивание (кнопка зажата, сдвиг 20 px).</summary>
    private static DragDetector Dragging(int hotkey = Space)
    {
        var d = new DragDetector(hotkey);
        d.MouseDown(100, 100);
        d.MouseMove(120, 100);
        return d;
    }

    /// <summary>Перетаскивание с открытым колесом (горячая клавиша нажата и отпущена).</summary>
    private static DragDetector WheelShown()
    {
        var d = Dragging();
        Assert.Equal((DragAction.ShowWheel, true), d.KeyDown(Space));
        Assert.True(d.KeyUp(Space));
        return d;
    }

    // --- Порог сдвига ---

    [Fact]
    public void Move_within_threshold_is_not_a_drag()
    {
        var d = new DragDetector(Space);
        d.MouseDown(100, 100);
        Assert.Equal(DragAction.None, d.MouseMove(110, 100)); // ровно 10 px — ещё не перетаскивание
        Assert.False(d.IsDragCandidate);
        Assert.Equal((DragAction.None, false), d.KeyDown(Space));
    }

    [Fact]
    public void Move_beyond_threshold_is_a_drag()
    {
        var d = new DragDetector(Space);
        d.MouseDown(100, 100);
        Assert.Equal(DragAction.None, d.MouseMove(111, 100));
        Assert.True(d.IsDragCandidate);
    }

    [Fact]
    public void Threshold_is_euclidean()
    {
        var d = new DragDetector(Space);
        d.MouseDown(0, 0);
        d.MouseMove(7, 7); // ≈ 9.9 px: по каждой оси меньше 10 и по диагонали тоже
        Assert.False(d.IsDragCandidate);
        d.MouseMove(8, 7); // ≈ 10.6 px
        Assert.True(d.IsDragCandidate);
    }

    [Fact]
    public void Moving_back_does_not_cancel_drag()
    {
        var d = Dragging();
        d.MouseMove(100, 100);
        Assert.True(d.IsDragCandidate);
    }

    [Fact]
    public void Custom_threshold()
    {
        var d = new DragDetector(Space, thresholdPx: 3);
        d.MouseDown(0, 0);
        d.MouseMove(0, -4);
        Assert.True(d.IsDragCandidate);
    }

    [Fact]
    public void Move_without_button_is_not_a_drag()
    {
        var d = new DragDetector(Space);
        d.MouseMove(0, 0);
        d.MouseMove(500, 500);
        Assert.False(d.IsDragCandidate);
        Assert.Equal((DragAction.None, false), d.KeyDown(Space));
    }

    [Fact]
    public void Last_position_follows_mouse()
    {
        var d = new DragDetector(Space);
        d.MouseMove(-1920, 300); // второй монитор слева — отрицательные координаты
        Assert.Equal((-1920, 300), d.LastPosition);
        d.MouseDown(5, 6);
        Assert.Equal((5, 6), d.LastPosition);
        d.MouseUp(7, 8);
        Assert.Equal((7, 8), d.LastPosition);
    }

    // --- Показ колеса ---

    [Fact]
    public void Hotkey_during_drag_shows_wheel_and_is_swallowed()
    {
        var d = Dragging();
        Assert.Equal((DragAction.ShowWheel, true), d.KeyDown(Space));
        Assert.True(d.WheelOpen);
    }

    [Fact]
    public void Other_key_during_drag_passes_through()
    {
        var d = Dragging();
        Assert.Equal((DragAction.None, false), d.KeyDown(Shift)); // Shift/Ctrl/Alt нужны проводнику
        Assert.False(d.KeyUp(Shift));
        Assert.Equal((DragAction.None, false), d.KeyDown(KeyA));
        Assert.False(d.WheelOpen);
    }

    [Fact]
    public void Escape_during_drag_without_wheel_passes_through()
    {
        var d = Dragging();
        Assert.Equal((DragAction.None, false), d.KeyDown(Esc)); // проводник отменит перетаскивание сам
        Assert.False(d.KeyUp(Esc));
    }

    [Fact]
    public void Hotkey_held_before_drag_does_not_show_wheel_on_autorepeat()
    {
        var d = new DragDetector(Space);
        Assert.Equal((DragAction.None, false), d.KeyDown(Space));
        d.MouseDown(0, 0);
        d.MouseMove(50, 0);
        Assert.Equal((DragAction.None, false), d.KeyDown(Space)); // автоповтор
        Assert.False(d.KeyUp(Space));
        Assert.Equal((DragAction.ShowWheel, true), d.KeyDown(Space)); // новое нажатие
    }

    // --- Переключение режима и автоповтор ---

    [Fact]
    public void Hotkey_while_wheel_open_toggles_mode()
    {
        var d = WheelShown();
        Assert.Equal((DragAction.ToggleMode, true), d.KeyDown(Space));
        Assert.True(d.KeyUp(Space));
        Assert.Equal((DragAction.ToggleMode, true), d.KeyDown(Space));
        Assert.True(d.WheelOpen);
    }

    [Fact]
    public void Autorepeat_does_not_trigger_again_but_is_swallowed()
    {
        var d = Dragging();
        Assert.Equal((DragAction.ShowWheel, true), d.KeyDown(Space));
        for (var i = 0; i < 5; i++)
            Assert.Equal((DragAction.None, true), d.KeyDown(Space));
        Assert.True(d.KeyUp(Space));
        Assert.Equal((DragAction.ToggleMode, true), d.KeyDown(Space));
        Assert.Equal((DragAction.None, true), d.KeyDown(Space));
    }

    [Fact]
    public void Wheel_opened_by_host_without_drag_toggles_and_closes()
    {
        // Колесо из контекстного меню: перетаскивания нет, но клавиши работают.
        var d = new DragDetector(Space) { WheelOpen = true };
        Assert.Equal((DragAction.ToggleMode, true), d.KeyDown(Space));
        Assert.Equal((DragAction.CloseWheel, true), d.KeyDown(Esc));
        Assert.False(d.WheelOpen);
    }

    [Fact]
    public void Other_keys_pass_while_wheel_open()
    {
        var d = WheelShown();
        Assert.Equal((DragAction.None, false), d.KeyDown(Shift));
        Assert.False(d.KeyUp(Shift));
    }

    // --- Esc ---

    [Fact]
    public void Escape_while_wheel_open_closes_it()
    {
        var d = WheelShown();
        Assert.Equal((DragAction.CloseWheel, true), d.KeyDown(Esc));
        Assert.False(d.WheelOpen);
        Assert.True(d.KeyUp(Esc));
    }

    [Fact]
    public void Escape_autorepeat_closes_once()
    {
        var d = WheelShown();
        Assert.Equal((DragAction.CloseWheel, true), d.KeyDown(Esc));
        Assert.Equal((DragAction.None, true), d.KeyDown(Esc));
        Assert.True(d.KeyUp(Esc));
    }

    [Fact]
    public void Hotkey_after_escape_in_same_drag_shows_wheel_again()
    {
        var d = WheelShown();
        d.KeyDown(Esc);
        d.KeyUp(Esc);
        Assert.Equal((DragAction.ShowWheel, true), d.KeyDown(Space));
    }

    [Fact]
    public void Host_closing_wheel_returns_to_drag_state()
    {
        var d = WheelShown();
        d.WheelOpen = false;
        Assert.Equal((DragAction.ShowWheel, true), d.KeyDown(Space));
    }

    // --- Обычный набор текста ---

    [Fact]
    public void Typing_outside_drag_is_not_touched()
    {
        var d = new DragDetector(Space);
        foreach (var vk in new[] { Space, KeyA, Esc, F2, Shift })
        {
            Assert.Equal((DragAction.None, false), d.KeyDown(vk));
            Assert.Equal((DragAction.None, false), d.KeyDown(vk)); // автоповтор
            Assert.False(d.KeyUp(vk));
        }
    }

    [Fact]
    public void Hotkey_after_click_without_move_is_not_touched()
    {
        var d = new DragDetector(Space);
        d.MouseDown(10, 10);
        d.MouseMove(12, 11);
        Assert.Equal((DragAction.None, false), d.KeyDown(Space));
        Assert.False(d.KeyUp(Space));
    }

    [Fact]
    public void Hotkey_after_drag_finished_is_not_touched()
    {
        var d = Dragging();
        d.MouseUp(120, 100);
        Assert.Equal((DragAction.None, false), d.KeyDown(Space));
    }

    // --- Отпускание проглоченных клавиш ---

    [Fact]
    public void Key_up_is_swallowed_only_after_swallowed_down()
    {
        var d = Dragging();
        Assert.False(d.KeyUp(Space)); // отпускание без нажатия
        d.KeyDown(Space);
        Assert.True(d.KeyUp(Space));
        Assert.False(d.KeyUp(Space)); // повторное отпускание уже не наше
    }

    [Fact]
    public void Key_up_is_swallowed_even_after_drag_ended()
    {
        // Пробел нажат во время перетаскивания, кнопку мыши отпустили раньше пробела.
        var d = Dragging();
        d.KeyDown(Space);
        Assert.Equal(DragAction.MouseReleased, d.MouseUp(120, 100));
        d.WheelOpen = false;
        Assert.Equal((DragAction.None, true), d.KeyDown(Space)); // автоповтор всё ещё глотается
        Assert.True(d.KeyUp(Space));
        Assert.Equal((DragAction.None, false), d.KeyDown(Space));
    }

    [Fact]
    public void Key_up_is_swallowed_after_hotkey_change()
    {
        var d = Dragging();
        d.KeyDown(Space);
        d.HotkeyVk = F2;
        Assert.True(d.KeyUp(Space));
    }

    [Fact]
    public void Reset_keys_forgets_stuck_key()
    {
        var d = Dragging();
        d.KeyDown(Space); // отпускание потерялось (например, блокировка экрана)
        d.ResetKeys();
        d.WheelOpen = false;
        Assert.Equal((DragAction.ShowWheel, true), d.KeyDown(Space));
    }

    // --- Отпускание кнопки мыши ---

    [Fact]
    public void Mouse_up_after_drag_reports_release()
    {
        var d = Dragging();
        Assert.Equal(DragAction.MouseReleased, d.MouseUp(130, 100));
        Assert.False(d.IsDragCandidate);
    }

    [Fact]
    public void Mouse_up_with_wheel_open_reports_release()
    {
        var d = WheelShown();
        Assert.Equal(DragAction.MouseReleased, d.MouseUp(130, 100));
    }

    [Fact]
    public void Mouse_up_after_simple_click_is_nothing()
    {
        var d = new DragDetector(Space);
        d.MouseDown(10, 10);
        Assert.Equal(DragAction.None, d.MouseUp(10, 10));
    }

    [Fact]
    public void Mouse_up_without_mouse_down_is_nothing()
    {
        var d = new DragDetector(Space) { WheelOpen = true };
        Assert.Equal(DragAction.None, d.MouseUp(10, 10));
    }

    [Fact]
    public void Mouse_up_twice_reports_once()
    {
        var d = Dragging();
        Assert.Equal(DragAction.MouseReleased, d.MouseUp(130, 100));
        Assert.Equal(DragAction.None, d.MouseUp(130, 100));
    }

    [Fact]
    public void Mouse_up_does_not_close_wheel_itself()
    {
        // Решение «прятать или нет» за хозяином: он знает, пришёл ли DragEnter.
        var d = WheelShown();
        d.MouseUp(130, 100);
        Assert.True(d.WheelOpen);
    }

    [Fact]
    public void Second_mouse_down_resets_drag()
    {
        var d = Dragging();
        d.MouseDown(500, 500);
        Assert.False(d.IsDragCandidate);
        d.MouseMove(505, 505); // ≈ 7 px от новой точки
        Assert.False(d.IsDragCandidate);
        Assert.Equal((DragAction.None, false), d.KeyDown(Space));
        d.MouseMove(520, 500);
        Assert.True(d.IsDragCandidate);
    }

    [Fact]
    public void New_drag_after_release_works()
    {
        var d = Dragging();
        d.MouseUp(120, 100);
        d.MouseDown(0, 0);
        d.MouseMove(0, 30);
        Assert.Equal((DragAction.ShowWheel, true), d.KeyDown(Space));
    }

    // --- Смена клавиши ---

    [Fact]
    public void Hotkey_can_be_changed_on_the_fly()
    {
        var d = Dragging();
        d.HotkeyVk = F2;
        Assert.Equal((DragAction.None, false), d.KeyDown(Space));
        Assert.False(d.KeyUp(Space));
        Assert.Equal((DragAction.ShowWheel, true), d.KeyDown(F2));
        Assert.True(d.KeyUp(F2));
        Assert.Equal((DragAction.ToggleMode, true), d.KeyDown(F2));
    }

    [Fact]
    public void Other_hotkey_from_constructor()
    {
        var d = Dragging(hotkey: 0x14); // CapsLock
        Assert.Equal((DragAction.None, false), d.KeyDown(Space));
        Assert.Equal((DragAction.ShowWheel, true), d.KeyDown(0x14));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(256)]
    [InlineData(Hotkeys.Escape)]
    public void Invalid_hotkey_is_rejected(int vk)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DragDetector(vk));
        var d = new DragDetector(Space);
        Assert.Throws<ArgumentOutOfRangeException>(() => d.HotkeyVk = vk);
        Assert.Equal(Space, d.HotkeyVk);
    }

    [Fact]
    public void Negative_threshold_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DragDetector(Space, -1));
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(256)]
    [InlineData(int.MaxValue)]
    public void Out_of_range_key_codes_are_ignored(int vk)
    {
        var d = WheelShown();
        Assert.Equal((DragAction.None, false), d.KeyDown(vk));
        Assert.False(d.KeyUp(vk));
    }

    [Fact]
    public void Huge_coordinates_do_not_overflow()
    {
        var d = new DragDetector(Space);
        d.MouseDown(int.MinValue / 2, 0);
        d.MouseMove(int.MaxValue / 2, 0);
        Assert.True(d.IsDragCandidate);
    }

    // --- Без выделения памяти ---

    [Fact]
    public void Events_do_not_allocate()
    {
        var d = new DragDetector(Space);
        void Cycle()
        {
            d.MouseDown(0, 0);
            for (var i = 0; i < 50; i++) d.MouseMove(i, i);
            d.KeyDown(Space);
            d.KeyDown(Space);
            d.KeyUp(Space);
            d.KeyDown(Space);
            d.KeyUp(Space);
            d.KeyDown(Esc);
            d.KeyUp(Esc);
            d.KeyDown(KeyA);
            d.KeyUp(KeyA);
            _ = d.LastPosition;
            d.MouseUp(50, 50);
        }

        Cycle(); // прогрев JIT
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) Cycle();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Lost_key_up_does_not_kill_the_hotkey()
    {
        // Пробел нажали в окне, куда хук не видит отпускания (UAC, экран блокировки) — «залип».
        var d = new DragDetector(Space);
        Assert.Equal((DragAction.None, false), d.KeyDown(Space, 1_000));
        // Через минуту: перетаскиваем и жмём Пробел — должно сработать, а не считаться автоповтором.
        d.MouseDown(0, 0);
        d.MouseMove(50, 0);
        Assert.Equal((DragAction.ShowWheel, true), d.KeyDown(Space, 61_000));
    }

    [Fact]
    public void Real_autorepeat_within_threshold_stays_autorepeat()
    {
        var d = Dragging();
        Assert.Equal((DragAction.ShowWheel, true), d.KeyDown(Space, 10_000));
        Assert.Equal((DragAction.None, true), d.KeyDown(Space, 10_500)); // первый автоповтор
        Assert.Equal((DragAction.None, true), d.KeyDown(Space, 10_533));
        Assert.Equal((DragAction.None, true), d.KeyDown(Space, 11_400));
    }
}
