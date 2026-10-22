using DropWheel.Wheel;

namespace DropWheel.Tests.Wheel;

public class WheelFillTests
{
    [Theory]
    [InlineData(0xE0)]
    [InlineData(0xA6)]
    [InlineData(0x24)]
    [InlineData(0x00)]
    public void Default_keeps_the_original_look(byte alpha)
    {
        Assert.Equal(alpha, WheelFill.ScaleAlpha(alpha, WheelFill.DefaultPercent));
    }

    [Fact]
    public void Full_makes_the_solid_disc_opaque()
    {
        Assert.Equal(255, WheelFill.ScaleAlpha(0xE0, 100));
        Assert.Equal(255, WheelFill.ScaleAlpha(0xFF, 100)); // не переполняется
    }

    [Fact]
    public void Half_and_minimum_scale_proportionally()
    {
        Assert.Equal(Math.Round(0xE0 * 50 / 88.0), WheelFill.ScaleAlpha(0xE0, 50));
        Assert.Equal(Math.Round(0xA6 * 40 / 88.0), WheelFill.ScaleAlpha(0xA6, 40));
        Assert.True(WheelFill.ScaleAlpha(0xE0, 40) < WheelFill.ScaleAlpha(0xE0, 60));
    }

    [Theory]
    [InlineData(0, 40)]
    [InlineData(-5, 40)]
    [InlineData(1000, 100)]
    [InlineData(64, 64)]
    public void Out_of_range_percent_is_clamped(int percent, int expected)
    {
        Assert.Equal(expected, WheelFill.Clamp(percent));
        Assert.Equal(WheelFill.ScaleAlpha(0xE0, expected), WheelFill.ScaleAlpha(0xE0, percent));
    }

    [Fact]
    public void Default_matches_solid_disc_alpha()
    {
        // 0xE0 / 255 ≈ 87,8 % — исходный вид колеса и есть «88 %».
        Assert.Equal(WheelFill.DefaultPercent, (int)Math.Round(0xE0 * 100 / 255.0));
    }
}
