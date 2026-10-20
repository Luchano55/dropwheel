using DropWheel.Conversions;

namespace DropWheel.Wheel;

/// <summary>Сектор колеса: id операции и подпись.</summary>
public sealed record WheelItem(string Id, string Label);

/// <summary>Сектора колеса из каталога: по режиму, не больше 8, в порядке каталога.</summary>
public static class WheelItems
{
    public static IReadOnlyList<WheelItem> For(IReadOnlyList<Operation> ops, OperationMode mode) =>
        ops.Where(o => o.Mode == mode)
           .Take(WheelGeometry.MaxSectors)
           .Select(o => new WheelItem(o.Id, o.Label))
           .ToList();
}
