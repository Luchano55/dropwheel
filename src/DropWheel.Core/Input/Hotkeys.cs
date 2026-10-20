namespace DropWheel.Input;

/// <summary>
/// Горячие клавиши колеса: имя из settings.json → код виртуальной клавиши Windows (VK_*).
/// Shift/Ctrl/Alt намеренно не поддерживаются: во время перетаскивания проводник
/// использует их для «переместить / копировать / ярлык» (решение D7, ждёт ответа Романа).
/// </summary>
public static class Hotkeys
{
    /// <summary>Клавиша по умолчанию.</summary>
    public const string Default = "Space";

    /// <summary>VK_ESCAPE — закрыть колесо.</summary>
    public const int Escape = 0x1B;

    private static readonly (string Name, int Vk)[] Table = BuildTable();

    private static readonly Dictionary<string, int> ByName =
        Table.ToDictionary(t => t.Name, t => t.Vk, StringComparer.OrdinalIgnoreCase);

    /// <summary>Имена в порядке показа в настройках.</summary>
    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(Table.Select(t => t.Name).ToArray());

    /// <summary>Код клавиши по имени (без учёта регистра); неизвестное имя → null.</summary>
    public static int? ToVirtualKey(string? name) =>
        name is not null && ByName.TryGetValue(name, out var vk) ? vk : null;

    private static (string, int)[] BuildTable()
    {
        var list = new List<(string, int)>
        {
            ("Space", 0x20),     // VK_SPACE
            ("Backquote", 0xC0), // VK_OEM_3: ` ~ (на русской раскладке — Ё)
            ("CapsLock", 0x14),  // VK_CAPITAL
        };
        for (var i = 1; i <= 12; i++)
            list.Add(($"F{i}", 0x70 + i - 1)); // VK_F1 = 0x70 … VK_F12 = 0x7B
        return list.ToArray();
    }
}
