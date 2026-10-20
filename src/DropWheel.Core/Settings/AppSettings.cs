using System.Text.Encodings.Web;
using System.Text.Json;
using DropWheel.Conversions;

namespace DropWheel.Settings;

/// <summary>Настройки приложения (раздел 10 ТЗ), хранятся в settings.json.</summary>
public sealed record AppSettings
{
    /// <summary>Горячая клавиша колеса (имя клавиши).</summary>
    public string Hotkey { get; init; } = "Space";
    public bool ContextMenuEnabled { get; init; } = true;
    public bool AutoStart { get; init; }
    /// <summary>null — искать в tools/ рядом с exe.</summary>
    public string? FfmpegPath { get; init; }
    /// <summary>null — искать в tools/ рядом с exe.</summary>
    public string? MagickPath { get; init; }
    public ConversionSettings Conversion { get; init; } = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Кириллица в путях пишется как есть, а не М…, — файл можно читать глазами.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Нет файла — настройки по умолчанию. Битый файл переименовывается в settings.json.bad
    /// (старый .bad затирается), возвращаются настройки по умолчанию. Ошибки чтения не бросает.
    /// </summary>
    public static AppSettings Load(string path)
    {
        string text;
        try
        {
            if (!File.Exists(path)) return new();
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new();
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<AppSettings>(text, Json);
            if (loaded is not null) return Sanitize(loaded);
        }
        catch (JsonException)
        {
        }

        // Сюда попадаем, если JSON битый (или в файле «null»): откладываем файл в сторону.
        try { File.Move(path, path + ".bad", overwrite: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return new();
    }

    /// <summary>Атомарная запись: временный файл в той же папке, затем замена.</summary>
    public void Save(string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(this, Json));
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    /// <summary>Явный null в JSON затирает значение по умолчанию — возвращаем его.</summary>
    private static AppSettings Sanitize(AppSettings s) => s with
    {
        Hotkey = string.IsNullOrWhiteSpace(s.Hotkey) ? new AppSettings().Hotkey : s.Hotkey,
        Conversion = s.Conversion ?? new(),
    };
}
