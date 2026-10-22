using DropWheel.Localization;
namespace DropWheel.Input;

/// <summary>Можно ли сделать клавишу горячей (решение D-HOTKEY).</summary>
public enum HotkeyCheck
{
    /// <summary>Подходит.</summary>
    Ok,
    /// <summary>Ctrl, Shift, Alt, Win: во время перетаскивания ими Windows выбирает «копировать / переместить / ярлык» (D7).</summary>
    Modifier,
    /// <summary>Esc, Enter, Print Screen, кнопки мыши, служебные коды — заняты Windows или колесом.</summary>
    Reserved,
}

/// <summary>
/// Горячие клавиши колеса: имя из settings.json → код виртуальной клавиши Windows (VK_*).
/// Имена — готовый список (<see cref="Names"/>) или «своя клавиша» в виде <c>vk:0x65</c> (D-HOTKEY).
/// Shift/Ctrl/Alt/Win не принимаются: во время перетаскивания проводник
/// использует их для «переместить / копировать / ярлык» (решение D7).
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

    /// <summary>Приставка имени «своей клавиши» в settings.json: <c>vk:0x65</c>.</summary>
    public const string CustomPrefix = "vk:0x";

    /// <summary>
    /// Код клавиши по имени (без учёта регистра): имя из списка или <c>vk:0xNN</c>.
    /// Неизвестное имя или неподходящая клавиша (<see cref="Check"/>) → null.
    /// </summary>
    public static int? ToVirtualKey(string? name)
    {
        if (name is null) return null;
        if (ByName.TryGetValue(name, out var vk)) return vk;
        if (name.Length is > 5 and <= 7 && name.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(CustomPrefix.Length), System.Globalization.NumberStyles.AllowHexSpecifier, null, out vk)
            && Check(vk) == HotkeyCheck.Ok)
            return vk;
        return null;
    }

    /// <summary>Имя для settings.json по коду: из списка, если клавиша там есть, иначе <c>vk:0xNN</c>; неподходящая → null.</summary>
    public static string? FromVirtualKey(int vk)
    {
        if (Check(vk) != HotkeyCheck.Ok) return null;
        foreach (var (name, code) in Table)
            if (code == vk) return name;
        return $"{CustomPrefix}{vk:X2}";
    }

    /// <summary>Подходит ли клавиша (D-HOTKEY).</summary>
    public static HotkeyCheck Check(int vk) => vk switch
    {
        0x10 or 0x11 or 0x12 or (>= 0xA0 and <= 0xA5) or 0x5B or 0x5C => HotkeyCheck.Modifier,
        < 0x08 or > 0xFE // кнопки мыши, VK_CANCEL, мусор
            or 0x0D or Escape or 0x2C // Enter, Esc, Print Screen
            or (>= 0x15 and <= 0x1A) or (>= 0x1C and <= 0x1F) or 0xE5 or 0xE7 // IME, VK_PROCESSKEY, VK_PACKET
            or 0x5F => HotkeyCheck.Reserved, // Sleep
        _ => HotkeyCheck.Ok,
    };

    /// <summary>
    /// Клавиша печатает символ (буква, цифра, знак, цифровой блок). Её можно назначить, но пока идёт
    /// перетаскивание (или открыто колесо), хук её глотает — символ не напечатается.
    /// </summary>
    public static bool Types(int vk) => vk is (>= 0x30 and <= 0x39) or (>= 0x41 and <= 0x5A) or (>= 0x60 and <= 0x6F)
        or (>= 0xBA and <= 0xC0) or (>= 0xDB and <= 0xDF) or 0xE2;

    /// <summary>Как назвать клавишу человеку: «Пробел», «Ё», «Caps Lock», «F8», «Tab», «Num 5», «Q».</summary>
    public static string DisplayName(string? name) => name?.ToLowerInvariant() switch
    {
        null or "" or "space" => Loc.T.KeySpace,
        "backquote" => Loc.T.KeyBackquote,
        "capslock" => "Caps Lock",
        _ when !ByName.ContainsKey(name) && ToVirtualKey(name) is { } vk => KeyName(vk),
        _ => name,
    };

    /// <summary>Подпись клавиши по коду (и для неподходящих — чтобы сказать, какая не подошла).</summary>
    public static string KeyName(int vk) => vk switch
    {
        0x0D => "Enter", Escape => "Esc", 0x2C => "Print Screen", 0x5F => "Sleep",
        0x20 => Loc.T.KeySpace,
        0xC0 => Loc.T.KeyBackquote,
        0x14 => "Caps Lock",
        (>= 0x30 and <= 0x39) or (>= 0x41 and <= 0x5A) => ((char)vk).ToString(),
        >= 0x60 and <= 0x69 => $"Num {vk - 0x60}",
        0x6A => "Num *", 0x6B => "Num +", 0x6D => "Num -", 0x6E => "Num .", 0x6F => "Num /",
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        0x08 => "Backspace", 0x09 => "Tab", 0x0C => "Clear", 0x13 => "Pause",
        0x21 => "Page Up", 0x22 => "Page Down", 0x23 => "End", 0x24 => "Home",
        0x25 => "←", 0x26 => "↑", 0x27 => "→", 0x28 => "↓",
        0x2D => "Insert", 0x2E => "Delete", 0x5D => "Menu", 0x90 => "Num Lock", 0x91 => "Scroll Lock",
        0xBA => ";", 0xBB => "=", 0xBC => ",", 0xBD => "-", 0xBE => ".", 0xBF => "/",
        0xDB => "[", 0xDC => "\\", 0xDD => "]", 0xDE => "'",
        _ => $"VK 0x{vk:X2}",
    };

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
