using System.Text.Encodings.Web;
using System.Text.Json;

namespace DemoDriver;

/// <summary>Отметка сценария: имя и Unix-время в миллисекундах.</summary>
internal sealed record TimelineEvent(string Name, long UnixMs);

/// <summary>
/// Журнал сценария для монтажа (timeline.json): когда что произошло и когда началась запись.
/// Монтаж по нему вырезает паузы, ускоряет ожидание и ставит подписи.
/// </summary>
internal sealed class Timeline
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Unix-время (мс) кадра 0 записи; null — записи нет.</summary>
    public long? T0UnixMs { get; set; }
    public double RecordedSeconds { get; set; }
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
    public List<TimelineEvent> Events { get; set; } = [];
    /// <summary>Что получилось: photoResult, videoResult — полные пути.</summary>
    public Dictionary<string, string> Results { get; set; } = [];
    public List<string> Errors { get; set; } = [];

    public void Mark(string name)
    {
        Events.Add(new TimelineEvent(name, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        Log.Write($"● отметка «{name}»");
    }

    public void Fail(string message)
    {
        Errors.Add(message);
        Log.Error(message);
    }

    public bool Has(string name) => Events.Any(e => e.Name == name);

    /// <summary>Секунда записи, на которую пришлось событие (первое с таким именем); null — нет события или записи.</summary>
    public double? At(string name)
    {
        if (T0UnixMs is not { } t0) return null;
        var e = Events.FirstOrDefault(x => x.Name == name);
        return e is null ? null : (e.UnixMs - t0) / 1000.0;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
        Log.Write($"Журнал сценария: {path}");
    }

    public static Timeline Load(string path) =>
        JsonSerializer.Deserialize<Timeline>(File.ReadAllText(path), Json) ?? new Timeline();
}
