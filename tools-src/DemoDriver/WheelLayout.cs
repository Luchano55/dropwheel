using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Wheel;

namespace DemoDriver;

/// <summary>
/// Где на колесе нужный сектор. Считаем тем же кодом, что и программа (DropWheel.Core):
/// каталог операций → доступные здесь → сектора режима → угол середины сектора.
/// Радиус кольца — середина между «Отменой» и внешним краем (WheelControl: 55 и 130 DIP).
/// </summary>
internal static class WheelLayout
{
    /// <summary>Середина кольца секторов, DIP. Если поменяется рисунок колеса — поправить здесь (или --ring-radius).</summary>
    public const double DefaultRingRadiusDip = 92;

    /// <summary>Сектора колеса для файла после toggles нажатий «сменить режим».</summary>
    public static IReadOnlyList<WheelItem> Items(string file, int toggles, string toolsDir)
    {
        var tools = new ToolLocator { ToolsDirectory = toolsDir };
        var ops = OperationCatalog.ForFiles([file], (op, _) => Available(op, tools));
        var state = new WheelState();
        state.SetItems(WheelItems.For(ops, OperationMode.Convert), WheelItems.For(ops, OperationMode.Tool));
        for (var i = 0; i < toggles; i++) state.Toggle();
        return state.Items;
    }

    /// <summary>
    /// Доступность как у программы (Converter.CanRun): внешняя утилита лежит в tools/;
    /// встроенные движки приложения считаем доступными, кроме Office (на сборочной машине его нет).
    /// </summary>
    private static bool Available(Operation op, ToolLocator tools) =>
        EngineAvailable(op.Engine, tools) || (op.Fallback is { } fb && EngineAvailable(fb, tools));

    private static bool EngineAvailable(EngineKind kind, ToolLocator tools)
    {
        try
        {
            return tools.IsAvailable(kind);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Не внешняя утилита — встроенный движок (ZIP и т.п.). По имени, чтобы не зависеть от состава перечисления.
            return kind.ToString() != "Office";
        }
    }

    /// <summary>Точка середины сектора index из count: смещение от центра колеса в пикселях (Y вниз).</summary>
    public static (double X, double Y) SectorPoint(int index, int count, double radiusPx) =>
        WheelGeometry.PointAt(WheelGeometry.MidAngle(index, count), radiusPx);

    public static double SectorAngle(int index, int count) => WheelGeometry.MidAngle(index, count);
}
