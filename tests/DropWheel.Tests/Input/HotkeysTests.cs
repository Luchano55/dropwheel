using DropWheel.Input;

namespace DropWheel.Tests.Input;

public class HotkeysTests
{
    [Theory]
    [InlineData("Space", 0x20)]
    [InlineData("Backquote", 0xC0)]
    [InlineData("CapsLock", 0x14)]
    [InlineData("F1", 0x70)]
    [InlineData("F5", 0x74)]
    [InlineData("F12", 0x7B)]
    public void Known_names(string name, int vk) => Assert.Equal(vk, Hotkeys.ToVirtualKey(name));

    [Theory]
    [InlineData("space")]
    [InlineData("SPACE")]
    [InlineData("capslock")]
    [InlineData("f12")]
    public void Case_insensitive(string name) => Assert.NotNull(Hotkeys.ToVirtualKey(name));

    [Theory]
    [InlineData("Shift")] // D7: Shift/Ctrl/Alt нужны проводнику во время перетаскивания
    [InlineData("Ctrl")]
    [InlineData("Alt")]
    [InlineData("Escape")]
    [InlineData("F0")]
    [InlineData("F13")]
    [InlineData("")]
    [InlineData(" Space")]
    [InlineData(null)]
    public void Unknown_names_give_null(string? name) => Assert.Null(Hotkeys.ToVirtualKey(name));

    [Fact]
    public void Names_list()
    {
        Assert.Equal(15, Hotkeys.Names.Count);
        Assert.Equal("Space", Hotkeys.Names[0]);
        Assert.Equal(Hotkeys.Default, Hotkeys.Names[0]);
        Assert.Contains("Backquote", Hotkeys.Names);
        Assert.Contains("CapsLock", Hotkeys.Names);
        Assert.Equal("F12", Hotkeys.Names[^1]);
        Assert.All(Hotkeys.Names, n => Assert.NotNull(Hotkeys.ToVirtualKey(n)));
        Assert.Equal(Hotkeys.Names.Count, Hotkeys.Names.Select(n => Hotkeys.ToVirtualKey(n)).Distinct().Count());
    }

    [Fact]
    public void All_names_are_valid_hotkeys_for_detector()
    {
        foreach (var name in Hotkeys.Names)
            _ = new DragDetector(Hotkeys.ToVirtualKey(name)!.Value);
    }

    [Fact]
    public void Escape_code() => Assert.Equal(0x1B, Hotkeys.Escape);

    [Theory]
    [InlineData("Space", "Пробел")]
    [InlineData("space", "Пробел")]
    [InlineData(null, "Пробел")]
    [InlineData("Backquote", "Ё")]
    [InlineData("CapsLock", "Caps Lock")]
    [InlineData("F8", "F8")]
    public void Display_names_for_people(string? name, string expected) =>
        Assert.Equal(expected, Hotkeys.DisplayName(name));

    // ---------- своя клавиша (D-HOTKEY) ----------

    [Theory]
    [InlineData(0x09, "vk:0x09", "Tab")]
    [InlineData(0x65, "vk:0x65", "Num 5")]
    [InlineData(0x51, "vk:0x51", "Q")]
    [InlineData(0x37, "vk:0x37", "7")]
    [InlineData(0x7C, "vk:0x7C", "F13")]
    [InlineData(0x2E, "vk:0x2E", "Delete")]
    [InlineData(0xBA, "vk:0xBA", ";")]
    [InlineData(0x77, "F8", "F8")]           // клавиша из списка — под своим именем
    [InlineData(0x20, "Space", "Пробел")]
    [InlineData(0xC0, "Backquote", "Ё")]
    public void Custom_key_round_trip(int vk, string name, string display)
    {
        Assert.Equal(name, Hotkeys.FromVirtualKey(vk));
        Assert.Equal(vk, Hotkeys.ToVirtualKey(name));
        Assert.Equal(display, Hotkeys.DisplayName(name));
        _ = new DragDetector(vk);
    }

