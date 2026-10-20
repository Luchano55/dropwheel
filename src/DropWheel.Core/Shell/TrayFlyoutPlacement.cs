namespace DropWheel.Shell;

/// <summary>Край экрана, у которого стоит панель задач.</summary>
public enum TaskbarEdge { Bottom, Top, Left, Right }

/// <summary>Прямоугольник в физических пикселях (Right/Bottom — не включительно).</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public bool Intersects(PixelRect other) =>
        Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;
}

/// <summary>
/// Где показать окно задач у трея (как всплывающие окна Windows 11): в углу у области уведомлений,
/// с отступом от панели задач, на мониторе под курсором. Панель задач может быть у любого края;
/// при автоскрытии рабочая область совпадает с монитором — тогда край берётся по прямоугольнику панели.
/// </summary>
public static class TrayFlyoutPlacement
{
    public static TaskbarEdge EdgeOf(PixelRect monitor, PixelRect work, PixelRect? taskbar)
    {
        if (work.Bottom < monitor.Bottom) return TaskbarEdge.Bottom;
        if (work.Top > monitor.Top) return TaskbarEdge.Top;
        if (work.Left > monitor.Left) return TaskbarEdge.Left;
        if (work.Right < monitor.Right) return TaskbarEdge.Right;

        // Автоскрытие или панели на этом мониторе нет: смотрим, к какому краю ближе сама панель.
        if (taskbar is { } t && t.Intersects(monitor))
        {
            if (t.Width >= t.Height)
                return t.Top - monitor.Top < monitor.Bottom - t.Bottom ? TaskbarEdge.Top : TaskbarEdge.Bottom;
            return t.Left - monitor.Left < monitor.Right - t.Right ? TaskbarEdge.Left : TaskbarEdge.Right;
        }
        return TaskbarEdge.Bottom;
    }

    /// <summary>Левый верхний угол окна width×height и край панели (с какой стороны «выезжать»).</summary>
    public static (int X, int Y, TaskbarEdge Edge) Place(PixelRect monitor, PixelRect work, PixelRect? taskbar,
        int width, int height, int margin)
    {
        var edge = EdgeOf(monitor, work, taskbar);
        var area = work;

        // Скрытая панель выезжает поверх рабочей области — не прячем окно под ней.
        if (work == monitor && taskbar is { } t && t.Intersects(monitor))
        {
            var thickness = edge is TaskbarEdge.Bottom or TaskbarEdge.Top ? t.Height : t.Width;
            thickness = Math.Clamp(thickness, 0, Math.Min(monitor.Width, monitor.Height) / 3);
            area = edge switch
            {
                TaskbarEdge.Bottom => area with { Bottom = area.Bottom - thickness },
                TaskbarEdge.Top => area with { Top = area.Top + thickness },
                TaskbarEdge.Left => area with { Left = area.Left + thickness },
                _ => area with { Right = area.Right - thickness },
            };
        }

        var (x, y) = edge switch
        {
            TaskbarEdge.Top => (area.Right - margin - width, area.Top + margin),
            TaskbarEdge.Left => (area.Left + margin, area.Bottom - margin - height),
            _ => (area.Right - margin - width, area.Bottom - margin - height),
        };

        // Не вылезать за рабочую область; если окно больше неё — прижать к левому/верхнему краю.
        x = Math.Max(area.Left, Math.Min(x, area.Right - width));
        y = Math.Max(area.Top, Math.Min(y, area.Bottom - height));
        return (x, y, edge);
    }
}
