using System.Globalization;

namespace DropWheel.Capture;

/// <summary>
/// Сочетание клавиш для RegisterHotKey («Ctrl+Alt+S», «Ctrl+Shift+PrintScreen», «F9»): модификаторы
/// в флагах MOD_* и код виртуальной клавиши. Хранится в settings.json строкой (<see cref="ToString"/>),
/// разбор — без учёта регистра и пробелов; «Control», «PrtSc», «Print» — тоже понимаются.
/// </summary>
public sealed record KeyChord(uint Modifiers, int Key)
{
    public const uint Alt = 0x0001, Control = 0x0002, Shift = 0x0004, Win = 0x0008;

    /// <summary>VK_SNAPSHOT.</summary>
    public const int PrintScreen = 0x2C;

    /// <summary>
    /// Снимок экрана по умолчанию — Ctrl+Alt+S (решение D-CAPTURE): «S» — «снимок» / «screenshot», пара к Ctrl+Alt+V
    /// (буфер обмена). Print Screen в Windows 11 обычно занят системным снимком, Ctrl+Shift+S во многих программах —
    /// «Сохранить как», Win+Shift+S — системный снимок.
    /// </summary>
    public const string DefaultText = "Ctrl+Alt+S";

    public static KeyChord Default { get; } = Parse(DefaultText)!;

    /// <summary>
    /// Разобрать «Ctrl+Alt+S». Клавиша — буква, цифра, F1–F24 или Print Screen. Букве и цифре нужен Ctrl, Alt или Win
    /// (одна буква или Shift+буква — это обычный набор текста); F-клавиши и Print Screen можно без модификаторов.
    /// Неверная строка → null.
    /// </summary>
    public static KeyChord? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Any(p => p.Length == 0)) return null;
        uint mods = 0;
        int? key = null;
        foreach (var part in parts)
        {
            if (Modifier(part) is { } m)
            {
                if ((mods & m) != 0) return null; // «Ctrl+Ctrl+S»
                mods |= m;
                continue;
            }
            if (key is not null || KeyCode(part) is not { } k) return null; // две клавиши или непонятное
            key = k;
        }
        if (key is not { } vk) return null;
        var chord = new KeyChord(mods, vk);
        return chord.IsAllowed ? chord : null;
    }

    /// <summary>Можно ли назначить: буквам и цифрам нужен Ctrl, Alt или Win.</summary>
    public bool IsAllowed => Key switch
    {
        (>= 0x30 and <= 0x39) or (>= 0x41 and <= 0x5A) => (Modifiers & (Control | Alt | Win)) != 0,
        PrintScreen or (>= 0x70 and <= 0x87) => true,
        _ => false,
    };

    /// <summary>
    /// Проверка сочетания, нажатого в настройках («Изменить…»): <paramref name="vk"/> — нажатая клавиша,
    /// <paramref name="modifiers"/> — зажатые модификаторы (флаги MOD_*). Строже <see cref="IsAllowed"/>:
    /// Win с буквой, цифрой или Print Screen Windows держит за собой (Win+Shift+S, Win+V, Win+цифры — панель задач,
    /// Win+Alt+R — запись, Win+Ctrl+D — рабочие столы и т. д.), RegisterHotKey их либо не даст, либо они не дойдут,
    /// поэтому Win подходит только с F1–F24.
    /// </summary>
    public static ChordCheck Check(uint modifiers, int vk)
    {
        if (IsModifierKey(vk)) return ChordCheck.ModifierOnly;
        var letterOrDigit = vk is (>= 0x30 and <= 0x39) or (>= 0x41 and <= 0x5A);
        if (!letterOrDigit && vk is not PrintScreen and not (>= 0x70 and <= 0x87)) return ChordCheck.UnsupportedKey;
        if ((modifiers & Win) != 0 && (letterOrDigit || vk == PrintScreen)) return ChordCheck.WindowsOwned;
        return new KeyChord(modifiers & (Alt | Control | Shift | Win), vk).IsAllowed ? ChordCheck.Ok : ChordCheck.NeedsModifier;
    }

    /// <summary>Shift, Ctrl, Alt, Win (левые, правые и общие коды VK).</summary>
    public static bool IsModifierKey(int vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);

    /// <summary>Каноническая запись: «Ctrl+Alt+Shift+Win+S» (порядок модификаторов фиксированный).</summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if ((Modifiers & Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & Alt) != 0) parts.Add("Alt");
        if ((Modifiers & Shift) != 0) parts.Add("Shift");
        if ((Modifiers & Win) != 0) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join('+', parts);
    }

    /// <summary>Для settings.json: верная строка — в канонической записи, иначе — по умолчанию.</summary>
    public static string Normalize(string? text) => (Parse(text) ?? Default).ToString();

    private static uint? Modifier(string part) => part.ToLowerInvariant() switch
    {
        "ctrl" or "control" or "ctl" => Control,
        "alt" => Alt,
        "shift" => Shift,
        "win" or "windows" => Win,
        _ => null,
    };

    private static int? KeyCode(string part)
    {
        var p = part.ToUpperInvariant();
        if (p.Length == 1 && p[0] is (>= 'A' and <= 'Z') or (>= '0' and <= '9')) return p[0];
        if (p is "PRINTSCREEN" or "PRTSC" or "PRTSCN" or "PRINT" or "PRINT SCREEN") return PrintScreen;
        if (p.Length is 2 or 3 && p[0] == 'F' && int.TryParse(p.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            && n is >= 1 and <= 24 && p[1] != '0')
            return 0x70 + n - 1;
        return null;
    }

    private static string KeyName(int vk) => vk switch
    {
        PrintScreen => "PrintScreen",
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        _ => ((char)vk).ToString(),
    };

    /// <summary>Как показать человеку: «Ctrl+Alt+S», «Ctrl+Print Screen».</summary>
    public string DisplayName => ToString().Replace("PrintScreen", "Print Screen", StringComparison.Ordinal);
}

/// <summary>Итог <see cref="KeyChord.Check"/>.</summary>
public enum ChordCheck
{
    Ok,
    /// <summary>Нажат только модификатор — ждём клавишу дальше.</summary>
    ModifierOnly,
    /// <summary>Буква или цифра без Ctrl, Alt или Win — это обычный набор текста.</summary>
    NeedsModifier,
    /// <summary>Win + буква, цифра или Print Screen — сочетания Windows.</summary>
    WindowsOwned,
    /// <summary>Не буква, не цифра, не F1–F24 и не Print Screen.</summary>
    UnsupportedKey,
}
