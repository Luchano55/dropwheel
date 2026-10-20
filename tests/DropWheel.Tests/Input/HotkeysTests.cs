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
}
