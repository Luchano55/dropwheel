using System.Text.Encodings.Web;
using System.Text.Json;
using DropWheel.Conversions;
using DropWheel.Localization;
using DropWheel.Wheel;

namespace DropWheel.Settings;

/// <summary>Настройки приложения (раздел 10 ТЗ), хранятся в settings.json.</summary>
public sealed record AppSettings
{
    /// <summary>Горячая клавиша колеса: имя из <see cref="Input.Hotkeys.Names"/> или своя клавиша «vk:0x65» (D-HOTKEY).</summary>
    public string Hotkey { get; init; } = "Space";
    public bool ContextMenuEnabled { get; init; } = true;
    /// <summary>Ctrl+Alt+V — колесо для файлов или картинки из буфера обмена (решение D-CLIPBOARD).</summary>
    public bool ClipboardHotkey { get; init; } = true;
    /// <summary>Папка для картинок из буфера; null — как раньше: «В одну папку» или «Изображения\DropWheel».</summary>
    public string? ClipboardFolder { get; init; }
    /// <summary>false — после удачной конвертации PNG из буфера удаляется, остаётся только результат.</summary>
    public bool KeepClipboardImage { get; init; } = true;
    /// <summary>true — готовый результат сразу в буфер обмена: картинка — картинкой и файлом, остальное — файлом (D-COPYRESULT).</summary>
    public bool CopyResultToClipboard { get; init; }
    public bool AutoStart { get; init; }
    /// <summary>null — искать в tools/ рядом с exe.</summary>
    public string? FfmpegPath { get; init; }
    /// <summary>null — искать в tools/ рядом с exe.</summary>
    public string? MagickPath { get; init; }
    public ConversionSettings Conversion { get; init; } = new();
    /// <summary>Раз в сутки проверять на GitHub, нет ли новой сборки. Единственное обращение программы в сеть.</summary>
    public bool CheckForUpdates { get; init; } = true;
    /// <summary>Канал обновлений: «auto» — как при сборке (установщик версии — стабильные, dev-build — тестовые), «stable», «dev».</summary>
    public string UpdateChannel { get; init; } = Updates.UpdateChannels.Auto;
    /// <summary>Своё колесо: порядок и видимость лепестков по типам файлов, «последний выбор — первым».</summary>
    public WheelPreferences Wheel { get; init; } = new();
    /// <summary>Плотность заливки колеса, % (40–100; 88 — исходный вид), см. WheelFill.</summary>
    public int WheelOpacity { get; init; } = WheelFill.DefaultPercent;
    /// <summary>Язык интерфейса: «auto» — как у Windows (русский для русской, иначе английский), «ru», «en».</summary>
    public string Language { get; init; } = Loc.Auto;

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
        Hotkey = Input.Hotkeys.ToVirtualKey(s.Hotkey) is null ? new AppSettings().Hotkey : s.Hotkey, // неизвестная или запрещённая → Пробел
        Conversion = s.Conversion ?? new(),
        Wheel = (s.Wheel ?? new()).Sanitized(),
        Language = Loc.Normalize(s.Language),
        WheelOpacity = WheelFill.Clamp(s.WheelOpacity),
        ClipboardFolder = string.IsNullOrWhiteSpace(s.ClipboardFolder) ? null : s.ClipboardFolder,
        UpdateChannel = Updates.UpdateChannels.Normalize(s.UpdateChannel),
    };
}
