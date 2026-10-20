using DropWheel.Wheel;

namespace DropWheel.Tests.Wheel;

public class WheelGeometryTests
{
    private const double Inner = 55, Outer = 130, R = 90;

    private static int? Hit(double angleDeg, int count, double radius = R)
    {
        var (x, y) = WheelGeometry.PointAt(angleDeg, radius);
        return WheelGeometry.HitTest(x, y, count, Inner, Outer);
    }

    [Fact]
    public void Four_sectors_top_right_bottom_left()
    {
        // Y вниз: сверху — отрицательный dy.
        Assert.Equal(0, WheelGeometry.HitTest(0, -R, 4, Inner, Outer));
        Assert.Equal(1, WheelGeometry.HitTest(R, 0, 4, Inner, Outer));
        Assert.Equal(2, WheelGeometry.HitTest(0, R, 4, Inner, Outer));
        Assert.Equal(3, WheelGeometry.HitTest(-R, 0, 4, Inner, Outer));
    }

    [Fact]
    public void Eight_sectors_go_clockwise_from_top()
    {
        for (var i = 0; i < 8; i++)
            Assert.Equal(i, Hit(i * 45, 8));
        // Чуть левее верха — всё ещё сектор 0, левее его границы — сектор 7.
        Assert.Equal(0, Hit(-22, 8));
        Assert.Equal(7, Hit(-23, 8));
        Assert.Equal(0, Hit(22, 8));
        Assert.Equal(1, Hit(23, 8));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Every_sector_is_hit_at_its_middle_and_near_its_edges(int count)
    {
        var span = 360.0 / count;
        for (var i = 0; i < count; i++)
        {
            var (start, end) = WheelGeometry.SectorAngles(i, count);
            Assert.Equal(span, end - start, 9);
            Assert.Equal(i, Hit(WheelGeometry.MidAngle(i, count), count));
            Assert.Equal(i, Hit(start + 0.5, count));
            Assert.Equal(i, Hit(end - 0.5, count));
            Assert.Equal(i, Hit(WheelGeometry.MidAngle(i, count), count, Inner + 0.001));
            Assert.Equal(i, Hit(WheelGeometry.MidAngle(i, count), count, Outer - 0.001));
        }
    }

    [Fact]
    public void Single_sector_covers_the_whole_ring()
    {
        Assert.Equal((-180.0, 180.0), WheelGeometry.SectorAngles(0, 1));
        foreach (var a in new[] { 0, 90, 179, 180, 181, 270, 359 })
            Assert.Equal(0, Hit(a, 1));
    }

    [Fact]
    public void Sector_zero_is_centered_on_top()
    {
        Assert.Equal((-45.0, 45.0), WheelGeometry.SectorAngles(0, 4));
        Assert.Equal((45.0, 135.0), WheelGeometry.SectorAngles(1, 4));
        Assert.Equal((-22.5, 22.5), WheelGeometry.SectorAngles(0, 8));
        Assert.Equal((292.5, 337.5), WheelGeometry.SectorAngles(7, 8));
    }

    [Fact]
    public void Exact_boundary_belongs_to_next_sector_clockwise()
    {
        Assert.Equal(1, WheelGeometry.HitTest(R, 0, 2, Inner, Outer));  // справа: граница 0|1
        Assert.Equal(0, WheelGeometry.HitTest(-R, 0, 2, Inner, Outer)); // слева: граница 1|0
    }

    [Fact]
    public void Center_and_outside_are_not_sectors()
    {
        Assert.Null(WheelGeometry.HitTest(0, 0, 4, Inner, Outer));
        Assert.Null(WheelGeometry.HitTest(0, -(Inner - 0.01), 4, Inner, Outer));
        Assert.Null(WheelGeometry.HitTest(0, -(Outer + 0.01), 4, Inner, Outer));
        Assert.Null(WheelGeometry.HitTest(200, 200, 4, Inner, Outer));

        Assert.True(WheelGeometry.IsCenter(0, 0, Inner));
        Assert.True(WheelGeometry.IsCenter(30, -30, Inner));
        Assert.False(WheelGeometry.IsCenter(0, Inner, Inner));
        Assert.False(WheelGeometry.IsCenter(R, 0, Inner));
    }

    [Fact]
    public void Ring_edges_are_inclusive()
    {
        Assert.Equal(0, WheelGeometry.HitTest(0, -Inner, 4, Inner, Outer));
        Assert.Equal(0, WheelGeometry.HitTest(0, -Outer, 4, Inner, Outer));
    }

    [Fact]
    public void No_items_means_no_hit()
    {
        Assert.Null(WheelGeometry.HitTest(0, -R, 0, Inner, Outer));
    }

    [Fact]
    public void Bad_sector_arguments_throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WheelGeometry.SectorAngles(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => WheelGeometry.SectorAngles(4, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => WheelGeometry.SectorAngles(-1, 4));
    }

    [Fact]
    public void AngleOf_matches_clock_directions()
    {
        Assert.Equal(0, WheelGeometry.AngleOf(0, -1), 9);
        Assert.Equal(90, WheelGeometry.AngleOf(1, 0), 9);
        Assert.Equal(180, WheelGeometry.AngleOf(0, 1), 9);
        Assert.Equal(270, WheelGeometry.AngleOf(-1, 0), 9);
    }

    [Theory]
    [InlineData(96, 300)]
    [InlineData(120, 375)]
    [InlineData(144, 450)]
    [InlineData(192, 600)]
    public void Dip_size_scales_with_monitor_dpi(int dpi, int expected)
    {
        Assert.Equal(expected, WheelGeometry.ToPixels(300, dpi));
    }

    [Fact]
    public void Window_is_centered_on_the_cursor_when_it_fits()
    {
        Assert.Equal((850, 390), WheelGeometry.PlaceInside(1000, 540, 300, 0, 0, 1920, 1040));
    }

    [Fact]
    public void Window_is_pushed_inside_the_work_area()
    {
        // Левый верхний угол основного монитора.
        Assert.Equal((0, 0), WheelGeometry.PlaceInside(10, 10, 300, 0, 0, 1920, 1040));
        // У панели задач внизу справа.
        Assert.Equal((1620, 740), WheelGeometry.PlaceInside(1915, 1075, 300, 0, 0, 1920, 1040));
        // Второй монитор слева, с отрицательными координатами.
        Assert.Equal((-2560, -200), WheelGeometry.PlaceInside(-2555, -190, 450, -2560, -200, 0, 1240));
    }
}
