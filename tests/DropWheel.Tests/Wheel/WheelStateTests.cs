using DropWheel.Conversions;
using DropWheel.Wheel;

namespace DropWheel.Tests.Wheel;

public class WheelStateTests
{
    private static readonly WheelItem[] Conv = [new("to-jpg", "JPG"), new("to-png", "PNG")];
    private static readonly WheelItem[] Tools = [new("img-compress", "Сжать")];

    [Fact]
    public void Starts_in_convert_mode_and_toggles()
    {
        var s = new WheelState();
        s.SetItems(Conv, Tools);
        Assert.Equal(OperationMode.Convert, s.Mode);
        Assert.Equal(Conv, s.Items);

        s.Toggle();
        Assert.Equal(OperationMode.Tool, s.Mode);
        Assert.Equal(Tools, s.Items);

        s.Toggle();
        Assert.Equal(OperationMode.Convert, s.Mode);
    }

    [Fact]
    public void Opens_tools_when_there_is_nothing_to_convert()
    {
        var s = new WheelState();
        s.SetItems([], Tools);
        Assert.Equal(OperationMode.Tool, s.Mode);
        Assert.False(s.IsEmpty);
    }

    [Fact]
    public void New_items_reset_mode_to_convert()
    {
        var s = new WheelState();
        s.SetItems(Conv, Tools);
        s.Toggle();
        s.SetItems(Conv, Tools);
        Assert.Equal(OperationMode.Convert, s.Mode);
    }

    [Fact]
    public void Empty_state_is_nothing_to_offer()
    {
        var s = new WheelState();
        Assert.True(s.IsEmpty);
        s.SetItems([], []);
        Assert.True(s.IsEmpty);
        Assert.Equal(OperationMode.Convert, s.Mode);
        Assert.Null(s.At(0));
    }

    [Fact]
    public void At_returns_item_or_null()
    {
        var s = new WheelState();
        s.SetItems(Conv, Tools);
        Assert.Equal("to-png", s.At(1)?.Id);
        Assert.Null(s.At(null));
        Assert.Null(s.At(2));
        Assert.Null(s.At(-1));
    }

    [Fact]
    public void More_than_8_items_are_cut()
    {
        var many = Enumerable.Range(0, 10).Select(i => new WheelItem($"op{i}", $"{i}")).ToList();
        var s = new WheelState();
        s.SetItems(many, []);
        Assert.Equal(8, s.Items.Count);
    }
}
