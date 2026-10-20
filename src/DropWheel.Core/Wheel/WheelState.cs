using DropWheel.Conversions;

namespace DropWheel.Wheel;

/// <summary>
/// Состояние колеса без UI: два набора секторов и текущий режим.
/// Пустой набор в текущем режиме — «Нечего предложить».
/// </summary>
public sealed class WheelState
{
    private IReadOnlyList<WheelItem> _convert = [];
    private IReadOnlyList<WheelItem> _tools = [];

    public OperationMode Mode { get; private set; } = OperationMode.Convert;

    /// <summary>Сектора текущего режима (не больше 8).</summary>
    public IReadOnlyList<WheelItem> Items => Mode == OperationMode.Convert ? _convert : _tools;

    public bool IsEmpty => Items.Count == 0;

    /// <summary>В обоих режимах есть сектора — переключать режим имеет смысл.</summary>
    public bool HasBothModes => _convert.Count > 0 && _tools.Count > 0;

    /// <summary>Новые наборы; режим — «Конвертация», а если она пуста — «Инструменты».</summary>
    public void SetItems(IReadOnlyList<WheelItem> convertItems, IReadOnlyList<WheelItem> toolItems)
    {
        _convert = Limit(convertItems);
        _tools = Limit(toolItems);
        Mode = _convert.Count == 0 && _tools.Count > 0 ? OperationMode.Tool : OperationMode.Convert;
    }

    /// <summary>Конвертация ↔ Инструменты (повторное нажатие горячей клавиши).</summary>
    public void Toggle() =>
        Mode = Mode == OperationMode.Convert ? OperationMode.Tool : OperationMode.Convert;

    /// <summary>Сектор по номеру; null — нет такого.</summary>
    public WheelItem? At(int? index) =>
        index is { } i && i >= 0 && i < Items.Count ? Items[i] : null;

    private static IReadOnlyList<WheelItem> Limit(IReadOnlyList<WheelItem> items) =>
        items.Count <= WheelGeometry.MaxSectors ? items : items.Take(WheelGeometry.MaxSectors).ToList();
}
