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
    // Кусок входа (обрезка: «-ss 5 -to 12 -i …»): готово — доля длины куска, а не всего ролика.
    private double _from;
    private double? _to;

    /// <summary>Прогресс для этих аргументов ffmpeg: учитывает -ss / -to перед -i.</summary>
    public static FfmpegProgress For(IReadOnlyList<string> args)
    {
        var progress = new FfmpegProgress();
        var input = args.ToList().IndexOf("-i");
        for (var i = 0; i + 1 < input; i++)
        {
            if (args[i] == "-ss" && ParseTime(args[i + 1]) is { } from) progress._from = from;
            else if (args[i] == "-to" && ParseTime(args[i + 1]) is { } to) progress._to = to;
        }
        return progress;
    }

    /// <summary>Сколько секунд обработает ffmpeg: весь вход или кусок -ss…-to.</summary>
    private double Total
    {
        get
        {
            var end = _to is { } to ? (_durationSeconds > 0 ? Math.Min(to, _durationSeconds) : to) : _durationSeconds;
            return end - _from;
        }
    }

    /// <summary>Время ffmpeg: «12.5» (секунды) или «[ч:]мм:сс[.доли]».</summary>
    private static double? ParseTime(string text)
    {
        double total = 0;
        foreach (var part in text.Split(':'))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0) return null;
            total = total * 60 + v;
        }
        return total;
    }

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
            && long.TryParse(line.AsSpan(12), out var us) && us >= 0 && (_durationSeconds > 0 || _to is not null) && Total > 0)
            return Math.Clamp(us / 1_000_000.0 / Total, 0, 0.999);
        if (line == "progress=end") return 1.0;
        return null;
    }
}
