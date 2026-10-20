using DropWheel.Engines;

namespace DropWheel.Tests;

public class FfmpegProgressTests
{
    [Fact]
    public void Progress_is_position_over_duration()
    {
        var p = new FfmpegProgress();
        Assert.Null(p.ReadProgressLine("out_time_us=1000000")); // длительность ещё неизвестна
        p.ReadStderr("  Duration: 00:01:40.00, start: 0.000000, bitrate: 1000 kb/s");
        Assert.Equal(100, p.DurationSeconds, 3);
        Assert.Equal(0.25, p.ReadProgressLine("out_time_us=25000000")!.Value, 3);
        Assert.Null(p.ReadProgressLine("frame=100"));
        Assert.Equal(1.0, p.ReadProgressLine("progress=end"));
    }

    [Fact]
    public void Only_first_duration_counts()
    {
        var p = new FfmpegProgress();
        p.ReadStderr("  Duration: 01:00:00.5, start: 0");
        p.ReadStderr("  Duration: 00:00:01.00, start: 0");
        Assert.Equal(3600.5, p.DurationSeconds, 3);
    }

    [Fact]
    public void Negative_and_overflow_values_are_safe()
    {
        var p = new FfmpegProgress();
        p.ReadStderr("Duration: 00:00:10.00");
        Assert.Null(p.ReadProgressLine("out_time_us=-5"));
        Assert.True(p.ReadProgressLine("out_time_us=99000000") < 1.0);
    }
}
