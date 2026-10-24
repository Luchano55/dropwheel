using DropWheel.Input;

namespace DropWheel.Tests.Input;

public class FullscreenRuleTests
{
    private static readonly (int, int, int, int) Monitor = (0, 0, 1920, 1080);
    private const long Popup = 0x80000000; // WS_POPUP — окно игры без рамки

    [Fact]
    public void Borderless_game_covering_the_monitor_is_fullscreen() =>
        Assert.True(FullscreenRule.IsFullscreenApp("UnityWndClass", Popup, (0, 0, 1920, 1080), Monitor));

    [Fact]
    public void Second_monitor_is_checked_by_its_own_bounds() =>
        Assert.True(FullscreenRule.IsFullscreenApp("Chrome_WidgetWin_1", 0, (1920, 0, 4480, 1440), (1920, 0, 4480, 1440)));

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    public void Desktop_is_not_an_app(string cls) =>
        Assert.False(FullscreenRule.IsFullscreenApp(cls, Popup, (0, 0, 1920, 1080), Monitor));

    [Fact]
    public void Maximized_explorer_with_a_caption_is_not_fullscreen() =>
        // Скрытая панель задач: развёрнутое окно (с рамкой −8 px) закрывает весь монитор, но у него есть заголовок.
        Assert.False(FullscreenRule.IsFullscreenApp("CabinetWClass", FullscreenRule.Caption, (-8, -8, 1928, 1088), Monitor));

    [Fact]
    public void Ordinary_window_is_not_fullscreen() =>
        Assert.False(FullscreenRule.IsFullscreenApp("Notepad", Popup, (100, 100, 900, 700), Monitor));

    [Fact]
    public void Detector_does_not_show_the_wheel_or_swallow_the_key_when_suppressed()
    {
        var space = Hotkeys.ToVirtualKey("Space")!.Value;
        var suppressed = true;
        var detector = new DragDetector(space) { SuppressWheel = () => suppressed };
        detector.MouseDown(100, 100);
        detector.MouseMove(200, 200);
        Assert.True(detector.IsDragCandidate);

        Assert.Equal((DragAction.None, false), detector.KeyDown(space, 1000)); // игра: Пробел проходит в игру
        detector.KeyUp(space);
        suppressed = false;
        Assert.Equal((DragAction.ShowWheel, true), detector.KeyDown(space, 5000));
    }
}
