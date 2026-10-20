using DropWheel.Shell;

namespace DropWheel.Tests.Shell;

public class TrayFlyoutPlacementTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void Bottom_taskbar_puts_flyout_above_the_tray_corner()
    {
        var work = new PixelRect(0, 0, 1920, 1032);
        var (x, y, edge) = TrayFlyoutPlacement.Place(Monitor, work, null, 380, 400, 12);
        Assert.Equal(TaskbarEdge.Bottom, edge);
        Assert.Equal(1920 - 12 - 380, x);
        Assert.Equal(1032 - 12 - 400, y);
    }

    [Fact]
    public void Top_taskbar_puts_flyout_below_it()
    {
        var work = new PixelRect(0, 48, 1920, 1080);
        var (x, y, edge) = TrayFlyoutPlacement.Place(Monitor, work, null, 380, 400, 12);
        Assert.Equal(TaskbarEdge.Top, edge);
        Assert.Equal(1920 - 12 - 380, x);
        Assert.Equal(48 + 12, y);
    }

    [Fact]
    public void Left_taskbar_puts_flyout_at_bottom_left()
    {
        var work = new PixelRect(62, 0, 1920, 1080);
        var (x, y, edge) = TrayFlyoutPlacement.Place(Monitor, work, null, 380, 400, 12);
        Assert.Equal(TaskbarEdge.Left, edge);
        Assert.Equal(62 + 12, x);
        Assert.Equal(1080 - 12 - 400, y);
    }

    [Fact]
    public void Right_taskbar_puts_flyout_at_bottom_right()
    {
        var work = new PixelRect(0, 0, 1858, 1080);
        var (x, y, edge) = TrayFlyoutPlacement.Place(Monitor, work, null, 380, 400, 12);
        Assert.Equal(TaskbarEdge.Right, edge);
        Assert.Equal(1858 - 12 - 380, x);
        Assert.Equal(1080 - 12 - 400, y);
    }

    [Fact]
    public void Second_monitor_with_negative_coordinates()
    {
        var monitor = new PixelRect(-2560, -200, 0, 1240);
        var work = new PixelRect(-2560, -200, 0, 1180);
        var (x, y, edge) = TrayFlyoutPlacement.Place(monitor, work, null, 570, 600, 18);
        Assert.Equal(TaskbarEdge.Bottom, edge);
        Assert.Equal(-18 - 570, x);
        Assert.Equal(1180 - 18 - 600, y);
    }

    [Fact]
    public void Auto_hidden_taskbar_is_detected_by_its_rectangle()
    {
        var taskbar = new PixelRect(0, 1078, 1920, 1126); // спрятана почти за нижним краем
        var (_, y, edge) = TrayFlyoutPlacement.Place(Monitor, Monitor, taskbar, 380, 400, 12);
        Assert.Equal(TaskbarEdge.Bottom, edge);
        Assert.True(y + 400 <= 1080 - 12, "окно не должно уходить под выезжающую панель");

        var top = new PixelRect(0, -46, 1920, 2);
        var (_, yTop, edgeTop) = TrayFlyoutPlacement.Place(Monitor, Monitor, top, 380, 400, 12);
        Assert.Equal(TaskbarEdge.Top, edgeTop);
        Assert.True(yTop >= 12);
    }

    [Fact]
    public void No_taskbar_on_this_monitor_defaults_to_bottom_right()
    {
        var elsewhere = new PixelRect(1920, 1032, 3840, 1080);
        var (x, y, edge) = TrayFlyoutPlacement.Place(Monitor, Monitor, elsewhere, 380, 400, 12);
        Assert.Equal(TaskbarEdge.Bottom, edge);
        Assert.Equal(1920 - 12 - 380, x);
        Assert.Equal(1080 - 12 - 400, y);
    }

    [Fact]
    public void Too_big_flyout_is_pinned_inside_the_work_area()
    {
        var work = new PixelRect(0, 0, 800, 560);
        var (x, y, _) = TrayFlyoutPlacement.Place(new PixelRect(0, 0, 800, 600), work, null, 1000, 900, 12);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }
}