    [Theory]
    [InlineData("vk:0x65", 0x65)]
    [InlineData("VK:0X65", 0x65)]
    [InlineData("vk:0x9", 0x09)]
    [InlineData("vk:0x20", 0x20)] // Пробел, записанный кодом, тоже читается
    public void Custom_names_parse(string name, int vk) => Assert.Equal(vk, Hotkeys.ToVirtualKey(name));

    [Theory]
    [InlineData("vk:")]
    [InlineData("vk:0x")]
    [InlineData("vk:0x100")]
    [InlineData("vk:0xZZ")]
    [InlineData("vk:0x-1")]
    [InlineData("vk:65")]
    [InlineData("vk:0x 9")]
    [InlineData("vk:0x1B")] // Esc
    [InlineData("vk:0x0D")] // Enter
    [InlineData("vk:0x10")] // Shift
    [InlineData("vk:0xA2")] // левый Ctrl
    [InlineData("vk:0x5B")] // Win
    [InlineData("vk:0x01")] // левая кнопка мыши
    public void Invalid_custom_names_give_null(string name)
    {
        Assert.Null(Hotkeys.ToVirtualKey(name));
        Assert.Equal(name, Hotkeys.DisplayName(name));
    }

    [Theory]
    [InlineData(0x10)] [InlineData(0x11)] [InlineData(0x12)]
    [InlineData(0xA0)] [InlineData(0xA1)] [InlineData(0xA2)] [InlineData(0xA3)] [InlineData(0xA4)] [InlineData(0xA5)]
    [InlineData(0x5B)] [InlineData(0x5C)]
    public void Modifiers_are_rejected(int vk)
    {
        Assert.Equal(HotkeyCheck.Modifier, Hotkeys.Check(vk));
        Assert.Null(Hotkeys.FromVirtualKey(vk));
    }

    [Theory]
    [InlineData(0x00)] [InlineData(0x01)] [InlineData(0x02)] [InlineData(0x04)]
    [InlineData(0x0D)] [InlineData(Hotkeys.Escape)] [InlineData(0x2C)] [InlineData(0x5F)]
    [InlineData(0xE5)] [InlineData(0xE7)] [InlineData(0xFF)] [InlineData(0x100)] [InlineData(-1)]
    public void Reserved_keys_are_rejected(int vk)
    {
        Assert.Equal(HotkeyCheck.Reserved, Hotkeys.Check(vk));
        Assert.Null(Hotkeys.FromVirtualKey(vk));
    }

    [Fact]
    public void Every_accepted_key_works_in_detector()
    {
        for (var vk = 0; vk < 256; vk++)
            if (Hotkeys.FromVirtualKey(vk) is { } name)
            {
                Assert.Equal(vk, Hotkeys.ToVirtualKey(name));
                _ = new DragDetector(vk);
            }
    }

    [Theory]
    [InlineData(0x41, true)] [InlineData(0x30, true)] [InlineData(0x65, true)] [InlineData(0xC0, true)] [InlineData(0xBA, true)]
    [InlineData(0x20, false)] [InlineData(0x77, false)] [InlineData(0x09, false)] [InlineData(0x14, false)]
    public void Typing_keys(int vk, bool types) => Assert.Equal(types, Hotkeys.Types(vk));

    [Fact]
    public void Custom_key_while_dragging_is_swallowed_only_during_drag()
    {
        const int q = 0x51;
        var d = new DragDetector(q);
        Assert.Equal((DragAction.None, false), d.KeyDown(q, 0)); // не тащим — буква печатается
        Assert.False(d.KeyUp(q));
        d.MouseDown(0, 0);
        d.MouseMove(50, 0);
        Assert.Equal((DragAction.ShowWheel, true), d.KeyDown(q, 100));
        Assert.True(d.KeyUp(q));
    }
}
