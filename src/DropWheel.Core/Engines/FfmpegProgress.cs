using System.Globalization;
using System.Text.RegularExpressions;

namespace DropWheel.Engines;

/// <summary>
/// Прогресс ffmpeg: длительность берём из строки «Duration: 00:01:02.50» в stderr,
/// текущую позицию — из «out_time_us=…» потока -progress.
/// </summary>
internal sealed partial class FfmpegProgress
{
    private double _durationSeconds;

    [GeneratedRegex(@"^\s*Duration:\s*(\d+):(\d\d):(\d\d(?:\.\d+)?)")]
    private static partial Regex DurationRegex();

    public double DurationSeconds => _durationSeconds;

    public double? ReadStderr(string line)
    {
        // Берём первую длительность — это вход; у выхода её в stderr нет.
        if (_durationSeconds > 0) return null;
        var m = DurationRegex().Match(line);
        if (!m.Success) return null;
        _durationSeconds = int.Parse(m.Groups[1].Value) * 3600
            + int.Parse(m.Groups[2].Value) * 60
            + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        return null;
    }

    public double? ReadProgressLine(string line)
    {
        // out_time_ms, несмотря на имя, тоже в микросекундах — читаем out_time_us.
        if (line.StartsWith("out_time_us=", StringComparison.Ordinal)
            && long.TryParse(line.AsSpan(12), out var us) && us >= 0 && _durationSeconds > 0)
            return Math.Clamp(us / 1_000_000.0 / _durationSeconds, 0, 0.999);
        if (line == "progress=end") return 1.0;
        return null;
    }
}
