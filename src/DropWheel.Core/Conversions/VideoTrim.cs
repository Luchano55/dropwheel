using System.Globalization;

namespace DropWheel.Conversions;

/// <summary>
/// Обрезка видео по времени (решение D-TRIM): id операций, разбор и показ времени, значения {start}/{end}.
/// Окно «Обрезать…» и CLI (<c>--start 0:05 --end 0:12 [--exact | --gif]</c>) собирают задачу через <see cref="Bind"/>.
/// </summary>
public static class VideoTrim
{
    /// <summary>Без пересжатия: мгновенно, без потерь, начало — по опорному кадру.</summary>
    public const string Id = "video-trim";
    /// <summary>Точно по кадру: пересжатие в H.264 (дольше). В колесе не показывается.</summary>
    public const string ExactId = "video-trim-exact";
    /// <summary>Кусок → анимированный GIF (как «GIF» у видео: 15 кадров/с, до 640 px). В колесе не показывается.</summary>
    public const string GifId = "video-trim-gif";

    /// <summary>Кусок длиннее — окно предупреждает, что GIF выйдет большим (только подсказка, не запрет).</summary>
    public const double GifLongSeconds = 30;

    /// <summary>GIF из куска такой длины, скорее всего, будет очень большим.</summary>
    public static bool IsLongForGif(double seconds) => seconds > GifLongSeconds;

    public const string StartKey = "start", EndKey = "end";
    public static readonly IReadOnlyList<string> ParameterNames = [StartKey, EndKey];

    /// <summary>Самый короткий кусок, который можно выбрать, — 0,1 с.</summary>
    public const double MinLength = 0.1;

    /// <summary>Операция обрезки с началом и концом (секунды). Конец раньше начала, отрицательное — ArgumentException.</summary>
    public static Operation Bind(bool exact, double start, double end) => BindId(exact ? ExactId : Id, start, end);

    /// <summary>Кусок → GIF (<see cref="GifId"/>).</summary>
    public static Operation BindGif(double start, double end) => BindId(GifId, start, end);

    private static Operation BindId(string id, double start, double end)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end - start < MinLength / 2)
            throw new ArgumentException($"Неверный отрезок {start}…{end}");
        var op = OperationCatalog.Find(id)!;
        return op.WithValues(new Dictionary<string, string>
        {
            [StartKey] = Arg(start),
            [EndKey] = Arg(end),
        });
    }

    /// <summary>Секунды для ffmpeg: «5», «12.345» (точка, до миллисекунд).</summary>
    public static string Arg(double seconds) =>
        Math.Round(Math.Max(0, seconds), 3).ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Время, как его пишет человек: «12», «12.5», «12,5», «0:05», «1:02.5», «1:00:00». Пробелы по краям не мешают.
    /// </summary>
    public static bool TryParse(string? text, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Replace(',', '.').Split(':');
        if (parts.Length > 3) return false;
        double total = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0 || part.StartsWith('-') || part.StartsWith('+')) return false;
            if (!double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v)) return false;
            // Дробная часть — только у секунд, минуты и секунды после двоеточия — меньше 60.
            if (i < parts.Length - 1 && part.Contains('.')) return false;
            if (i > 0 && v >= 60) return false;
            total = total * 60 + v;
        }
        seconds = total;
        return double.IsFinite(total);
    }

    /// <summary>«мм:сс.д» (до часа) или «ч:мм:сс.д»: 5.24 → «00:05.2», 3725.5 → «1:02:05.5».</summary>
    public static string Format(double seconds)
    {
        var tenths = (long)Math.Round(Math.Max(0, seconds) * 10, MidpointRounding.AwayFromZero);
        var h = tenths / 36000;
        var m = tenths / 600 % 60;
        var s = tenths / 10 % 60;
        var d = tenths % 10;
        return h > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}.{3}", h, m, s, d)
            : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}.{2}", m, s, d);
    }
}

/// <summary>
/// Выбранный отрезок в окне обрезки: ручки «начало» и «конец» внутри [0; Duration], между ними не меньше
/// <see cref="VideoTrim.MinLength"/>. Ручка не перескакивает другую — упирается в неё.
/// </summary>
public sealed record TrimRange
{
    public TrimRange(double duration, double start = 0, double? end = null)
    {
        Duration = Math.Max(VideoTrim.MinLength, double.IsFinite(duration) ? duration : VideoTrim.MinLength);
        End = Math.Clamp(end ?? Duration, VideoTrim.MinLength, Duration);
        Start = Math.Clamp(start, 0, End - VideoTrim.MinLength);
    }

    public double Duration { get; }
    public double Start { get; private init; }
    public double End { get; private init; }
    public double Length => End - Start;
    /// <summary>Выбран весь ролик — обрезать нечего.</summary>
    public bool IsWhole => Start <= 0.0005 && End >= Duration - 0.0005;

    public TrimRange WithStart(double seconds) =>
        this with { Start = Math.Clamp(seconds, 0, End - VideoTrim.MinLength) };

    public TrimRange WithEnd(double seconds) =>
        this with { End = Math.Clamp(seconds, Start + VideoTrim.MinLength, Duration) };
}
